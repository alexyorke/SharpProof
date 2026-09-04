using System.Text;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1849",
    Justification = "These tests intentionally exercise AtomicFile's synchronous API.")]
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
        AssertWriteUtf8ReplacementAsync(static (path, content) =>
        {
            AtomicFile.WriteUtf8(path, content);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    [Test]
    public async Task WriteUtf8AsyncCreatesParentsWithoutPreambleAndReplacesDestination()
    {
        await AssertWriteUtf8ReplacementAsync(
            static (path, content) => AtomicFile.WriteUtf8Async(
                path,
                content));
    }

    [Test]
    public void WriteUtf8SupportsValidLongDestinationBasename()
    {
        AssertWriteUtf8LongDestinationAsync(static (path, content) =>
        {
            AtomicFile.WriteUtf8(path, content);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    [Test]
    public async Task WriteUtf8AsyncSupportsValidLongDestinationBasename()
    {
        await AssertWriteUtf8LongDestinationAsync(
            static (path, content) => AtomicFile.WriteUtf8Async(
                path,
                content));
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

    private static string[] TemporaryFiles(string path)
    {
        return Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp");
    }

    private async Task AssertWriteUtf8ReplacementAsync(
        Func<string, string, Task> write)
    {
        var path = Path.Combine(_root, "nested", "result.txt");
        await write(path, "first\n");
        await write(path, "second\n");

        AssertPublished(path, "second\n");
    }

    private async Task AssertWriteUtf8LongDestinationAsync(
        Func<string, string, Task> write)
    {
        var path = LongDestinationPath();
        await write(path, "content\n");

        AssertPublished(path, "content\n");
    }

    private static void AssertPublished(string path, string expected)
    {
        Assert.That(
            File.ReadAllBytes(path),
            Is.EqualTo(Encoding.UTF8.GetBytes(expected)));
        Assert.That(TemporaryFiles(path), Is.Empty);
    }

    private string LongDestinationPath()
    {
        return Path.Combine(_root, new string('s', 220) + ".sarif");
    }
}
