using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Effects;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreIrEffectCoverageTests
{
    [Test]
    public async Task AReviewedIoBoundaryRetainsItsParsedExplicitCapabilityEncoding()
    {
        var external = AnalyzerTestHost.EmitReference(
            "using SharpProof.Attributes; public static class Api { " +
            "[SharpProofTrusted(\"Reviewed IO boundary.\")] " +
            "[EffectContract(SharpProofEffect.None, Capabilities = SharpProofCapability.IO, " +
            "PreconditionFree = true, Complete = true)] public static extern int Value(); }",
            "AnalyzerCoreExplicitIoBoundary");
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Fixture { " +
            "[AllowedCapabilities(SharpProofCapability.None)] public static int Target() => Api.Value(); }",
            "effects", ["SP0016"], new SharpProofAnalyzer(factory), additionalReferences: [external]);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0016");
        AnalyzerTestHost.AssertMessageContains(diagnostics[0], "IO");
        Assert.That(factory.Outcomes["Target"], Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        var compilation = (CSharpCompilation)factory.Session!.Compilation;
        var callee = compilation.GetTypeByMetadataName("Api")!.GetMembers("Value").OfType<IMethodSymbol>().Single();
        var resolved = factory.Session.ResolveEffectContract(callee);
        Assert.That(resolved.Kind, Is.EqualTo(EffectContractResolutionKind.Valid));
        var projected = EffectSummaryProjector.Project(resolved.Summary);
        Assert.That(projected.Capabilities, Is.EqualTo(EffectContractCapabilityKind.IO));
        var root = await compilation.SyntaxTrees.Single().GetRootAsync().ConfigureAwait(false);
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!;
        var (body, gap) = AdvisoryLowering.Lower(compilation, method, declaration, CancellationToken.None);
        Assert.That(gap, Is.Null);
        Assert.That(body, Is.Not.Null);
        var call = body!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>()
            .Single(instruction => instruction.Receiver == null && instruction.Target == null);
        var flags = IrOpaqueCallSite.Effects(body.Program.Factory, call.Operation);
        Assert.That(IrOpaqueCallSite.Capabilities(flags), Is.EqualTo((int)projected.Capabilities));
        var direct = AdvisoryEffectSites.Analyze(body.Program, body.Locate, method.Locations[0], CancellationToken.None);
        var ordinary = AdvisoryEffectSites.Analyze(compilation, method, declaration, CancellationToken.None);
        Assert.That(direct.Sites, Is.EqualTo(ordinary.Sites));
    }
}
