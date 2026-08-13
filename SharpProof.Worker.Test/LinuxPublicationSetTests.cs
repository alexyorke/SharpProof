using System.Diagnostics;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
public sealed class LinuxPublicationSetTests
{
    [Test]
    public void InvalidAndNonRegularPublicationPathsFailClosed()
    {
        using var directory = TemporaryDirectory.Create();
        Assert.Throws<ArgumentException>((Action)(() =>
            LinuxPathIdentity.Canonicalize(" ")));
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                Array.Empty<string>(),
                TimeSpan.FromSeconds(1));
        }));

        var missing = Path.Combine(directory.Path, "missing.json");
        Assert.That(
            LinuxPathIdentity.DeleteIfUnprotected(
                missing,
                Array.Empty<string>()),
            Is.False);

        var outputDirectory = Directory.CreateDirectory(
            Path.Combine(directory.Path, "directory-output")).FullName;
        Assert.Throws<InvalidOperationException>((Action)(() =>
            LinuxPathIdentity.DeleteIfUnprotected(
                outputDirectory,
                Array.Empty<string>())));
    }

    [Test]
    public void SameSetInDifferentOrdersSerializesWithoutDeadlock()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = CreatePaths(directory.Path, "shared");
        using var start = new Barrier(2);
        var entered = 0;

        Task RunAsync(IEnumerable<string> requested)
        {
            return Task.Run(() =>
            {
                start.SignalAndWait();
                using var publication = LinuxPathIdentity.AcquirePublicationSet(
                    requested,
                    TimeSpan.FromSeconds(2));
                Interlocked.Increment(ref entered);
                Thread.Sleep(50);
            });
        }

        var first = RunAsync(paths);
        var second = RunAsync(paths.Reverse());

        Assert.That(
            Task.WaitAll([first, second], TimeSpan.FromSeconds(5)),
            Is.True);
        Assert.That(entered, Is.EqualTo(2));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void OverlapOnAnyPublicationMemberBlocks(int sharedIndex)
    {
        using var directory = TemporaryDirectory.Create();
        var firstPaths = CreatePaths(directory.Path, "first");
        var secondPaths = CreatePaths(directory.Path, "second");
        secondPaths[sharedIndex] = firstPaths[sharedIndex];

        using var first = LinuxPathIdentity.AcquirePublicationSet(
            firstPaths,
            TimeSpan.FromSeconds(1));
        var error = Task.Run(() => Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                secondPaths,
                TimeSpan.FromMilliseconds(100));
        }))).GetAwaiter().GetResult();

        Assert.That(
            error!.Message,
            Does.Contain("Timed out waiting for SharpProof publication paths"));
    }

    [Test]
    public void TimeoutReleasesLocksAcquiredEarlierInTheSet()
    {
        using var directory = TemporaryDirectory.Create();
        var ordered = new[]
        {
            Path.Combine(directory.Path, "first.json"),
            Path.Combine(directory.Path, "second.json")
        }.OrderBy(LinuxPathIdentity.PublicationLockName, StringComparer.Ordinal)
            .ToArray();
        using var blocked = LinuxPathIdentity.AcquirePublicationSet(
            [ordered[1]],
            TimeSpan.FromSeconds(1));

        var error = Task.Run(() => Assert.Throws<IOException>((Action)(() =>
        {
            using var attempted = LinuxPathIdentity.AcquirePublicationSet(
                ordered,
                TimeSpan.FromMilliseconds(100));
        }))).GetAwaiter().GetResult();
        using var reacquired = LinuxPathIdentity.AcquirePublicationSet(
            [ordered[0]],
            TimeSpan.FromSeconds(1));

        Assert.That(error!.Message, Does.Contain("Timed out"));
    }

    [Test]
    public void PersistentMetadataRejectsSequentialPartialOverlap()
    {
        using var directory = TemporaryDirectory.Create();
        var firstPaths = CreatePaths(directory.Path, "first");
        using (LinuxPathIdentity.AcquirePublicationSet(
                   firstPaths,
                   TimeSpan.FromSeconds(1)))
        {
        }
        var secondPaths = CreatePaths(directory.Path, "second");
        secondPaths[0] = firstPaths[0];

        var error = Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                secondPaths,
                TimeSpan.FromSeconds(1));
        }));

        Assert.That(error!.Message, Does.Contain("partially overlap"));
    }

    [Test]
    public void PublicationMetadataNamespaceIsReserved()
    {
        using var directory = TemporaryDirectory.Create();
        var result = Path.Combine(directory.Path, "result.json");
        var marker = LinuxPathIdentity.PublicationMarkerPath(result);

        var aliasError = Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [result, marker],
                TimeSpan.FromSeconds(1));
        }));
        var reservedError = Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [Path.Combine(
                    directory.Path,
                    "foreign.sharpproof-publication-lock")],
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(aliasError!.Message, Does.Contain("publication metadata"));
            Assert.That(reservedError!.Message, Does.Contain("publication metadata"));
            Assert.That(File.Exists(marker), Is.False);
        }
    }

    [Test]
    public void ExistingUnownedDestinationCannotBeAdopted()
    {
        using var directory = TemporaryDirectory.Create();
        var result = Path.Combine(directory.Path, "result.json");
        const string sentinel = "user-owned source bytes";
        File.WriteAllText(result, sentinel);

        var error = Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [result],
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("pre-existing"));
            Assert.That(File.ReadAllText(result), Is.EqualTo(sentinel));
            Assert.That(
                File.Exists(LinuxPathIdentity.PublicationMarkerPath(result)),
                Is.False);
        }
    }

    [Test]
    public void OwnershipMarkerSymlinkIsRejectedWithoutModifyingItsTarget()
    {
        using var directory = TemporaryDirectory.Create();
        var result = Path.Combine(directory.Path, "result.json");
        using (LinuxPathIdentity.AcquirePublicationSet(
                   [result],
                   TimeSpan.FromSeconds(1)))
        {
        }

        var marker = LinuxPathIdentity.PublicationMarkerPath(result);
        var target = Path.Combine(directory.Path, "user-owned-marker.txt");
        File.Move(marker, target);
        var expected = File.ReadAllBytes(target);
        File.CreateSymbolicLink(marker, target);

        Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [result],
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllBytes(target), Is.EqualTo(expected));
            Assert.That(
                new FileInfo(marker).LinkTarget,
                Is.EqualTo(target));
        }
    }

    [Test]
    public void RejectedPartialOverlapLeavesNoNewOwnershipMarkers()
    {
        using var directory = TemporaryDirectory.Create();
        var shared = Path.Combine(directory.Path, "z-shared.json");
        using (LinuxPathIdentity.AcquirePublicationSet(
                   [shared, Path.Combine(directory.Path, "z-first.json")],
                   TimeSpan.FromSeconds(1)))
        {
        }
        var disjoint = Path.Combine(directory.Path, "a-disjoint.json");

        Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [disjoint, shared],
                TimeSpan.FromSeconds(1));
        }));

        Assert.That(
            File.Exists(LinuxPathIdentity.PublicationMarkerPath(disjoint)),
            Is.False);
    }

    [Test]
    public void SymbolicLinksAndNonDirectoryAncestorsAreRejected()
    {
        using var directory = TemporaryDirectory.Create();
        var real = Directory.CreateDirectory(
            Path.Combine(directory.Path, "real")).FullName;
        var link = Path.Combine(directory.Path, "link");
        Directory.CreateSymbolicLink(link, real);
        var file = Path.Combine(directory.Path, "file.txt");
        File.WriteAllText(file, "not a directory");

        var linkError = Assert.Throws<ArgumentException>((Action)(() =>
            LinuxPathIdentity.Canonicalize(Path.Combine(link, "result.json"))));
        var ancestorError = Assert.Throws<ArgumentException>((Action)(() =>
            LinuxPathIdentity.Canonicalize(Path.Combine(file, "result.json"))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(linkError!.Message, Does.Contain("symbolic links"));
            Assert.That(
                ancestorError!.Message,
                Does.Contain("ancestors must be directories"));
        }
    }

    [Test]
    public void LocalPathsSupportSpacesPercentUnicodeAndLongNames()
    {
        using var directory = TemporaryDirectory.Create();
        var current = Path.Combine(directory.Path, "space % 雪");
        Directory.CreateDirectory(current);
        while (current.Length <= 300)
        {
            current = Path.Combine(current, new string('a', 40));
            Directory.CreateDirectory(current);
        }
        var result = Path.Combine(current, "result.json");

        var canonical = LinuxPathIdentity.RequireLocalPath(result);

        Assert.That(canonical, Is.EqualTo(Path.GetFullPath(result)));
        Assert.That(canonical, Has.Length.GreaterThan(260));
    }

    [Test]
    public void InvalidationPreservesProtectedFileIdentity()
    {
        using var directory = TemporaryDirectory.Create();
        var protectedPath = Path.Combine(directory.Path, "protected.json");
        var alias = Path.Combine(directory.Path, "alias.json");
        File.WriteAllText(protectedPath, "stable");
        var linkStart = new ProcessStartInfo
        {
            FileName = "/usr/bin/ln",
            UseShellExecute = false
        };
        linkStart.ArgumentList.Add(protectedPath);
        linkStart.ArgumentList.Add(alias);
        using (var link = Process.Start(linkStart) ??
               throw new InvalidOperationException("The hard-link helper did not start."))
        {
            Assert.That(link.WaitForExit(5_000), Is.True);
            Assert.That(link.ExitCode, Is.Zero);
        }

        var error = Assert.Throws<InvalidOperationException>((Action)(() =>
            LinuxPathIdentity.DeleteIfUnprotected(alias, [protectedPath])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("aliases a protected file"));
            Assert.That(File.ReadAllText(protectedPath), Is.EqualTo("stable"));
            Assert.That(File.Exists(alias), Is.True);
        }
    }

    private static string[] CreatePaths(string directory, string prefix)
    {
        return
        [
            Path.Combine(directory, prefix + ".request.json"),
            Path.Combine(directory, prefix + ".result.json"),
            Path.Combine(directory, prefix + ".manifest.json"),
            Path.Combine(directory, prefix + ".sarif.json")
        ];
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        internal string Path { get; }

        internal static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SharpProof.PublicationSet." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
