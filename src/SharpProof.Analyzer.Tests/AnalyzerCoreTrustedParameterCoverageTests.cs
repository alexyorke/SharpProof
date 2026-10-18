using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Effects;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreTrustedParameterCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RealMetadataParameterAttributesRetainConditionalCallBounds(bool positiveParameter)
    {
        var parameter = positiveParameter
            ? "[Positive] int value"
            : "[System.Runtime.CompilerServices.CallerLineNumber] int value = 0";
        var reference = AnalyzerTestHost.EmitReference(
            "using SharpProof.Attributes; public static class Api { " +
            "[SharpProofTrusted(\"Reviewed ordinary declaration.\"), " +
            "EffectContract(SharpProofEffect.None, Complete = true, PreconditionFree = false)] " +
            "public static int Value(" + parameter + ") => value; }", "AnalyzerCoreTrustedParameter");
        var compilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Fixture { " +
            "[EnforcePure] public static int Selected() => 1; public static int Target() => Api.Value(1); }",
            ["SP0002"], additionalReferences: [reference]);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Session, Is.Not.Null);
        var actual = (CSharpCompilation)factory.Session!.Compilation;
        var callee = actual.GetTypeByMetadataName("Api")!.GetMembers("Value").OfType<IMethodSymbol>().Single();
        Assert.That(callee.DeclaringSyntaxReferences, Is.Empty);
        var attribute = callee.Parameters.Single().GetAttributes().Single();
        Assert.That(attribute.ApplicationSyntaxReference, Is.Null);
        Assert.That(attribute.AttributeClass!.ToDisplayString(), Is.EqualTo(positiveParameter
            ? "SharpProof.Attributes.PositiveAttribute"
            : "System.Runtime.CompilerServices.CallerLineNumberAttribute"));
        var resolution = factory.Session.ResolveEffectContract(callee);
        Assert.That(resolution.Kind, Is.EqualTo(EffectContractResolutionKind.Incomplete));
        Assert.That(resolution.Summary.AnalysisIncompleteReason, Is.EqualTo(EffectAnalysisIncompleteReason.CallPreconditionNotProven));
        var root = await actual.SyntaxTrees.Single().GetRootAsync().ConfigureAwait(false);
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var caller = (IMethodSymbol)actual.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!;
        var (body, gap) = AdvisoryLowering.Lower(actual, caller, declaration, TestContext.CurrentContext.CancellationToken);
        Assert.That(gap, Is.Null);
        Assert.That(body, Is.Not.Null);
        var analysis = AdvisoryEffectSites.Analyze(body!.Program, body.Locate, caller.Locations.Single(), TestContext.CurrentContext.CancellationToken);
        Assert.That(analysis.Gap, Is.Null);
        var call = analysis.Sites.Single(site => site is null ? throw new System.ArgumentNullException(nameof(site)) : (site.Kind == AdvisoryEffectSiteKind.Call));
        Assert.That(call.Effects, Is.EqualTo(positiveParameter
            ? IrOpaqueCallEffects.All
            : IrOpaqueCallSite.WithCapabilities(IrOpaqueCallEffects.None, 0)));
        var invocation = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        Assert.That(call.Location.SourceTree, Is.SameAs(invocation.SyntaxTree));
        Assert.That(call.Location.SourceSpan, Is.EqualTo(invocation.Span));
    }
}
