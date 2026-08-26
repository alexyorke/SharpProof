using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[Platform("Linux")]
[SupportedOSPlatform("linux")]
public sealed class LinuxPublicationSetTests
{
    [Test]
    public void StatFsInteropLayoutMatchesLinuxAmd64Abi()
    {
        var statsType = typeof(LinuxPathIdentity).GetNestedType(
            "LinuxFileSystemStats",
            BindingFlags.NonPublic);

        Assert.That(statsType, Is.Not.Null);
        Assert.That(Marshal.SizeOf(statsType!), Is.EqualTo(120));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void NestedPublicationSetsFailBeforeAnyFilesystemMutation(
        bool descendantFirst,
        bool threeLevels)
    {
        using var directory = TemporaryDirectory.Create();
        var parent = Path.Combine(directory.Path, "result.json");
        var descendant = threeLevels
            ? Path.Combine(parent, "middle", "child.json")
            : Path.Combine(parent, "child.json");
        var paths = descendantFirst
            ? new[] { descendant, parent }
            : new[] { parent, descendant };

        var error = Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                paths,
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("ancestor"));
            Assert.That(
                Directory.EnumerateFileSystemEntries(
                    directory.Path,
                    "*",
                    SearchOption.AllDirectories),
                Is.Empty);
        }
    }

    [Test]
    public void DuplicatePublicationPathsFailBeforeAnyFilesystemMutation()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "result.json");
        var alias = Path.Combine(directory.Path, ".", "result.json");

        var error = Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [output, alias],
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("duplicate"));
            Assert.That(
                Directory.EnumerateFileSystemEntries(
                    directory.Path,
                    "*",
                    SearchOption.AllDirectories),
                Is.Empty);
        }
    }

    [Test]
    public void NestedSetPreservesAPreExistingParentDirectoryWithoutMetadata()
    {
        using var directory = TemporaryDirectory.Create();
        var parent = Directory.CreateDirectory(
            Path.Combine(directory.Path, "result.json")).FullName;
        var child = Path.Combine(parent, "child.json");

        Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [child, parent],
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(parent), Is.True);
            Assert.That(
                Directory.EnumerateFileSystemEntries(parent),
                Is.Empty);
            Assert.That(
                Directory.EnumerateFileSystemEntries(directory.Path),
                Is.EqualTo(new[] { parent }));
        }
    }

    [Test]
    public void DisjointPublicationPathsUnderExistingParentsRemainRetryable()
    {
        using var directory = TemporaryDirectory.Create();
        var firstParent = Directory.CreateDirectory(
            Path.Combine(directory.Path, "first-parent")).FullName;
        var secondParent = Directory.CreateDirectory(
            Path.Combine(directory.Path, "second-parent", "nested")).FullName;
        var paths = new[]
        {
            Path.Combine(firstParent, "result.json"),
            Path.Combine(firstParent, "result.sarif.json"),
            Path.Combine(secondParent, "manifest.json")
        };

        using (LinuxPathIdentity.AcquirePublicationSet(
                   paths,
                   TimeSpan.FromSeconds(1)))
        {
        }
        using var retry = LinuxPathIdentity.AcquirePublicationSet(
            paths.Reverse(),
            TimeSpan.FromSeconds(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paths, Has.None.Matches<string>(File.Exists));
            Assert.That(
                paths.Select(LinuxPathIdentity.PublicationLockName),
                Has.All.Matches<string>(File.Exists));
            Assert.That(
                paths.Select(LinuxPathIdentity.PublicationMarkerPath),
                Has.All.Matches<string>(File.Exists));
        }
    }

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
        Assert.Throws<IOException>((Action)(() =>
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

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ConstructorFailureDisposesEveryEarlierLock(int failureIndex)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        using var directory = TemporaryDirectory.Create();
        var ordered = Enumerable.Range(0, 3)
            .Select(index => Path.Combine(directory.Path, $"output-{index}.json"))
            .OrderBy(LinuxPathIdentity.PublicationLockName, StringComparer.Ordinal)
            .ToArray();
        var lockPaths = ordered.Select(LinuxPathIdentity.PublicationLockName)
            .ToArray();
        var metadataDirectory = Path.GetDirectoryName(lockPaths[0])!;
        Directory.CreateDirectory(metadataDirectory);
        File.SetUnixFileMode(
            metadataDirectory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        Directory.CreateDirectory(lockPaths[failureIndex]);
        var before = CountOwnedFileDescriptors(metadataDirectory);

        for (var attempt = 0; attempt < 32; attempt++)
        {
            Assert.Throws<IOException>((Action)(() =>
            {
                using var publication = LinuxPathIdentity.AcquirePublicationSet(
                    ordered,
                    TimeSpan.FromSeconds(1));
            }));
        }
        var after = CountOwnedFileDescriptors(metadataDirectory);
        Directory.Delete(lockPaths[failureIndex]);
        using var reacquired = LinuxPathIdentity.AcquirePublicationSet(
            ordered,
            TimeSpan.FromSeconds(1));

        Assert.That(after, Is.EqualTo(before));

        static int CountOwnedFileDescriptors(string directory)
        {
            var prefix = directory + Path.DirectorySeparatorChar;
            return Directory.EnumerateFileSystemEntries("/proc/self/fd")
                .Count(path =>
                {
                    try
                    {
                        var target = new FileInfo(path).LinkTarget;
                        return target != null && target.StartsWith(
                            prefix,
                            StringComparison.Ordinal);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        return false;
                    }
                });
        }
    }

    [Test]
    public void PreCanceledAcquisitionPerformsNoPathIo()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "canceled.json");
        var metadataDirectory = Path.GetDirectoryName(
            LinuxPathIdentity.PublicationLockName(path))!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [path],
                TimeSpan.FromSeconds(1),
                cancellation.Token);
        }));

        Assert.That(Directory.Exists(metadataDirectory), Is.False);
    }

    [Test]
    public void MidAcquisitionCancellationReleasesEarlierLocks()
    {
        using var directory = TemporaryDirectory.Create();
        var ordered = new[]
        {
            Path.Combine(directory.Path, "first-canceled.json"),
            Path.Combine(directory.Path, "second-canceled.json")
        }.OrderBy(LinuxPathIdentity.PublicationLockName, StringComparer.Ordinal)
            .ToArray();
        using var blocked = LinuxPathIdentity.AcquirePublicationSet(
            [ordered[1]],
            TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));

        Assert.Throws<OperationCanceledException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                ordered,
                TimeSpan.FromSeconds(2),
                cancellation.Token);
        }));
        using var reacquired = LinuxPathIdentity.AcquirePublicationSet(
            [ordered[0]],
            TimeSpan.FromSeconds(1));
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
    public void NewlineDelimitedSetCollisionIsRejectedAsPartialOverlap()
    {
        using var directory = TemporaryDirectory.Create();
        var prefix = directory.Path + Path.DirectorySeparatorChar;
        var shared = Path.Combine(directory.Path, "z-shared.json");
        var first = new[]
        {
            prefix + "a",
            prefix + "b\n" + prefix + "c",
            shared
        };
        var second = new[]
        {
            prefix + "a\n" + prefix + "b",
            prefix + "c",
            shared
        };
        Assert.That(
            string.Join("\n", first.Order(StringComparer.Ordinal)),
            Is.EqualTo(string.Join(
                "\n",
                second.Order(StringComparer.Ordinal))));

        using (LinuxPathIdentity.AcquirePublicationSet(
                   first,
                   TimeSpan.FromSeconds(1)))
        {
        }
        var sharedMarker = LinuxPathIdentity.PublicationMarkerPath(shared);
        var markerIdentity = File.ReadAllBytes(sharedMarker);
        var error = Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                second,
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("partially overlap"));
            Assert.That(
                File.ReadAllBytes(sharedMarker),
                Is.EqualTo(markerIdentity));
            Assert.That(
                second.Take(2).Select(
                    LinuxPathIdentity.PublicationMarkerPath),
                Has.None.Matches<string>(File.Exists));
        }
    }

    [Test]
    public void PublicationSetIdentityUsesCanonicalInjectiveUtf8Framing()
    {
        var collisionLeft = new[] { "/a", "/b\n/c" };
        var collisionRight = new[] { "/a\n/b", "/c" };
        Assert.That(
            string.Join("\n", collisionLeft),
            Is.EqualTo(string.Join("\n", collisionRight)));

        var empty = LinuxPathIdentity.PublicationSetId([]);
        var single = LinuxPathIdentity.PublicationSetId(["/a"]);
        var multiple = LinuxPathIdentity.PublicationSetId(["/a", "/b"]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty, Has.Length.EqualTo(64));
            Assert.That(single, Is.Not.EqualTo(empty));
            Assert.That(multiple, Is.Not.EqualTo(single));
            Assert.That(
                LinuxPathIdentity.PublicationSetId(collisionLeft),
                Is.Not.EqualTo(
                    LinuxPathIdentity.PublicationSetId(collisionRight)));
            Assert.That(
                LinuxPathIdentity.PublicationSetId(["/z", "/a"]),
                Is.EqualTo(
                    LinuxPathIdentity.PublicationSetId(["/a", "/z"])));
            Assert.That(
                LinuxPathIdentity.PublicationSetId(
                    ["/雪\u2028x", "/carriage\rreturn"]),
                Is.Not.EqualTo(
                    LinuxPathIdentity.PublicationSetId(
                        ["/雪", "/x\n/carriage", "/return"])));
        }
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
        var directoryError = Assert.Throws<ArgumentException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [Path.Combine(
                    directory.Path,
                    ".sharpproof-publication",
                    "foreign.json")],
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(aliasError!.Message, Does.Contain("publication metadata"));
            Assert.That(reservedError!.Message, Does.Contain("publication metadata"));
            Assert.That(directoryError!.Message, Does.Contain("publication metadata"));
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
    public void ResetRemovesTornMarkerWhenDestinationIsAbsent()
    {
        using var directory = TemporaryDirectory.Create();
        var result = Path.Combine(directory.Path, "torn-result.json");
        var marker = LinuxPathIdentity.PublicationMarkerPath(result);
        var metadataDirectory = Path.GetDirectoryName(marker)!;
        Directory.CreateDirectory(metadataDirectory);
        File.SetUnixFileMode(
            metadataDirectory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        File.WriteAllText(
            marker,
            "SharpProof.PublicationSet/1\n");

        LinuxPathIdentity.ResetPublicationSet(
            [result],
            TimeSpan.FromSeconds(1));

        using var publication = LinuxPathIdentity.AcquirePublicationSet(
            [result],
            TimeSpan.FromSeconds(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(result), Is.False);
            Assert.That(File.Exists(marker), Is.True);
        }
    }

    [Test]
    [NonParallelizable]
    public void CaseFoldProbeChecksEveryExistingAncestorAndFailsClosed()
    {
        using var directory = TemporaryDirectory.Create();
        var nested = Directory.CreateDirectory(
            Path.Combine(directory.Path, "middle")).FullName;
        var target = Path.Combine(nested, "result.json");
        var visited = new List<string>();
        try
        {
            LinuxPathIdentity.CaseFoldedParentProbeOverrideForTest = path =>
            {
                visited.Add(path);
                return string.Equals(
                    path,
                    directory.Path,
                    StringComparison.Ordinal)
                    ? true
                    : false;
            };

            Assert.Throws<ArgumentException>((Action)(() =>
                LinuxPathIdentity.RequireLocalPath(target)));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(visited, Does.Contain(nested));
                Assert.That(visited, Does.Contain(directory.Path));
            }

            LinuxPathIdentity.CaseFoldedParentProbeOverrideForTest =
                static _ => null;
            Assert.Throws<IOException>((Action)(() =>
                LinuxPathIdentity.RequireLocalPath(target)));
        }
        finally
        {
            LinuxPathIdentity.CaseFoldedParentProbeOverrideForTest = null;
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
    public void PublicationMetadataSupportsNameMaxBoundaryForEveryMember()
    {
        using var directory = TemporaryDirectory.Create();
        var paths = Enumerable.Range(0, 4)
            .Select(index => Path.Combine(
                directory.Path,
                new string((char)('a' + index), 250) + ".json"))
            .ToArray();
        foreach (var path in paths)
        {
            File.WriteAllText(path, "boundary");
            File.Delete(path);
        }

        using var publication = LinuxPathIdentity.AcquirePublicationSet(
            paths,
            TimeSpan.FromSeconds(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paths.Select(LinuxPathIdentity.PublicationLockName),
                Has.All.Matches<string>(path => File.Exists(path)));
            Assert.That(paths.Select(LinuxPathIdentity.PublicationMarkerPath),
                Has.All.Matches<string>(path => File.Exists(path)));
        }
    }

    [Test]
    public void PublicationMetadataUsesUtf8ByteBoundedHashNames()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        using var directory = TemporaryDirectory.Create();
        var first = Path.Combine(
            directory.Path,
            new string('\u96ea', 80) + "-first.json");
        var second = Path.Combine(
            directory.Path,
            new string('\u96ea', 80) + "-second.json");
        var sibling = Directory.CreateDirectory(
            Path.Combine(directory.Path, "sibling")).FullName;
        var sameBasename = Path.Combine(sibling, Path.GetFileName(first));
        foreach (var path in new[] { first, second })
        {
            File.WriteAllText(path, "multibyte boundary");
            File.Delete(path);
        }

        var firstLock = LinuxPathIdentity.PublicationLockName(first);
        var repeatedLock = LinuxPathIdentity.PublicationLockName(first);
        var secondLock = LinuxPathIdentity.PublicationLockName(second);
        var sameBasenameLock = LinuxPathIdentity.PublicationLockName(
            sameBasename);
        using var publication = LinuxPathIdentity.AcquirePublicationSet(
            [first, second],
            TimeSpan.FromSeconds(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstLock, Is.EqualTo(repeatedLock));
            Assert.That(firstLock, Is.Not.EqualTo(secondLock));
            Assert.That(firstLock, Is.Not.EqualTo(sameBasenameLock));
            Assert.That(
                Encoding.UTF8.GetByteCount(Path.GetFileName(firstLock)),
                Is.LessThanOrEqualTo(255));
            Assert.That(
                Path.GetDirectoryName(firstLock),
                Is.EqualTo(Path.GetDirectoryName(secondLock)));
            Assert.That(File.Exists(firstLock), Is.True);
            Assert.That(
                File.GetUnixFileMode(Path.GetDirectoryName(firstLock)!) &
                (UnixFileMode.GroupRead |
                 UnixFileMode.GroupWrite |
                 UnixFileMode.GroupExecute |
                 UnixFileMode.OtherRead |
                 UnixFileMode.OtherWrite |
                 UnixFileMode.OtherExecute),
                Is.EqualTo((UnixFileMode)0));
        }
    }

    [Test]
    public void UnownedPublicationMetadataDirectoryFailsBeforeMutation()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        using var directory = TemporaryDirectory.Create();
        var result = Path.Combine(directory.Path, "result.json");
        var metadataDirectory = Path.GetDirectoryName(
            LinuxPathIdentity.PublicationLockName(result))!;
        Directory.CreateDirectory(metadataDirectory);
        File.SetUnixFileMode(
            metadataDirectory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead);

        var error = Assert.Throws<IOException>((Action)(() =>
        {
            using var publication = LinuxPathIdentity.AcquirePublicationSet(
                [result],
                TimeSpan.FromSeconds(1));
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("owned private directory"));
            Assert.That(File.Exists(result), Is.False);
            Assert.That(
                Directory.EnumerateFileSystemEntries(metadataDirectory),
                Is.Empty);
        }
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

        var error = Assert.Throws<IOException>((Action)(() =>
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
