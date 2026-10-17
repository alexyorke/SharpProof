using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OracleWorkflowInputTests
{
    [TestCase(-1)]
    [TestCase(201)]
    public void PinnedOracleRejectsLimitsOutsideItsMethodUniverse(int limit)
    {
        var error = Assert.ThrowsAsync<ArgumentOutOfRangeException>((Func<Task>)(async () =>
            await NativeEffectOracleGate.RunAsync(RepositoryLayout.FindRoot(), maximumMethods: limit, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.ParamName, Is.EqualTo("maximumMethods"));
    }

    [TestCase("empty", 1)]
    [TestCase("blank", 1)]
    [TestCase("duplicate", 2)]
    [TestCase("larger-than-universe", 0)]
    public void OracleRejectsInvalidSelectedMethodIdentities(string selection, int universeCount)
    {
        ImmutableArray<string> methods = selection switch
        {
            "empty" => [],
            "blank" => [" "],
            "duplicate" => ["sample", "sample"],
            _ => ["sample"]
        };
        var compilation = AnalyzerGateHost.CreateCompilation("public static class C { public static int Value() => 1; }");
        var error = Assert.ThrowsAsync<ArgumentException>((Func<Task>)(async () =>
            await NativeEffectOracleGate.ObserveAsync(compilation, methods,
                RepositoryLayout.FindRoot(), "fixture", universeCount, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.ParamName, Is.EqualTo("methodIds"));
        Assert.That(error.Message, Does.StartWith("An oracle universe must have unique, nonempty method identities."));
    }

    [Test]
    public void OracleRejectsSimultaneousContractModes()
    {
        var compilation = AnalyzerGateHost.CreateCompilation("public static class C { public static int Value() => 1; }");
        var error = Assert.ThrowsAsync<ArgumentException>((Func<Task>)(async () =>
            await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"],
                RepositoryLayout.FindRoot(), "fixture", 1, allocations: true, purity: true, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.ParamName, Is.EqualTo("purity"));
        Assert.That(error.Message, Does.StartWith("An oracle run must select exactly one effect contract."));
    }

    [Test]
    public void OracleRejectsASelectedMethodAbsentFromCompilation()
    {
        var compilation = AnalyzerGateHost.CreateCompilation("public static class C { public static int Value() => 1; }");
        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"],
                RepositoryLayout.FindRoot(), "fixture", 1, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message,
            Is.EqualTo("The declared exception probes do not equal the selected corpus universe."));
    }
}
