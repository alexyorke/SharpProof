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
public sealed class OrdinarySummaryWriteInvocationReportTests
{
    [TestCase("public class SummaryInvocation { public static int Counter; public void Target() { Counter = 1; } }", "UnsupportedWriteInvocationShape", 0)]
    [TestCase("public class SummaryInvocation<T> { public static int Counter; public static void Target() { Counter = 1; } }", "UnsupportedWriteOwnerInitialization", 0)]
    [TestCase("public class SummaryInvocation { public static readonly int Seed = 1; public static int Counter; public static void Target() { Counter = 1; } }", "UnsupportedWriteOwnerInitialization", 0)]
    [TestCase("public class SummaryInvocation { public static int Counter; public static void Target([global::SharpProof.Attributes.NotNull] object value) { Counter = 1; } }", "UnsupportedWriteInput", 0)]
    [TestCase("public class SummaryInvocation { public static int Counter; public static void Target() { Counter = 1; while (true) { } } }", "NonTerminatingOrIncompleteWriteWitness", 0)]
    [TestCase("public class SummaryInvocation { public static int Counter; public static void Target(bool value) { Counter = 1; } }", "Confirmed", 1)]
    [TestCase("public class SummaryInvocation { public static int Counter; public static void Target(object value) { Counter = 1; } }", "Confirmed", 1)]
    public async Task ActualSourceWriteClassifiesSupportedAndUnperformedInvocations(string source, string runtime, int checks)
    {
        var compilation = await PrepareAsync(source);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), CancellationToken.None);
        await TestContext.Progress.WriteLineAsync($"Invocation native: outcome={evidence.Outcome?.GetType().Name ?? "null"}; reason={evidence.Reason}; total={preparations.Single().Total != null}; complete={preparations.Single().Total?.EffectsCompleteAtEntry}; abstraction={preparations.Single().Total?.IsBodyAbstraction}; inputKinds={string.Join(",", evidence.EntryModel.Values.Select(value => value.Kind))}");
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.WriteWitness, Is.Not.Null);
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.AllocationWitness, Is.Null);
        Assert.That(evidence.LockWitness, Is.Null);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "summary-write-invocation", 1, summary: true);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo(runtime));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.EqualTo(checks));
        Assert.That(report.RuntimeWitnesses, Is.EqualTo(runtime == "Confirmed" ? 1 : 0));
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
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), CancellationToken.None, summary: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }
}
