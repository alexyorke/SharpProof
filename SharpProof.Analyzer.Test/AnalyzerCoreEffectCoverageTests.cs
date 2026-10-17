using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Effects;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreEffectCoverageTests
{
    [Test]
    public void AMethodWithoutEffectClaimsIsNotApplicable()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class Fixture { public static int Target() => 1; }", []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, TestContext.CurrentContext.CancellationToken);
        var diagnostics = new List<Diagnostic>();
        var outcome = EffectContractDiagnostics.Analyze(method, declaration, session, diagnostics.Add, TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ReviewedSynchronizationBoundaryStillRequiresSynchronizationPermission()
    {
        var external = AnalyzerTestHost.EmitReference(
            "using SharpProof.Attributes; public static class ExternalFixture { " +
            "[SharpProofTrusted(\"Reviewed synchronization boundary.\")] " +
            "[EffectContract(SharpProofEffect.Synchronizes, PreconditionFree = true, Complete = true)] " +
            "public static extern int Synchronize(); }", "AnalyzerCoreSynchronizationBoundary");
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { " +
            "[AllowedCapabilities(SharpProofCapability.None)] " +
            "public static int Target() => ExternalFixture.Synchronize(); }",
            "effects", ["SP0016"], new SharpProofAnalyzer(factory), additionalReferences: [external]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0016");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "Synchronization");
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [Test]
    public async Task DeclaredAllocationDoesNotBecomeAnAdvisoryProof()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { " +
            "[EffectContract(SharpProofEffect.Allocates, Complete = true)] " +
            "public static object Target() => new object(); }",
            "effects", ["SP0052"], new SharpProofAnalyzer(factory));
        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [TestCase("var value = new object(); return x;", "Allocates", "SharpProofEffect.Throws")]
    [TestCase("lock (gate) { } return x;", "Synchronizes", "SharpProofEffect.Throws")]
    [TestCase("return State;", "ReadsReceiverState", "SharpProofEffect.Throws")]
    [TestCase("values[0] = x; return x;", "WritesArgumentState",
        "SharpProofEffect.Throws | SharpProofEffect.ReadsReceiverState | SharpProofEffect.ReadsArgumentState | " +
        "SharpProofEffect.ReadsCapturedState | SharpProofEffect.ReadsStaticState | SharpProofEffect.ReadsAmbientState")]
    [TestCase("State = x; return x;", "WritesStaticState", "SharpProofEffect.Throws")]
    [TestCase("throw new System.InvalidOperationException();", "Allocates", "SharpProofEffect.Throws")]
    public async Task EffectContractReportsUndeclaredReachableEffects(string body, string detail, string declaredEffects)
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public class Fixture { public static int State; " +
            "[EffectContract(" + declaredEffects + ", ThrownExceptions = new[] { typeof(System.Exception) }, Complete = true)] " +
            "public int Target(int x, object gate, int[] values) { " + body + " } }",
            "effects", ["SP0052"], new SharpProofAnalyzer(factory));
        AnalyzerTestHost.AssertIds(diagnostics, "SP0052");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], detail);
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }

    [TestCase("SharpProofEffect.WritesArgumentState", "WritesArgumentState")]
    [TestCase("SharpProofEffect.ReadsArgumentState", "ReadsArgumentState")]
    [TestCase("SharpProofEffect.Allocates", "Allocates")]
    [TestCase("SharpProofEffect.Throws", "Throws")]
    [TestCase("SharpProofEffect.Synchronizes", "Synchronizes")]
    [TestCase("SharpProofEffect.ReadsAmbientState | SharpProofEffect.WritesAmbientState", "WritesAmbientState")]
    [TestCase("SharpProofEffect.UsesNondeterminism", "UsesNondeterminism")]
    [TestCase("SharpProofEffect.UsesNativeCode", "UsesNativeCode")]
    [TestCase("SharpProofEffect.UsesReflection", "UsesReflection")]
    public async Task ExternalBoundaryEffectsRemainVisibleToCaller(string effects, string detail)
    {
        var exceptions = effects == "SharpProofEffect.Throws"
            ? ", ThrownExceptions = new[] { typeof(System.Exception) }" : "";
        var external = AnalyzerTestHost.EmitReference(
            "using SharpProof.Attributes; public static class ExternalFixture { " +
            "[SharpProofTrusted(\"Reviewed boundary.\")] [EffectContract(" + effects + exceptions +
            ", PreconditionFree = true, Complete = true)] " +
            "public static extern int Value(int[] values); }", "AnalyzerCoreEffectBoundary");
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { " +
            "[EffectContract(SharpProofEffect.None, Complete = true)] " +
            "public static int Target(int[] values) => ExternalFixture.Value(values); }",
            "effects", ["SP0052"], new SharpProofAnalyzer(factory), additionalReferences: [external]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0052");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], detail);
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        var callee = (IMethodSymbol)factory.Session!.Compilation.GetTypeByMetadataName("ExternalFixture")!
            .GetMembers("Value").Single();
        Assert.That(factory.Session.ResolveEffectContract(callee).Kind, Is.EqualTo(EffectContractResolutionKind.Valid));
    }

    [Test]
    public async Task UnspecifiedExternalBoundaryCannotSatisfyEmptyContract()
    {
        var external = AnalyzerTestHost.EmitReference(
            "public static class ExternalFixture { public static extern int Value(); }",
            "AnalyzerCoreUnspecifiedBoundary");
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { " +
            "[EffectContract(SharpProofEffect.None, Complete = true)] " +
            "public static int Target() => ExternalFixture.Value(); }",
            "effects", ["SP0052"], additionalReferences: [external]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0052");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "UsesReflection");
    }
}
