using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreMalformedCalleeDeclarationTests
{
    private static readonly ImmutableArray<string> MissingBodyErrorIds = ["CS0501"];

    [TestCase(false)]
    [TestCase(true)]
    public async Task OrdinaryMissingCalleeBodyWithACompanionRetainsAnUnprovedCaller(bool missingBody)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            public static class Guard
            {
                public static int Need(int value) {{(missingBody ? ";" : "=> value;")}}
            }
            [ContractFor(typeof(Guard))]
            public static class GuardContracts
            {
                public static int Need(int value)
                {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            public static class Fixture
            {
                public static int Target() => Guard.Need(1);
            }
            """, ["SP0027"]);
        var errors = compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors.Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Id)), Is.EqualTo(missingBody ? MissingBodyErrorIds : Array.Empty<string>()));
        using (var image = new MemoryStream())
        {
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.EqualTo(!missingBody));
            Assert.That(emitted.Diagnostics.Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)).Select(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Id)),
                Is.EqualTo(missingBody ? MissingBodyErrorIds : Array.Empty<string>()));
        }

        var tree = compilation.SyntaxTrees.Single();
        var model = CompilationModelProvider.GetSemanticModel(compilation, tree);
        Assert.That(compilation.ContainsSyntaxTree(tree), Is.True);
        Assert.That(model.Compilation, Is.SameAs(compilation));
        var declarations = (await tree.GetRootAsync().ConfigureAwait(false)).DescendantNodes().OfType<MethodDeclarationSyntax>().ToImmutableArray();
        var calleeSyntax = declarations.Single(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Parent is ClassDeclarationSyntax c && c.Identifier.ValueText == "Guard"));
        var callerSyntax = declarations.Single(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Identifier.ValueText == "Target"));
        var callee = (IMethodSymbol)model.GetDeclaredSymbol(calleeSyntax)!;
        var caller = (IMethodSymbol)model.GetDeclaredSymbol(callerSyntax)!;
        Assert.That(callee.IsStatic, Is.True);
        Assert.That(callee.IsAbstract || callee.IsExtern || callee.IsVirtual || callee.IsOverride || callee.IsAsync, Is.False);
        Assert.That(callee.PartialDefinitionPart, Is.Null);
        Assert.That(callee.PartialImplementationPart, Is.Null);
        Assert.That(callee.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(await callee.DeclaringSyntaxReferences[0].GetSyntaxAsync().ConfigureAwait(false), Is.SameAs(calleeSyntax));
        Assert.That(SymbolEqualityComparer.Default.Equals(callee.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(model.GetOperation(calleeSyntax) == null, Is.EqualTo(missingBody));

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binding = new ContractBinder(compilation, factory).BindTotalRequires(new TotalLoweringContext(factory, callee));
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        Assert.That(binding.Clauses, Has.Length.EqualTo(1));
        Assert.That(binding.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(binding.Clauses[0].Evidence, Is.EqualTo(BoundContractEvidence.Companion));
        var graph = TotalBodyLowering.CreateGraph(callerSyntax, caller, model, TestContext.CurrentContext.CancellationToken);
        Assert.That(graph, Is.Not.Null);
        var lowered = TotalBodyLowering.Lower(compilation, graph!, new TotalLoweringContext(factory, caller), TestContext.CurrentContext.CancellationToken);
        Assert.That(lowered.IsExact, Is.EqualTo(!missingBody));

        var sessions = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", analyzer: new SharpProofAnalyzer(sessions),
            allowCompilationErrors: missingBody);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(sessions.CreateCount, Is.EqualTo(1));
        var analyzed = sessions.Session!.Compilation;
        Assert.That(analyzed.SyntaxTrees.Single(), Is.SameAs(tree));
        Assert.That(analyzed.References, Is.EqualTo(compilation.References).Using<MetadataReference>((actual, expected) => actual is null ? throw new System.ArgumentNullException(nameof(actual)) : expected is null ? throw new System.ArgumentNullException(nameof(expected)) : (ReferenceEquals(actual, expected))));
        Assert.That(analyzed.Assembly.Identity, Is.EqualTo(compilation.Assembly.Identity));
        var analyzedModel = CompilationModelProvider.GetSemanticModel(analyzed, tree);
        Assert.That(analyzedModel.Compilation, Is.SameAs(analyzed));
        Assert.That(analyzedModel.GetOperation(calleeSyntax) == null, Is.EqualTo(missingBody));
        Assert.That(sessions.GetNamedOutcome("Target"), Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }
}
