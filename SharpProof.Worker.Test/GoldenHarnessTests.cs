using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class GoldenHarnessTests
{
    [TestCase("")]
    [TestCase("true")]
    public void NormalComparisonNeverWritesOriginalFiles(string updateFlag)
    {
        using var copy = CreateRepository();
        using var original = CreateRepository();
        var fixture = GoldenTest.Load("worker", "sample", copy.FullName);
        using var environment = new GoldenEnvironment(updateFlag, original.FullName);
        Assert.Throws<AssertionException>((Action)(() => GoldenTest.Compare(fixture, "changed")));
        Assert.That(File.ReadAllText(Expected(original)), Is.EqualTo("old\n"));
        Assert.That(File.ReadAllText(Expected(copy)), Is.EqualTo("old\n"));
    }

    [Test]
    public void ExplicitUpdatePersistsOnlyPairedExpectationInOriginalSource()
    {
        using var copy = CreateRepository();
        using var original = CreateRepository();
        var fixture = GoldenTest.Load("worker", "sample", copy.FullName);
        using var environment = new GoldenEnvironment("1", original.FullName);
        GoldenTest.Compare(fixture, "changed\r\n");
        Assert.That(File.ReadAllBytes(Expected(original)), Is.EqualTo("changed\n"u8.ToArray()));
        Assert.That(File.ReadAllText(Expected(copy)), Is.EqualTo("old\n"));
        Assert.That(File.ReadAllText(Source(original)), Is.EqualTo("class Subject { }\n"));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(Expected(original))!), Has.Length.EqualTo(2));
    }

    [TestCase("source")]
    [TestCase("expected")]
    public void OriginalFilesMustMatchTheTestedCopyBeforeUpdating(string changed)
    {
        using var copy = CreateRepository();
        using var original = CreateRepository();
        var fixture = GoldenTest.Load("worker", "sample", copy.FullName);
        File.WriteAllText(changed == "source" ? Source(original) : Expected(original), "different\n");
        using var environment = new GoldenEnvironment("1", original.FullName);
        var exception = Assert.Throws<InvalidDataException>((Action)(() => GoldenTest.Compare(fixture, "changed")));
        Assert.That(exception!.Message, Does.Contain("differs from the tested copy"));
    }

    [Test]
    public void UpdateWithoutEntrypointSourceRootFailsClearly()
    {
        using var copy = CreateRepository();
        var fixture = GoldenTest.Load("worker", "sample", copy.FullName);
        using var environment = new GoldenEnvironment("1", null);
        var exception = Assert.Throws<InvalidOperationException>((Action)(() => GoldenTest.Compare(fixture, "changed")));
        Assert.That(exception!.Message, Does.Contain("canonical test entrypoint"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SymlinkedExpectedFileCannotRedirectAnUpdate(bool dangling)
    {
        using var copy = CreateRepository();
        using var original = CreateRepository();
        var fixture = GoldenTest.Load("worker", "sample", copy.FullName);
        var redirected = Path.Combine(original.FullName, "unrelated.txt");
        if (!dangling)
        {
            File.WriteAllText(redirected, "old\n");
        }
        File.Delete(Expected(original));
        File.CreateSymbolicLink(Expected(original), redirected);
        using var environment = new GoldenEnvironment("1", original.FullName);
        Assert.Throws<InvalidDataException>((Action)(() => GoldenTest.Compare(fixture, "changed")));
        Assert.That(File.Exists(redirected), Is.EqualTo(!dangling));
        if (!dangling)
        {
            Assert.That(File.ReadAllText(redirected), Is.EqualTo("old\n"));
        }
    }

    [Test]
    public void OrphanExpectationIsRejectedDuringDiscovery()
    {
        using var copy = CreateRepository();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Expected(copy))!, "orphan.expected"), "old\n");
        Assert.Throws<InvalidDataException>((Action)(() => GoldenTest.Cases("worker", copy.FullName).ToArray()));
    }

    [Test]
    public void MissingExpectationRequiresExplicitUpdateAndPersistsAtOriginalRoot()
    {
        using var copy = CreateRepository();
        using var original = CreateRepository();
        File.Delete(Expected(copy));
        File.Delete(Expected(original));
        using (var normal = new GoldenEnvironment("", original.FullName))
        {
            Assert.Throws<InvalidDataException>((Action)(() => GoldenTest.Load("worker", "sample", copy.FullName)));
        }
        using var update = new GoldenEnvironment("1", original.FullName);
        GoldenTest.Compare(GoldenTest.Load("worker", "sample", copy.FullName), "new");
        Assert.That(File.ReadAllText(Expected(original)), Is.EqualTo("new\n"));
        Assert.That(File.Exists(Expected(copy)), Is.False);
    }

    private static TempDirectory CreateRepository()
    {
        var directory = new TempDirectory("sharpproof-golden-helper-");
        File.WriteAllText(Path.Combine(directory.FullName, "SharpProof.slnx"), "");
        File.WriteAllText(Path.Combine(directory.FullName, "SharpProof.Release.props"), "");
        Directory.CreateDirectory(Path.GetDirectoryName(Source(directory))!);
        File.WriteAllText(Source(directory), "class Subject { }\n");
        File.WriteAllText(Expected(directory), "old\n");
        return directory;
    }

    private static string Source(TempDirectory directory)
    {
        return Path.Combine(directory.FullName, "tests", "golden", "worker", "sample.cs");
    }

    private static string Expected(TempDirectory directory)
    {
        return Path.ChangeExtension(Source(directory), ".expected");
    }

    private sealed class GoldenEnvironment : IDisposable
    {
        private readonly string? _update = Environment.GetEnvironmentVariable("SHARPPROOF_UPDATE_GOLDEN");
        private readonly string? _root = Environment.GetEnvironmentVariable("SHARPPROOF_GOLDEN_SOURCE_ROOT");
        internal GoldenEnvironment(string update, string? root)
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_UPDATE_GOLDEN", update);
            Environment.SetEnvironmentVariable("SHARPPROOF_GOLDEN_SOURCE_ROOT", root);
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_UPDATE_GOLDEN", _update);
            Environment.SetEnvironmentVariable("SHARPPROOF_GOLDEN_SOURCE_ROOT", _root);
        }
    }
}
