using System.Collections.Immutable;
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
public sealed class OrdinarySummaryThrowAfterWriteReportTests
{
    [Test]
    public async Task ActualAllowedExceptionAfterWriteKeepsTheInterruptedReplayVisible()
    {
        const string source = "public class SummaryInvocation { public static int Counter; [global::SharpProof.Attributes.EffectContract(global::SharpProof.Attributes.SharpProofEffect.Throws | global::SharpProof.Attributes.SharpProofEffect.Allocates, ThrownExceptions = new[] { typeof(System.InvalidOperationException) })] public static void Target() { Counter = 1; throw new System.InvalidOperationException(); } }";
        var compilation = await PrepareAsync(source);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var constraint = preparations.Single().EffectClaims.Single().Constraint;
        Assert.That(constraint.AllowedEffects, Is.EqualTo(WorkerEffectSet.Throws | WorkerEffectSet.Allocates));
        Assert.That(constraint.AllowedExceptionTypes, Is.EqualTo(ImmutableArray.Create(
            SharpProof.Frontend.CompilerIdentityBridge.CreateTypeDisplay(compilation.GetTypeByMetadataName("System.InvalidOperationException")))));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.WriteWitness, Is.Not.Null);
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.AllocationWitness, Is.Null);
        Assert.That(evidence.LockWitness, Is.Null);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "summary-write-invocation", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("WriteInvocationThrew"));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.EqualTo(1));
        Assert.That(report.RuntimeWitnesses, Is.Zero);
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }

    private static async Task<CSharpCompilation> PrepareAsync(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "summary-write-invocation.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), TestContext.CurrentContext.CancellationToken, summary: true);
        var probeTree = compilation.SyntaxTrees.Single(tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (tree.FilePath == file.Path));
        var probeRoot = await probeTree.GetRootAsync();
        var instrumented = probeRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        Assert.That(instrumented.AttributeLists, Has.Count.EqualTo(2));
        Assert.That(instrumented.AttributeLists[0].Attributes.Single().ArgumentList!.Arguments.Single().Expression.ToString(),
            Is.EqualTo("global::SharpProof.Attributes.SharpProofEffect.None"));
        // Restore the original source contract while preserving the actual corpus annotation.
        var restored = instrumented.WithAttributeLists(instrumented.AttributeLists.RemoveAt(0));
        Assert.That(OpenSourceCorpusRunner.CorpusMethodId(restored), Is.EqualTo("sample"));
        compilation = compilation.ReplaceSyntaxTree(probeTree, probeTree.WithRootAndOptions(
            probeRoot.ReplaceNode(instrumented, restored), probeTree.Options));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return compilation;
    }
}
