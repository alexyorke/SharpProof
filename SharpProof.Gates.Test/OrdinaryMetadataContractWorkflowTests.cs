using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryMetadataContractWorkflowTests
{
    [Test]
    public void ActualEmittedParameterContractIsRejectedAsAMetadataClauseOwner()
    {
        using var temporary = new TempDirectory("SharpProof-metadata-contract-");
        const string librarySource = """
            public static class MetadataHelper {
                public static int Target([SharpProof.Attributes.Positive] int value) => value;
            }
            """;
        var options = AnalyzerGateHost.ParseOptions.WithPreprocessorSymbols(Contract.ConditionalSymbol);
        var libraryTree = CSharpSyntaxTree.ParseText(librarySource, options, "MetadataHelper.cs");
        var library = AnalyzerGateHost.CreateCompilation(string.Empty,
                "OrdinaryMetadataContractLibrary", includeExternalEffectsFixture: false)
            .RemoveAllSyntaxTrees().AddSyntaxTrees(libraryTree);
        var libraryPath = Path.Combine(temporary.FullName, "OrdinaryMetadataContractLibrary.dll");
        using (var image = File.Create(libraryPath))
        {
            var emitted = library.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        var compilation = AnalyzerGateHost.CreateCompilation("""
            public static class Caller {
                public static int Root(int value) => MetadataHelper.Target(value);
            }
            """, "OrdinaryMetadataContractCaller", includeExternalEffectsFixture: false)
            .AddReferences(MetadataReference.CreateFromFile(libraryPath));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var method = compilation.GetTypeByMetadataName("MetadataHelper")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(method.Parameters.Single().GetAttributes().Single().AttributeClass!.ToDisplayString(),
            Is.EqualTo("SharpProof.Attributes.PositiveAttribute"));
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        Assert.That(session.HasPotentialCallPreconditions(method), Is.True);
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        Assert.That(inventory.Gaps, Is.Empty);
        var owner = inventory.Owners.Single();
        var call = owner.Calls.Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(call.Target, method), Is.True);
        Assert.That(owner.DiscoveryComplete, Is.True);

        var coverage = CorpusGate.MatchNativeContractCalls(compilation, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Expected, Is.Zero);
            Assert.That(coverage.Matched, Is.Zero);
            Assert.That(coverage.Failures, Has.Length.EqualTo(1));
            Assert.That(coverage.Failures.Single(), Is.EqualTo("UnsupportedClauseOwner:" + owner.CallableId));
        }
    }
}
