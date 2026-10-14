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
public sealed class OrdinarySummaryCapturedSuffixReportTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualCapturedSuffixKeepsItsMismatchedRawTreeSpanGapVisible(bool padded)
    {
        const string source = "public class SummaryInvocation { public static int Counter; public static void Target() { Counter = 1; } }";
        var compilation = await PrepareAsync(source);
        var originals = compilation.SyntaxTrees.ToArray();
        Assert.That(originals, Has.Length.EqualTo(2));
        var targetTree = originals[1];
        Assert.That(targetTree.FilePath, Is.EqualTo("summary-write-invocation.cs"));
        var prefix = CSharpSyntaxTree.ParseText("public class PathPrefixHelper { }",
            (CSharpParseOptions)targetTree.Options, targetTree.FilePath);
        var suffixPath = targetTree.FilePath + "#2";
        var suffixSource = padded ? "/*" + new string('x', source.Length) + "*/ public class PathSuffixHelper { }" : "public class PathSuffixHelper { }";
        var suffix = CSharpSyntaxTree.ParseText(suffixSource,
            (CSharpParseOptions)targetTree.Options, suffixPath);
        compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(originals[0], prefix, targetTree, suffix);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparations.Single(), new WorkerBudgets(), CancellationToken.None);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.WriteWitness, Is.Not.Null);
        var span = preparations.Single().Total!.Program.Factory.GetOperationInfo(evidence.WriteWitness!.Value).SourceSpan;
        Assert.That(span, Is.Not.Null);
        Assert.That(span!.Document, Is.EqualTo(Path.GetFullPath(suffixPath)));
        var suffixRoot = await suffix.GetRootAsync();
        var sdkSpan = new Microsoft.CodeAnalysis.Text.TextSpan(span.Start, span.Length);
        Assert.That(suffixRoot.FullSpan.Contains(sdkSpan), Is.EqualTo(padded));
        if (padded)
        {
            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, suffix);
            Assert.That(model.GetOperation(suffixRoot.FindNode(sdkSpan, getInnermostNodeForTie: true)), Is.Null);
        }
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.AllocationWitness, Is.Null);
        Assert.That(evidence.LockWitness, Is.Null);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "summary-write-invocation", 1, summary: true);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo(padded ? "UnsupportedWriteTarget" : "UnsupportedWriteSourceSite"));
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
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), CancellationToken.None, summary: true);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }
}
