using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Effects;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Specs;
using OperationId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrOperationTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreIrEffectCoverageTests
{
    [TestCase(SpecEffect.InputOutput, "IO")]
    [TestCase(SpecEffect.Synchronization, "Synchronization")]
    [TestCase(SpecEffect.NativeCode, "NativeInterop")]
    [TestCase(SpecEffect.Reflection, "Reflection")]
    [TestCase(SpecEffect.Nondeterminism, "Randomness")]
    [TestCase(SpecEffect.ReadsAmbientState, "None")]
    [TestCase(SpecEffect.WritesAmbientState, "None")]
    [TestCase(SpecEffect.Unknown, "IO")]
    [TestCase(SpecEffect.None, "None")]
    public void ValidatedLegacyApiFacetsRetainTheirReachableCapabilityBounds(SpecEffect effect, string capability)
    {
        var (compilation, callee, caller, declaration, invocation) = Caller();
        var facets = ResolveFacets(compilation, callee, effect);
        var flags = TotalBodyLowering.ApiSpecEffects(facets);
        // API facets carry legacy effect flags. Trusted metadata contracts use
        // WithCapabilities instead; that distinct path is checked below.
        Assert.That(IrOpaqueCallSite.Capabilities(flags), Is.Null);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, site) = CallProgram(factory, callee, invocation, flags, sourceSpan: true);
        Assert.That(IrOpaqueCallSite.Effects(factory, site), Is.EqualTo(flags));
        var analysis = AdvisoryEffectSites.Analyze(program,
            operation => factory.GetOperationInfo(operation).SourceSpan != null ? invocation.GetLocation() : null,
            declaration.Identifier.GetLocation(), CancellationToken.None);
        Assert.That(analysis.Gap, Is.Null);
        var call = analysis.Sites.Single();
        Assert.That(call.Kind, Is.EqualTo(AdvisoryEffectSiteKind.Call));
        Assert.That(call.Effects, Is.EqualTo(flags));
        Assert.That(call.Location.SourceSpan, Is.EqualTo(invocation.Span));
        Assert.That(call.Location.SourceTree, Is.SameAs(invocation.SyntaxTree));
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        var claims = EffectContractDiagnostics.Declare(caller, declaration.Identifier.GetLocation(), session, CancellationToken.None);
        var permission = claims.Single(claim => claim.Kind == EffectEvaluationContractKind.AllowedCapabilities);
        var violation = AdvisoryEffectClaims.FindViolation(permission.Kind, permission.Constraint, analysis.Sites, compilation);
        if (capability == "None")
        {
            Assert.That(violation, Is.Null);
        }
        else
        {
            Assert.That(violation, Is.Not.Null);
            Assert.That(violation!.Value.Detail, Does.Contain(capability));
        }
        var contract = claims.Single(claim => claim.Kind == EffectEvaluationContractKind.EffectContract);
        var undeclared = AdvisoryEffectClaims.FindViolation(contract.Kind, contract.Constraint, analysis.Sites, compilation);
        Assert.That(undeclared.HasValue, Is.EqualTo(effect != SpecEffect.None));
    }

    [Test]
    public void ASpanlessIrCallUsesTheCallersDeclaredSourceLocation()
    {
        var (_, callee, _, declaration, invocation) = Caller();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, _) = CallProgram(factory, callee, invocation, IrOpaqueCallEffects.None, sourceSpan: false);
        var fallback = declaration.Identifier.GetLocation();
        var analysis = AdvisoryEffectSites.Analyze(program, _ => null, fallback, CancellationToken.None);
        Assert.That(analysis.Gap, Is.Null);
        Assert.That(analysis.Sites.Single().Location, Is.SameAs(fallback));
    }

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
        var declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
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

    private static (CSharpCompilation Compilation, IMethodSymbol Callee, IMethodSymbol Caller,
        MethodDeclarationSyntax Declaration, InvocationExpressionSyntax Invocation) Caller()
    {
        var external = AnalyzerTestHost.EmitReference(
            "public static class Api { public static extern int Value(); }", "AnalyzerCoreLegacyFacetBoundary");
        var compilation = AnalyzerTestHost.CreateCompilation(
            "using SharpProof.Attributes; public static class Fixture { " +
            "[AllowedCapabilities(SharpProofCapability.None), EffectContract(SharpProofEffect.None, Complete = true)] " +
            "public static int Target() { Api.Value(); return 1; } }", [], [external]);
        var declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var caller = (IMethodSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!;
        var callee = compilation.GetTypeByMetadataName("Api")!.GetMembers("Value").OfType<IMethodSymbol>().Single();
        return (compilation, callee, caller, declaration, declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Single());
    }

    private static ApiSpecFacets ResolveFacets(CSharpCompilation compilation, IMethodSymbol callee, SpecEffect effect)
    {
        var evidence = new SpecEvidence(SpecEvidenceKind.Documented, "Declared may-effect bound for the component control.");
        var table = ApiSpecTable.Create([new ApiSpecDeclaration(
            new ApiSpecTarget("analyzer.legacy.effect", callee.GetDocumentationCommentId()!, "Api",
                SpecTargetMemberKind.Method, "Value", true, 0, null, [], IrTypeKind.Integer,
                [new ApiSpecAssemblyIdentity(callee.ContainingAssembly.Identity.Name, string.Empty)]),
            new ApiSpecFacets(new SpecEffectFacet(effect, evidence),
                new SpecAllocationFacet(SpecAllocationBehavior.None, evidence),
                new SpecThrowFacet(SpecThrowBehavior.DoesNotThrow, [], evidence),
                new SpecNullnessFacet(SpecNullness.NotApplicable, evidence),
                new SpecCardinalityFacet(SpecCardinality.NotApplicable, null, evidence),
                new SpecTerminationFacet(SpecTerminationBehavior.Terminates, evidence)), [])]);
        var resolved = new ApiSpecResolver(table).Resolve(compilation);
        Assert.That(resolved.Failures, Is.Empty);
        Assert.That(resolved.TryGet(callee, out var spec), Is.True);
        return spec!.Template.Facets;
    }

    private static (IrProgram Program, OperationId Site) CallProgram(IrFactory factory, IMethodSymbol callee,
        InvocationExpressionSyntax invocation, IrOpaqueCallEffects flags, bool sourceSpan)
    {
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var unreachable = builder.CreateBlock();
        var member = factory.GetOrCreateMember(CompilerIdentityBridge.InternSymbol(factory, callee),
            factory.GetOrCreateReferenceType(CompilerIdentityBridge.InternType(factory, callee.ContainingType),
                CompilerIdentityBridge.CreateTypeDisplay(callee.ContainingType)), callee.Name, factory.BooleanType, true, []);
        var position = invocation.SyntaxTree.GetLineSpan(invocation.Span).StartLinePosition;
        IrSourceSpan? span = sourceSpan ? new IrSourceSpan(invocation.SyntaxTree.FilePath,
            invocation.SpanStart, invocation.Span.Length, position.Line + 1, position.Character + 1) : null;
        var site = factory.CreateOperation(IrOpaqueCallSite.Describe(flags, callee.ToDisplayString()), span);
        builder.Call(entry, site, null, member, null);
        builder.Return(entry, factory.CreateOperation(), factory.Integer(1));
        builder.Call(unreachable, factory.CreateOperation(IrOpaqueCallSite.Describe(IrOpaqueCallEffects.All,
            callee.ToDisplayString())), null, member, null);
        builder.Return(unreachable, factory.CreateOperation(), factory.Integer(1));
        return (builder.Build(), site);
    }
}
