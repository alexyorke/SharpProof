using System.Reflection;
using System.Runtime.Loader;
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
public sealed class OrdinarySummaryWriteWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualSummaryWriteReportsItsNativeWitnessWithoutRequiringAnException(bool write)
    {
        var compilation = await PrepareAsync(write);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        if (write)
        {
            Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
            Assert.That(evidence.WriteWitness, Is.Not.Null);
            Assert.That(evidence.ExceptionWitness, Is.Null);
            Assert.That(evidence.AllocationWitness, Is.Null);
        }
        else
        {
            Assert.That(evidence.Outcome, Is.InstanceOf<ProvenOutcome>());
        }

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "ordinary-write-summary", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(write ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven));
        Assert.That(report.Rows.Single().NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(report.Rows.Single().ExceptionKind, Is.Null);
        Assert.That(report.NativeRefuted, Is.EqualTo(write ? 1 : 0));
        Assert.That(report.Passed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SameActualSdkCompilationExposesItsIndependentStaticWrite(bool write)
    {
        var compilation = await PrepareAsync(write);
        using var image = NativeSourceEmission.EmitImage(compilation, "The summary write source did not compile: ", TestContext.CurrentContext.CancellationToken);
        var context = new AssemblyLoadContext("SharpProof.OrdinarySummaryWrite." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var declaring = assembly.GetType("OrdinarySummaryWrite", throwOnError: true)!;
            var field = declaring.GetField("Counter", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
            var method = declaring.GetMethod("Target", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
            Assert.That(field.GetValue(null), Is.EqualTo(0));

            method.Invoke(null, null);

            Assert.That(field.GetValue(null), Is.EqualTo(write ? 1 : 0));
        }
        finally
        {
            context.Unload();
        }
    }

    private static async Task<CSharpCompilation> PrepareAsync(bool write)
    {
        var source = "public static class OrdinarySummaryWrite { public static int Counter; public static void Target() { " +
            (write ? "Counter = 1;" : string.Empty) + " } }";
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-summary-write.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), TestContext.CurrentContext.CancellationToken, summary: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(compilation.GetTypeByMetadataName("OrdinarySummaryWrite")!.StaticConstructors, Is.Empty);
        return compilation;
    }
}
