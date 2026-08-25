using System.Globalization;
using System.Text;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class AtomicFileTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "SharpProof.AtomicFile." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void WriteUtf8CreatesParentsWithoutPreambleAndReplacesDestination()
    {
        var path = Path.Combine(_root, "nested", "result.txt");
        AtomicFile.WriteUtf8(path, "first\n");
        AtomicFile.WriteUtf8(path, "second\n");

        Assert.That(File.ReadAllBytes(path), Is.EqualTo(Encoding.UTF8.GetBytes("second\n")));
        Assert.That(TemporaryFiles(path), Is.Empty);
    }

    [Test]
    public async Task WriteUtf8AsyncCreatesParentsWithoutPreambleAndReplacesDestination()
    {
        var path = Path.Combine(_root, "nested", "result.txt");
        await AtomicFile.WriteUtf8Async(path, "first\n");
        await AtomicFile.WriteUtf8Async(path, "second\n");

        Assert.That(
            await File.ReadAllBytesAsync(path),
            Is.EqualTo(Encoding.UTF8.GetBytes("second\n")));
        Assert.That(TemporaryFiles(path), Is.Empty);
    }

    [Test]
    public void CanceledWritePreservesDestinationAndCleansTemporaryFile()
    {
        var path = Path.Combine(_root, "result.txt");
        File.WriteAllText(path, "original");

        Func<Task> action = async () => await AtomicFile.WriteUtf8Async(
            path, "replacement", new CancellationToken(canceled: true));
        Assert.That(action, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(File.ReadAllText(path), Is.EqualTo("original"));
        Assert.That(TemporaryFiles(path), Is.Empty);
    }

    [Test]
    public void FailedPublicationCleansTemporaryFile()
    {
        var path = Path.Combine(_root, "destination");
        Directory.CreateDirectory(path);

        Action action = () => AtomicFile.WriteUtf8(path, "content");
        Assert.That(action, Throws.Exception);
        Assert.That(TemporaryFiles(path), Is.Empty);
    }

    [Test]
    public void PublicationStagingUsesAStableShortTemporaryName()
    {
        var path = Path.Combine(
            _root,
            new string('s', 220) + ".sarif");
        var temporary = AtomicFile.PrepareStaged(path);
        try
        {
            Assert.That(Path.GetFileName(temporary).Length, Is.LessThan(255));
            AtomicFile.WriteStagedBytes(
                temporary,
                Encoding.UTF8.GetBytes("content"));
            AtomicFile.PublishStaged(temporary, path);
            Assert.That(File.ReadAllText(path), Is.EqualTo("content"));
        }
        finally
        {
            AtomicFile.TryDeleteStaged(temporary);
        }
    }

    [Test]
    public void ConcurrentPublishersDoNotFailWhenDestinationStartsMissing()
    {
        var path = Path.Combine(_root, "concurrent", "result.txt");
        const int publisherCount = 8;
        using var ready = new CountdownEvent(publisherCount);
        using var release = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, publisherCount)
            .Select(index => Task.Run(() =>
            {
                var temporary = AtomicFile.PrepareStaged(path);
                try
                {
                    AtomicFile.WriteStagedBytes(
                        temporary,
                        Encoding.UTF8.GetBytes(
                            index.ToString(CultureInfo.InvariantCulture)));
                    ready.Signal();
                    release.Wait();
                    AtomicFile.PublishStaged(temporary, path);
                }
                finally
                {
                    AtomicFile.TryDeleteStaged(temporary);
                }
            }))
            .ToArray();

        Assert.That(ready.Wait(TimeSpan.FromSeconds(5)), Is.True);
        release.Set();
        Action wait = () => Task.WaitAll(tasks);
        Assert.DoesNotThrow(wait);
        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void WriteUtf8SupportsAValidNearLimitDestinationName()
    {
        var path = Path.Combine(_root, new string('x', 240));

        AtomicFile.WriteUtf8(path, "content");

        Assert.That(File.ReadAllText(path), Is.EqualTo("content"));
        Assert.That(TemporaryFiles(path), Is.Empty);
    }

    [Test]
    public void SweepStagedRemovesOnlyExpiredSharpProofFiles()
    {
        var expired = Path.Combine(_root, ".sharpproof-expired.tmp");
        var fresh = Path.Combine(_root, ".sharpproof-fresh.tmp");
        File.WriteAllText(expired, "old");
        File.WriteAllText(fresh, "new");
        File.SetLastWriteTimeUtc(expired, DateTime.UtcNow - TimeSpan.FromHours(2));

        AtomicFile.SweepStaged(_root, TimeSpan.FromHours(1));

        Assert.That(File.Exists(expired), Is.False);
        Assert.That(File.Exists(fresh), Is.True);
    }

    private static string[] TemporaryFiles(string path)
    {
        return Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp");
    }
}
