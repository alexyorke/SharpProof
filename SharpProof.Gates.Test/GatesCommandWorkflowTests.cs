using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class GatesCommandWorkflowTests : IDisposable
{
    private TempDirectory? repository;
    private string? runtimeDirectory;

    [OneTimeSetUp]
    public void CopyOrdinaryBuiltExecutableIntoAnIsolatedRepository()
    {
        repository = new TempDirectory("SharpProof-gates-command-");
        runtimeDirectory = Path.Combine(repository.FullName, "runtime");
        var builtDirectory = Path.GetDirectoryName(typeof(CorpusGate).Assembly.Location)!;
        CopyDirectory(builtDirectory, runtimeDirectory);
        var root = RepositoryLayout.FindRoot();
        CopyDirectory(Path.Combine(root, "SharpProof.Gates", "Corpus"),
            Path.Combine(repository.FullName, "SharpProof.Gates", "Corpus"));
        File.Copy(Path.Combine(root, "SharpProof.slnx"), Path.Combine(repository.FullName, "SharpProof.slnx"));
        var acceptanceDirectory = Path.Combine(repository.FullName, "eng", "acceptance");
        Directory.CreateDirectory(acceptanceDirectory);
        File.Copy(Path.Combine(root, "eng", "acceptance", "contract.json"),
            Path.Combine(acceptanceDirectory, "contract.json"));
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        repository?.Dispose();
        repository = null;
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task UnknownCommandReturnsTheDocumentedUsageStatus()
    {
        var result = await RunAsync("ordinary-unknown-command");
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.StartWith("Usage: SharpProof.Gates [corpus|corpus-update|"));
    }

    [Test]
    public async Task UnsupportedOracleArgumentsReturnAnActionableError()
    {
        var result = await RunAsync("exception-oracle", "--unknown", "1");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("Use exception-oracle, allocation-oracle, purity-oracle, capability-oracle or"));
    }

    [Test]
    public async Task NonNumericOracleLimitReturnsAConversionError()
    {
        var result = await RunAsync("exception-oracle", "--limit", "ordinary-text");
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.StartWith("System.FormatException:"));
        Assert.That(result.Error, Does.Contain("ordinary-text"));
    }

    private Task<GateProcessResult> RunAsync(params string[] arguments)
    {
        var start = GateProcess.CreateCaptured("dotnet", repository!.FullName);
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(Path.Combine(runtimeDirectory!, "SharpProof.Gates.Test.runtimeconfig.json"));
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(Path.Combine(runtimeDirectory!, "SharpProof.Gates.Test.deps.json"));
        start.ArgumentList.Add(Path.Combine(runtimeDirectory!, "SharpProof.Gates.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment.Remove(OpenSourceCorpusImporter.SourceEnvironmentVariable);
        return GateProcess.RunCapturedAsync(start, CancellationToken.None);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
