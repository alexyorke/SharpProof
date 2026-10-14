using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend.Host;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreDuplicateSourceReferenceTests
{
    [Test]
    public async Task MergedSourceReferenceAliasesRetainConservativeAdvisoryActivation()
    {
        var origin = AnalyzerTestHost.CreateCompilation(
            "namespace Remote { public static class Guard { public static int Value() => 1; } }", [])
            .WithAssemblyName("DuplicateSourceReferenceOrigin");
        var aliased = origin.ToMetadataReference(aliases: ["remote"]);
        var global = origin.ToMetadataReference();
        var caller = AnalyzerTestHost.CreateCompilation(
            """
            extern alias remote;
            public static class Fixture
            {
                public static int Target() => remote::Remote.Guard.Value() + Remote.Guard.Value();
            }
            """, ["SP0027"], additionalReferences: [aliased, global]);

        Assert.That(aliased, Is.Not.SameAs(global));
        Assert.That(aliased.Compilation, Is.SameAs(origin));
        Assert.That(global.Compilation, Is.SameAs(origin));
        Assert.That(caller.References.Contains(aliased), Is.True);
        Assert.That(caller.References.Contains(global), Is.True);
        Assert.That(origin.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(caller.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(AnalyzerTestHost.EmitImage(origin), Is.Not.Empty);
        Assert.That(AnalyzerTestHost.EmitImage(caller), Is.Not.Empty);
        Assert.That(caller.GetAssemblyOrModuleSymbol(aliased), Is.Null);
        Assert.That(caller.GetAssemblyOrModuleSymbol(global), Is.Not.Null);

        var callerTree = caller.SyntaxTrees.Single();
        var originTree = origin.SyntaxTrees.Single();
        Assert.That(caller.ContainsSyntaxTree(callerTree), Is.True);
        Assert.That(caller.ContainsSyntaxTree(originTree), Is.False);
        var callerModel = CompilationModelProvider.GetSemanticModel(caller, callerTree);
        var originModel = CompilationModelProvider.GetSemanticModel(caller, originTree);
        Assert.That(callerModel.Compilation, Is.SameAs(caller));
        Assert.That(originModel.Compilation, Is.SameAs(origin));
        var calls = (await callerTree.GetRootAsync().ConfigureAwait(false)).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        Assert.That(calls, Has.Length.EqualTo(2));
        foreach (var call in calls)
        {
            var target = (IMethodSymbol)callerModel.GetSymbolInfo(call).Symbol!;
            Assert.That(target.Name, Is.EqualTo("Value"));
            Assert.That(target.ContainingAssembly.Identity, Is.EqualTo(origin.Assembly.Identity));
        }

        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            caller, mode: null, analyzer: new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        var analyzed = factory.Session!.Compilation;
        Assert.That(analyzed.SyntaxTrees.Single(), Is.SameAs(callerTree));
        Assert.That(analyzed.References, Is.EqualTo(caller.References).Using<MetadataReference>((actual, expected) => ReferenceEquals(actual, expected)));
        Assert.That(analyzed.Assembly.Identity, Is.EqualTo(caller.Assembly.Identity));
        Assert.That(analyzed.GetAssemblyOrModuleSymbol(aliased), Is.Null);
        Assert.That(analyzed.GetAssemblyOrModuleSymbol(global), Is.Not.Null);
        Assert.That(CompilationModelProvider.GetSemanticModel(analyzed, callerTree).Compilation, Is.SameAs(analyzed));
        Assert.That(CompilationModelProvider.GetSemanticModel(analyzed, originTree).Compilation, Is.SameAs(origin));
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
    }
}
