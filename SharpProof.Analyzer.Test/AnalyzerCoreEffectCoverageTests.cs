using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Effects;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreEffectCoverageTests
{
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
}
