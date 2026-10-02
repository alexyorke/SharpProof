using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeExceptionShadowTests
{
    [TestCase("int x", "return 10 / x;")]
    [TestCase("ulong x", "return (int)(10UL / x);")]
    [TestCase("bool x", "return 10 / (x ? 0 : 1);")]
    [TestCase("int[] x", "return x.Length;")]
    public async Task SourceProbeRoundTripAndCompiledRuntimeConfirmConcreteWitnesses(string parameter, string body)
    {
        var document = Document("public static class C { public static int Target(" + parameter + ") { " + body + " } }");
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None);
        var report = await NativeExceptionShadow.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(report.RuntimeWitnesses, Is.EqualTo(1));
        Assert.That(report.RuntimeContradictions, Is.Zero);
    }

    [Test]
    public async Task PinnedUniverseIsExhaustivelyComparedWithoutOracleContradictions()
    {
        var root = RepositoryLayout.FindRoot();
        var report = await NativeExceptionShadow.RunAsync(root);
        Assert.That(report.UniverseMethodCount, Is.EqualTo(200));
        Assert.That(report.CheckedMethodCount, Is.EqualTo(report.UniverseMethodCount));
        Assert.That(report.Exhaustive, Is.True);
        Assert.That(report.UniverseSha256, Is.EqualTo("BD29688EDA47BA7EEB68093E4151D6A786901C4E41D8CE1A32DDA8CF8FA6F7ED"));
        Assert.That(report.Rows.Select(row => row.MethodId), Is.EquivalentTo(OpenSourceCorpusCatalog.Load(root).Methods.Select(method => method.Id)));
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.DisagreementCount, Is.Zero);
        Assert.That(report.ComparisonPassed, Is.True);
        Assert.That(report.Rows.Single(row => row.MethodId == "OSS0199").NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        await TestContext.Progress.WriteLineAsync($"Exception shadow: {report.CheckedMethodCount} methods; {report.LegacyProven} legacy proofs; " +
            $"{report.RetainedProven} retained; {report.RuntimeWitnesses} runtime confirmations; {report.WallSeconds:F1}s.");
    }

    [Test]
    public async Task SimpleSourceProofIsRetainedAfterArtifactRoundTrip()
    {
        var document = Document("public static class C { public static int Target(int x) { return x; } }");
        var report = await NativeExceptionShadow.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None),
            ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.Rows.Single().LegacyOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(report.RetentionGatePassed, Is.True);
        Assert.That(report.RuntimeWitnesses, Is.Zero);
    }

    [Test]
    public async Task UnknownBodiesAreCountedInsteadOfExcludedFromCoverage()
    {
        var document = Document("public static class C { public static int Target(int x) { System.Console.WriteLine(x); return x; } }");
        var report = await NativeExceptionShadow.ObserveAsync(OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None),
            ["sample"], RepositoryLayout.FindRoot(), "test", 1);
        Assert.That(report.CheckedMethodCount, Is.EqualTo(1));
        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(report.NativeUnknownReasons.Values.Sum(), Is.EqualTo(1));
        Assert.That(report.RuntimeWitnesses, Is.Zero);
    }

    [Test]
    public void SamplingCannotPassTheFullRetentionGate()
    {
        var row = Row("one", WorkerClaimOutcome.Proven);
        var report = NativeExceptionShadow.Summarize("test", 2, [row], 0);
        Assert.That(report.Exhaustive, Is.False);
        Assert.That(report.RetainedPercent, Is.EqualTo(100));
        Assert.That(report.RetentionGatePassed, Is.False);
    }

    [Test]
    public void MissingNativeProofsRemainInTheRetentionDenominator()
    {
        var report = NativeExceptionShadow.Summarize("test", 2,
            [Row("one", WorkerClaimOutcome.Proven), Row("two", WorkerClaimOutcome.Unknown)], 0);
        Assert.That(report.LegacyProven, Is.EqualTo(2));
        Assert.That(report.RetainedProven, Is.EqualTo(1));
        Assert.That(report.RetainedPercent, Is.EqualTo(50));
        Assert.That(report.RetentionGatePassed, Is.False);
        Assert.That(report.NativeUnknownReasons[WorkerClaimReason.UnsupportedBody.ToString()], Is.EqualTo(1));
    }

    [Test]
    public void CompilerAdmissionCannotRemoveLegacyProofsFromTheDenominator()
    {
        var row = Row("one", WorkerClaimOutcome.Unknown) with
        { CompilerOutcome = WorkerClaimOutcome.Unknown, CompilerReason = WorkerClaimReason.UnsupportedContract };
        var report = NativeExceptionShadow.Summarize("test", 1, [row], 0);
        Assert.That(report.LegacyProven, Is.EqualTo(1));
        Assert.That(report.RetainedProven, Is.Zero);
        Assert.That(report.RetentionGatePassed, Is.False);
    }

    [Test]
    public void DuplicateCoverageAndOracleContradictionsCannotPass()
    {
        var row = Row("one", WorkerClaimOutcome.Proven);
        Assert.Throws<ArgumentException>(new Action(() => NativeExceptionShadow.Summarize("test", 2, [row, row], 0)));
        var report = NativeExceptionShadow.Summarize("test", 1,
            [row with { NativeOutcome = WorkerClaimOutcome.Refuted, RuntimeOracle = "Contradiction" }], 0);
        Assert.That(report.ComparisonPassed, Is.False);
        Assert.That(report.DisagreementCount, Is.EqualTo(1));
        Assert.That(report.RuntimeContradictions, Is.EqualTo(1));
    }

    private static NativeExceptionShadowRow Row(string id, WorkerClaimOutcome native)
    {
        return new(id, id, WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerClaimOutcome.Proven, WorkerClaimReason.None, native,
            native == WorkerClaimOutcome.Unknown ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None,
            native != WorkerClaimOutcome.Unknown, false, null, "NotRun");
    }

    private static OpenSourceCorpusDocument Document(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = method.GetLocation().GetLineSpan();
        return new(2, [], [new("test", "sample.cs", "test", source)],
            [new("sample", "test", "sample.cs", span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                "test", "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported)]);
    }
}
