using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinarySummaryWriteReportTests
{
    [TestCase("", "Counter = 1;", "Confirmed", 1)]
    [TestCase("", "Counter = 0;", "NoObservedMutation", 0)]
    [TestCase("int value", "Counter = 1;", "Confirmed", 1)]
    [TestCase("", "Counter++;", "Confirmed", 1)]
    public async Task ActualSummaryWriteReportDistinguishesMutationFromAnUnchangedStore(
        string parameters, string body, string expectedRuntime, int expectedWitnesses)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(expectedRuntime);
        System.ArgumentNullException.ThrowIfNull(parameters);
        var compilation = await PrepareAsync("public static class SummaryWriteReport { public static int Counter; " +
            "public static void Target(" + parameters + ") { " + body + " } }");
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.WriteWitness, Is.Not.Null);
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.AllocationWitness, Is.Null);
        Assert.That(evidence.LockWitness, Is.Null);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "summary-write-report", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo(expectedRuntime));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.GreaterThan(0));
        Assert.That(report.RuntimeWitnesses, Is.EqualTo(expectedWitnesses));
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }

    private static async Task<CSharpCompilation> PrepareAsync(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "summary-write-report.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), TestContext.CurrentContext.CancellationToken, summary: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return compilation;
    }
}
