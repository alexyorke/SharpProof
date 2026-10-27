using System.Collections.Immutable;
using System.Text.Json;
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
        CopyDirectory(Path.Combine(root, "tests/SharpProof.Gates", "Corpus"),
            Path.Combine(repository.FullName, "tests/SharpProof.Gates", "Corpus"));
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
        var result = await RunAsync(["ordinary-unknown-command"]);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.StartWith("Usage: SharpProof.Gates [corpus|corpus-update|"));
    }

    [Test]
    public async Task UnsupportedOracleArgumentsReturnAnActionableError()
    {
        var result = await RunAsync(["exception-oracle", "--unknown", "1"]);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("Use exception-oracle, allocation-oracle, purity-oracle, capability-oracle or"));
    }

    [Test]
    public async Task NonNumericOracleLimitReturnsAConversionError()
    {
        var result = await RunAsync(["exception-oracle", "--limit", "ordinary-text"]);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.StartWith("System.FormatException:"));
        Assert.That(result.Error, Does.Contain("ordinary-text"));
    }

    [TestCase("exception-oracle", "DoesNotThrow")]
    [TestCase("allocation-oracle", "ZeroAllocations")]
    [TestCase("purity-oracle", "EnforcePure")]
    [TestCase("capability-oracle", "AllowedCapabilities")]
    [TestCase("effectcontract-oracle", "EffectContract")]
    public async Task LimitedOrdinaryOracleCommandReportsItsActualSelectedUniverse(string command, string contract)
    {
        System.ArgumentNullException.ThrowIfNull(command);
        System.ArgumentNullException.ThrowIfNull(contract);
        var result = await RunAsync([command, "--limit", "1"]);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Is.Empty);
        using var document = JsonDocument.Parse(result.Output);
        var report = document.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.GetProperty("ContractKind").GetString(), Is.EqualTo(contract));
            Assert.That(report.GetProperty("UniverseMethodCount").GetInt32(), Is.EqualTo(200));
            Assert.That(report.GetProperty("CheckedMethodCount").GetInt32(), Is.EqualTo(1));
            Assert.That(report.GetProperty("Exhaustive").GetBoolean(), Is.False);
            Assert.That(report.GetProperty("Passed").GetBoolean(), Is.True);
            Assert.That(report.GetProperty("Rows").GetArrayLength(), Is.EqualTo(1));
            Assert.That(report.GetProperty("Rows")[0].GetProperty("MethodId").GetString(), Is.EqualTo("OSS0001"));
        }
    }

    [Test]
    public async Task DefaultCorpusCommandVerifiesTheReviewedCorpus()
    {
        var result = await RunAsync([]);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Does.StartWith("Corpus gate wall time: "));
        using var document = JsonDocument.Parse(result.Output);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.RootElement.GetProperty("Passed").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("CaseCount").GetInt32(), Is.EqualTo(462));
        }
    }

    [Test]
    public async Task CorpusUpdateCommandReproducesTheReviewedSnapshot()
    {
        var snapshot = Path.Combine(repository!.FullName, "tests/SharpProof.Gates", "Corpus", "expected.canonical.snapshot");
        var before = (await File.ReadAllBytesAsync(snapshot)).ToImmutableArray();

        var result = await RunAsync(["corpus-update"]);

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(result.Error, Is.Empty);
        Assert.That(result.Output, Is.EqualTo("Updated the canonical corpus snapshot." + Environment.NewLine));
        Assert.That(await File.ReadAllBytesAsync(snapshot), Is.EqualTo(before));
    }

    private Task<GateProcessResult> RunAsync(ImmutableArray<string> arguments)
    {
        if (arguments.IsDefault)
        { throw new System.ArgumentNullException(nameof(arguments)); }
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
        return GateProcess.RunCapturedAsync(start, TestContext.CurrentContext.CancellationToken);
    }

    private static void CopyDirectory(string source, string destination)
    {
        System.ArgumentNullException.ThrowIfNull(destination);
        System.ArgumentNullException.ThrowIfNull(source);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
