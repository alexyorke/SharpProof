using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Gates.Corpus;
using SharpProof.Ir;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinarySourceReferenceContractWorkflowTests
{
    [Test]
    public void ActualSourceCompilationReferenceClauseIsRejectedAsAForeignCensusTree()
    {
        const string librarySource = """
            public static class SourceHelper {
                public static int Target([SharpProof.Attributes.Positive] int value) => value;
            }
            """;
        var options = AnalyzerGateHost.ParseOptions.WithPreprocessorSymbols(Contract.ConditionalSymbol);
        var libraryTree = CSharpSyntaxTree.ParseText(librarySource, options, "SourceHelper.cs");
        var library = AnalyzerGateHost.CreateCompilation(string.Empty,
                "OrdinarySourceContractLibrary", includeExternalEffectsFixture: false)
            .RemoveAllSyntaxTrees().AddSyntaxTrees(libraryTree);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var sourceMethod = library.GetTypeByMetadataName("SourceHelper")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var compilation = AnalyzerGateHost.CreateCompilation("""
            public static class Caller {
                public static int Root(int value) => SourceHelper.Target(value);
            }
            """, "OrdinarySourceContractCaller", includeExternalEffectsFixture: false)
            .AddReferences(library.ToMetadataReference());
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var method = compilation.GetTypeByMetadataName("SourceHelper")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(method, sourceMethod), Is.True);
        Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(method.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(libraryTree));
        Assert.That(method.Parameters.Single().GetAttributes().Single().AttributeClass!.ToDisplayString(),
            Is.EqualTo("SharpProof.Attributes.PositiveAttribute"));
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        Assert.That(session.HasPotentialCallPreconditions(method), Is.True);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var binding = new ContractBinder(compilation, factory).BindTotalRequires(context);
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        var clause = binding.Clauses.Single();
        Assert.That(clause.Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(clause.SourceSyntax, Is.Not.Null);
        var syntax = clause.SourceSyntax!;
        Assert.That(syntax.SyntaxTree, Is.SameAs(libraryTree));
        Assert.That(compilation.SyntaxTrees.Contains(libraryTree), Is.False);
        var span = factory.GetOperationInfo(clause.SourceOperation).SourceSpan!;
        Assert.That(span.Document, Is.EqualTo(libraryTree.FilePath));
        Assert.That(span.Start, Is.EqualTo(syntax.Span.Start));
        Assert.That(span.Length, Is.EqualTo(syntax.Span.Length));
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        Assert.That(inventory.Gaps, Is.Empty);
        var owner = inventory.Owners.Single();
        Assert.That(owner.DiscoveryComplete, Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(owner.Calls.Single().Target, method), Is.True);

        var coverage = CorpusGate.MatchNativeContractCalls(compilation, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Expected, Is.Zero);
            Assert.That(coverage.Matched, Is.Zero);
            Assert.That(coverage.Failures, Has.Length.EqualTo(1));
            Assert.That(coverage.Failures.Single(), Is.EqualTo("ForeignClauseTree:" + owner.CallableId));
        }
    }
}
