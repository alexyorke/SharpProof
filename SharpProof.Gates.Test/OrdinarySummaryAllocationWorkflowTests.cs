using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinarySummaryAllocationWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualAllocationSummaryReportsItsNativeWitnessWithoutRequiringAnException(bool allocate)
    {
        var source = allocate
            ? "public static class OrdinarySummary { public static object Target(int value) => new object(); }"
            : "public static class OrdinarySummary { public static int Target(int value) => value; }";
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-summary.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), TestContext.CurrentContext.CancellationToken, summary: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        if (allocate)
        {
            Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
            Assert.That(evidence.AllocationWitness, Is.Not.Null);
            Assert.That(evidence.ExceptionWitness, Is.Null);
            var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
                "ordinary-allocation-summary", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(report.Rows.Single().NativeReason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(report.Rows.Single().ExceptionKind, Is.Null);
            Assert.That(report.NativeRefuted, Is.EqualTo(1));
        }
        else
        {
            Assert.That(evidence.Outcome, Is.InstanceOf<ProvenOutcome>());
            var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
                "ordinary-allocation-summary", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("NotRun"));
            Assert.That(report.Passed, Is.True);
        }
    }

    [Test]
    public async Task ActualAllocationSummaryWitnessReceivesIndependentRuntimeChecks()
    {
        const string source = "public static class OrdinarySummary { public static object Target(int value) => new object(); }";
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-summary.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), TestContext.CurrentContext.CancellationToken, summary: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.AllocationWitness, Is.Not.Null);
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.HasFeasibleEntryWitness, Is.True);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "ordinary-allocation-summary", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.GreaterThan(0));
        Assert.That(report.Rows.Single().AllocatedBytes, Is.GreaterThan(0));
        Assert.That(report.RuntimeWitnesses, Is.EqualTo(1));
        Assert.That(report.Passed, Is.True);
    }
}
