using System.ComponentModel;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Win")]
public sealed class WindowsPublicationSetTests
{
    [Test]
    [Platform("Win")]
    public void RemotePublicationPathIsRejected()
    {
        string? exceptionType = null;
        try
        {
            WindowsPathIdentity.RequireLocalPath(
                @"\\server\share\SharpProof\result.json");
        }
        catch (ArgumentException exception)
        {
            exceptionType = exception.GetType().FullName;
        }
        catch (IOException exception)
        {
            exceptionType = exception.GetType().FullName;
        }

        Assert.That(
            exceptionType,
            Is.EqualTo(typeof(ArgumentException).FullName));
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
                using var publication = WindowsPathIdentity.AcquirePublicationSet(
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

        using var first = WindowsPathIdentity.AcquirePublicationSet(
            firstPaths,
            TimeSpan.FromSeconds(1));
        var error = Task.Run(() => Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = WindowsPathIdentity.AcquirePublicationSet(
                secondPaths,
                TimeSpan.FromMilliseconds(100));
        }))).GetAwaiter().GetResult();

        Assert.That(
            error!.Message,
            Does.Contain("Timed out waiting for SharpProof publication paths"));
    }

    [Test]
    public void DisjointPublicationSetsProceedConcurrently()
    {
        using var directory = TemporaryDirectory.Create();
        using var first = WindowsPathIdentity.AcquirePublicationSet(
            CreatePaths(directory.Path, "first"),
            TimeSpan.FromSeconds(1));

        var completed = Task.Run(() =>
        {
            using var second = WindowsPathIdentity.AcquirePublicationSet(
                CreatePaths(directory.Path, "second"),
                TimeSpan.FromSeconds(1));
        }).Wait(TimeSpan.FromSeconds(2));

        Assert.That(completed, Is.True);
    }

    [Test]
    public void TimeoutReleasesMutexesAcquiredEarlierInTheSet()
    {
        using var directory = TemporaryDirectory.Create();
        var ordered = new[]
        {
            Path.Combine(directory.Path, "first.json"),
            Path.Combine(directory.Path, "second.json")
        }.OrderBy(WindowsPathIdentity.PublicationMutexName, StringComparer.Ordinal)
            .ToArray();
        using var blocked = WindowsPathIdentity.AcquirePublicationSet(
            [ordered[1]],
            TimeSpan.FromSeconds(1));

        var error = Task.Run(() => Assert.Throws<IOException>((Action)(() =>
        {
            using var attempted = WindowsPathIdentity.AcquirePublicationSet(
                ordered,
                TimeSpan.FromMilliseconds(100));
        }))).GetAwaiter().GetResult();
        using var reacquired = WindowsPathIdentity.AcquirePublicationSet(
            [ordered[0]],
            TimeSpan.FromSeconds(1));

        Assert.That(error!.Message, Does.Contain("Timed out"));
    }

    [Test]
    public void PersistentMetadataRejectsSequentialPartialOverlap()
    {
        using var directory = TemporaryDirectory.Create();
        var firstPaths = CreatePaths(directory.Path, "first");
        using (WindowsPathIdentity.AcquirePublicationSet(
                   firstPaths,
                   TimeSpan.FromSeconds(1)))
        {
        }
        var secondPaths = CreatePaths(directory.Path, "second");
        secondPaths[0] = firstPaths[0];

        var error = Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = WindowsPathIdentity.AcquirePublicationSet(
                secondPaths,
                TimeSpan.FromSeconds(1));
        }));

        Assert.That(error!.Message, Does.Contain("partially overlap"));
    }

    [Test]
    public void PublicationPathCannotAliasAnotherPathsMetadata()
    {
        using var directory = TemporaryDirectory.Create();
        var result = Path.Combine(directory.Path, "result.json");
        var marker = WindowsPathIdentity.PublicationMarkerPath(result);

        var error = Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = WindowsPathIdentity.AcquirePublicationSet(
                [result, marker],
                TimeSpan.FromSeconds(1));
        }));

        Assert.That(error!.Message, Does.Contain("publication metadata"));
    }

    [Test]
    public void InvalidPublicationArgumentsFailBeforeTouchingDisk()
    {
        var pathError = Assert.Throws<ArgumentException>((Action)(() =>
            WindowsPathIdentity.Canonicalize(" ")));
        var nullError = Assert.Throws<ArgumentNullException>((Action)(() =>
            WindowsPathIdentity.AcquirePublicationSet(
                null!,
                TimeSpan.FromSeconds(1))));
        var timeoutError = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            WindowsPathIdentity.AcquirePublicationSet(
                ["result.json"],
                TimeSpan.Zero)));
        var emptyError = Assert.Throws<ArgumentException>((Action)(() =>
            WindowsPathIdentity.AcquirePublicationSet(
                ["", " "],
                TimeSpan.FromSeconds(1))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pathError!.ParamName, Is.EqualTo("path"));
            Assert.That(nullError!.ParamName, Is.EqualTo("publicationPaths"));
            Assert.That(timeoutError!.ParamName, Is.EqualTo("timeout"));
            Assert.That(emptyError!.ParamName, Is.EqualTo("publicationPaths"));
        }
    }

    [Test]
    public void CanonicalizationRejectsFileAsAncestor()
    {
        using var directory = TemporaryDirectory.Create();
        var ancestor = Path.Combine(directory.Path, "ancestor.txt");
        File.WriteAllText(ancestor, "not a directory");

        var error = Assert.Throws<ArgumentException>((Action)(() =>
            WindowsPathIdentity.Canonicalize(
                Path.Combine(ancestor, "child.json"))));

        Assert.That(error!.Message, Does.Contain("ancestors must be directories"));
    }

    [Test]
    public void UnsupportedNamespacesAndAlternateDataStreamsAreRejected()
    {
        using var directory = TemporaryDirectory.Create();
        var file = Path.Combine(directory.Path, "result.json");
        File.WriteAllText(file, "stable");

        var remote = Assert.Throws<ArgumentException>((Action)(() =>
            WindowsPathIdentity.RequireLocalPath(
                @"\\?\UNC\server\share\result.json")));
        var globalRoot = Assert.Throws<ArgumentException>((Action)(() =>
            WindowsPathIdentity.Canonicalize(
                @"\\?\GLOBALROOT\Device\HarddiskVolume1\result.json")));
        var device = Assert.Throws<ArgumentException>((Action)(() =>
            WindowsPathIdentity.Canonicalize(@"\\.\PhysicalDrive0")));
        var stream = Assert.Throws<ArgumentException>((Action)(() =>
            WindowsPathIdentity.Canonicalize(file + ":stream")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remote!.Message, Does.Contain("local Windows path"));
            Assert.That(globalRoot!.Message, Does.Contain("device namespaces"));
            Assert.That(device!.Message, Does.Contain("device namespaces"));
            Assert.That(stream!.Message, Does.Contain("alternate data streams"));
        }
    }

    [Test]
    public void MissingDriveHasNoExistingAncestor()
    {
        var used = DriveInfo.GetDrives()
            .Select(static drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();
        var unused = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(static value => (char)value)
            .FirstOrDefault(letter => !used.Contains(letter));
        if (unused == default)
        {
            Assert.Ignore("No unused local drive letter is available.");
        }

        var error = Assert.Throws<Win32Exception>((Action)(() =>
            WindowsPathIdentity.Canonicalize(
                $@"{unused}:\SharpProof\missing.json")));

        Assert.That(error!.Message, Does.Contain("existing path ancestor"));
    }

    [Test]
    public void AbandonedPublicationMutexIsRecoveredAndMarkerWritten()
    {
        using var directory = TemporaryDirectory.Create();
        var result = Path.Combine(directory.Path, "result.json");
        using var mutex = new Mutex(
            false,
            WindowsPathIdentity.PublicationMutexName(result));
        var owner = new Thread(() => Assert.That(
            mutex.WaitOne(TimeSpan.FromSeconds(1)),
            Is.True));
        owner.Start();
        Assert.That(owner.Join(TimeSpan.FromSeconds(2)), Is.True);

        using var publication = WindowsPathIdentity.AcquirePublicationSet(
            [result],
            TimeSpan.FromSeconds(1));
        var marker = File.ReadAllText(
            WindowsPathIdentity.PublicationMarkerPath(result));

        Assert.That(marker, Does.StartWith("SharpProof.PublicationSet/1\n"));
        Assert.That(marker.TrimEnd('\n').Split('\n')[1], Has.Length.EqualTo(64));
    }

    [Test]
    public void InvalidationRejectsUnsafeShapes()
    {
        using var directory = TemporaryDirectory.Create();
        var missing = Path.Combine(directory.Path, "missing.json");
        var candidate = Path.Combine(directory.Path, "result.json");
        File.WriteAllText(candidate, "stable");

        var nullError = Assert.Throws<ArgumentNullException>((Action)(() =>
            WindowsPathIdentity.DeleteIfUnprotected(candidate, null!)));
        var directoryError = Assert.Throws<InvalidOperationException>((Action)(() =>
            WindowsPathIdentity.DeleteIfUnprotected(directory.Path, [])));
        var aliasError = Assert.Throws<InvalidOperationException>((Action)(() =>
            WindowsPathIdentity.DeleteIfUnprotected(candidate, [candidate])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WindowsPathIdentity.IsSameOrDescendant(
                    directory.Path,
                    directory.Path),
                Is.True);
            Assert.That(nullError!.ParamName, Is.EqualTo("protectedPaths"));
            Assert.That(
                WindowsPathIdentity.DeleteIfUnprotected(missing, []),
                Is.False);
            Assert.That(directoryError!.Message, Does.Contain("must be files"));
            Assert.That(aliasError!.Message, Does.Contain("aliases a protected file"));
            Assert.That(File.ReadAllText(candidate), Is.EqualTo("stable"));
        }
    }

    [Test]
    public void SharingViolationPreventsInvalidation()
    {
        using var directory = TemporaryDirectory.Create();
        var candidate = Path.Combine(directory.Path, "result.json");
        File.WriteAllText(candidate, "stable");
        using var blocker = new FileStream(
            candidate,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        var error = Assert.Throws<Win32Exception>((Action)(() =>
            WindowsPathIdentity.DeleteIfUnprotected(candidate, [])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.NativeErrorCode, Is.EqualTo(32));
            Assert.That(error.Message, Does.Contain("open an output for invalidation"));
            Assert.That(File.Exists(candidate), Is.True);
        }
    }

    [Test]
    public void ReadOnlyOutputCannotBeInvalidated()
    {
        using var directory = TemporaryDirectory.Create();
        var candidate = Path.Combine(directory.Path, "result.json");
        File.WriteAllText(candidate, "stable");
        File.SetAttributes(candidate, FileAttributes.ReadOnly);
        try
        {
            var error = Assert.Throws<Win32Exception>((Action)(() =>
                WindowsPathIdentity.DeleteIfUnprotected(candidate, [])));

            Assert.That(error!.Message, Does.Contain("could not invalidate an output"));
            Assert.That(File.Exists(candidate), Is.True);
        }
        finally
        {
            File.SetAttributes(candidate, FileAttributes.Normal);
        }
    }

    [Test]
    public void CanonicalizationGrowsFinalPathBuffer()
    {
        using var directory = TemporaryDirectory.Create();
        var current = directory.Path;
        while (current.Length <= 540)
        {
            current = Path.Combine(current, new string('a', 40));
            Directory.CreateDirectory(current);
        }
        var missing = Path.Combine(current, "result.json");

        var canonical = WindowsPathIdentity.Canonicalize(missing);

        Assert.That(canonical, Is.EqualTo(Path.GetFullPath(missing)).IgnoreCase);
        Assert.That(canonical, Has.Length.GreaterThan(512));
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
