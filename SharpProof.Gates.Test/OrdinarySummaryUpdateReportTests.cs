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
public sealed class OrdinarySummaryUpdateReportTests
{
    [TestCase("value.Value += 1;")]
    [TestCase("value.Value++;")]
    public async Task ActualModeledInstanceUpdateKeepsItsRuntimeGapVisible(string update)
    {
        var source = "public class SummaryInvocation { public int Value; public static void Target([global::SharpProof.Attributes.NotNull] SummaryInvocation value) { " + update + " } }";
        var compilation = await PrepareAsync(source);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.WriteWitness, Is.Not.Null);
        var parameter = preparations.Single().Total!.Parameters.Single();
        var input = evidence.EntryModel[parameter.Entry];
        Assert.That(input.Kind, Is.EqualTo(SharpProof.Ir.IrValueKind.Reference));
        Assert.That(input.Reference, Is.InstanceOf<SharpProof.Ir.IrObjectState>());
        var fields = typeof(SharpProof.Ir.IrObjectState).GetProperty("Fields",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(fields, Is.Not.Null);
        Assert.That(fields!.GetValue(input.Reference), Is.Not.Empty);
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.AllocationWitness, Is.Null);
        Assert.That(evidence.LockWitness, Is.Null);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "summary-write-invocation", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("UnsupportedWriteTarget"));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.Zero);
        Assert.That(report.RuntimeWitnesses, Is.Zero);
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }

    private static async Task<CSharpCompilation> PrepareAsync(string source)
    {
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "summary-write-invocation.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), TestContext.CurrentContext.CancellationToken, summary: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }
}
