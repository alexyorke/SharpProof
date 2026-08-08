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
