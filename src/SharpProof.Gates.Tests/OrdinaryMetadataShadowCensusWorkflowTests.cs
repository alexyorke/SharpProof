using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryMetadataShadowCensusWorkflowTests
{
    [Test]
    public void ActualOwnedMetadataClauseKeepsItsSourceCensusGap()
    {
        using var temporary = new TempDirectory("SharpProof-metadata-shadow-census-");
        const string librarySource = """
            public static class MetadataHelper {
                public static int Target([SharpProof.Attributes.Positive] int value) => value;
            }
            """;
        var libraryTree = CSharpSyntaxTree.ParseText(librarySource,
            AnalyzerGateHost.ParseOptions.WithPreprocessorSymbols(Contract.ConditionalSymbol), "MetadataHelper.cs");
        var library = AnalyzerGateHost.CreateCompilation(string.Empty, "OrdinaryMetadataShadowLibrary", includeExternalEffectsFixture: false)
            .RemoveAllSyntaxTrees().AddSyntaxTrees(libraryTree);
        var libraryPath = Path.Combine(temporary.FullName, "OrdinaryMetadataShadowLibrary.dll");
        using (var image = File.Create(libraryPath))
        {
            var emitted = library.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        var compilation = AnalyzerGateHost.CreateCompilation("""
            public static class Caller {
                public static int Root(int value) => MetadataHelper.Target(value);
            }
            """, "OrdinaryMetadataShadowCaller", includeExternalEffectsFixture: false)
            .AddReferences(MetadataReference.CreateFromFile(libraryPath));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var method = compilation.GetTypeByMetadataName("MetadataHelper")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(method.Parameters.Single().GetAttributes().Single().AttributeClass!.ToDisplayString(),
            Is.EqualTo("SharpProof.Attributes.PositiveAttribute"));
        var references = CompilerCompilationCapture.CaptureReferences(compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken).ToImmutableArray(), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
        Assert.That(batch.Gaps, Is.Empty);
        var original = batch.Callers.Single().Body;
        var body = CompilerDecodedShadowBody.Decode(original.CallableId,
            CompilerTotalCallableArtifactCodec.Encode(original)!, TestContext.CurrentContext.CancellationToken, references).Body;
        var clause = body.CallPreconditions.Single();
        Assert.That(clause.MetadataClause, Is.Not.Null);
        Assert.That(body.Program.Factory.GetOperationInfo(clause.ClauseSite).SourceSpan, Is.Null);
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        var owner = inventory.Owners.Single();

        var coverage = CorpusGate.MatchNativeContractCalls(compilation, [body], TestContext.CurrentContext.CancellationToken);

        Assert.That(coverage.Expected, Is.Zero);
        Assert.That(coverage.Matched, Is.Zero);
        Assert.That(coverage.Failures, Is.EqualTo(ImmutableArray.Create(
            "UnsupportedClauseOwner:" + owner.CallableId,
            "NativeSourceSpanMissing:" + body.CallableId)));
    }
}
