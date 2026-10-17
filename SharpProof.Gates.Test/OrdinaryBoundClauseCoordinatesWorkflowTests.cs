using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Gates.Corpus;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryBoundClauseCoordinatesWorkflowTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void CompilerBoundSourceAndAttributeClausesKeepTheirActualCoordinates(bool attribute, bool unnamedTree)
    {
        var source = attribute ? """
            public static class Helper {
                internal static int Target([SharpProof.Attributes.Positive] int value) => value;
            }
            public static class Caller {
                public static int Root(int value) => Helper.Target(value);
            }
            """ : """
            using SharpProof.Attributes;
            public static class Helper {
                internal static int Target(int value) { Contract.Requires(value > 0); return value; }
            }
            public static class Caller {
                public static int Root(int value) => Helper.Target(value);
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, AnalyzerGateHost.ParseOptions,
            unnamedTree ? string.Empty : "OrdinaryClauses.cs");
        var compilation = AnalyzerGateHost.CreateCompilation(string.Empty,
                "OrdinaryBoundClauses", includeExternalEffectsFixture: false)
            .RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var method = compilation.GetTypeByMetadataName("Helper")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binding = new ContractBinder(compilation, factory).BindTotalRequires(new TotalLoweringContext(factory, method));
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        var clause = binding.Clauses.Single();
        var syntax = clause.SourceSyntax!;
        var span = factory.GetOperationInfo(clause.SourceOperation).SourceSpan!;
        Assert.That(syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(span.Document, Is.EqualTo(unnamedTree ? "source" : tree.FilePath));
        Assert.That(span.Start, Is.EqualTo(syntax.Span.Start));
        Assert.That(span.Length, Is.EqualTo(syntax.Span.Length).And.GreaterThan(0));

        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken);
        Assert.That(batch.Gaps.All(static gap => gap.Reason == "UnsupportedOwnContracts"), Is.True);
        var body = batch.Callers.Single().Body;
        var coverage = CorpusGate.MatchNativeContractCalls(compilation, [body], TestContext.CurrentContext.CancellationToken);
        Assert.That(coverage.Expected, Is.EqualTo(1));
        Assert.That(coverage.Matched, Is.EqualTo(1));
        Assert.That(coverage.Failures, Is.Empty);
    }
}
