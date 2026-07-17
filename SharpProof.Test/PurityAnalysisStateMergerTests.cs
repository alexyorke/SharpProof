using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic.Ir;
using PotentialTargets = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PotentialTargets;
using PurityAnalysisResult = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisResult;
using PurityAnalysisState = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisState;
using PurityEvidence = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityEvidence;

namespace SharpProof.Test;

[TestFixture]
public sealed class PurityAnalysisStateMergerTests
{
    [Test]
    public void MergeStatesAcrossAll_EmptyAndSingletonPreserveState()
    {
        var singleton = PurityAnalysisState.Pure.WithPathState(
            new SymbolicState().WithSymbolVersion("value", 7));

        Assert.Multiple(() =>
        {
            Assert.That(PurityAnalysisStateMerger.MergeStatesAcrossAll([], 3),
                Is.EqualTo(PurityAnalysisState.Pure));
            Assert.That(PurityAnalysisStateMerger.MergeStatesAcrossAll([singleton], 3),
                Is.EqualTo(singleton));
        });
    }

    [Test]
    public void MergeStates_NullLocationImpurityIsReplacedByConcreteEvidence()
    {
        var (_, _, firstNode, _) = CreateSymbols();
        var unknown = new PurityAnalysisState(
            true, null, null, null,
            firstImpurityEvidence: PurityEvidence.Create("unknown-first"));
        var concreteEvidence = PurityEvidence.Create("concrete");
        var concrete = new PurityAnalysisState(
            true, firstNode, null, null,
            firstImpurityEvidence: concreteEvidence);

        var merged = PurityAnalysisStateMerger.MergeStates(unknown, concrete, 4);

        Assert.Multiple(() =>
        {
            Assert.That(merged.FirstImpureSyntaxNode, Is.SameAs(firstNode));
            Assert.That(merged.FirstImpurityEvidence.Category, Is.EqualTo(concreteEvidence.Category));
        });
    }

    [Test]
    public void MergeStatesAcrossAll_PreservesOrderedMetadataAndCanonicalPathMerge()
    {
        var (delegateSymbol, otherSymbol, firstNode, secondNode) = CreateSymbols();
        var firstMethod = (IMethodSymbol)delegateSymbol.ContainingType.GetMembers("A").Single();
        var secondMethod = (IMethodSymbol)delegateSymbol.ContainingType.GetMembers("B").Single();
        var capture = default(CaptureId);
        var firstEvidence = PurityEvidence.Create("first-concrete");
        var earliestEvidence = PurityEvidence.Create("earliest-concrete");
        var states = new[]
        {
            CreateState(
                secondNode, firstEvidence, delegateSymbol, otherSymbol,
                PotentialTargets.FromSingle(firstMethod), PurityAnalysisResult.Pure,
                firstMethod, delegateSymbol, 1),
            CreateState(
                null, PurityEvidence.Create("later-unknown"), delegateSymbol, null,
                PotentialTargets.FromSingle(secondMethod), PurityAnalysisResult.Impure(firstNode),
                secondMethod, delegateSymbol, 2),
            CreateState(
                firstNode, earliestEvidence, delegateSymbol, null,
                PotentialTargets.Unresolved, PurityAnalysisResult.Pure,
                null, otherSymbol, 3)
        };

        var merged = PurityAnalysisStateMerger.MergeStatesAcrossAll(states, 9);
        var expectedPath = SymbolicStateMerger.MergePathStatesAcrossAll(
            states.Select(static state => state.PathState).ToArray(),
            SymbolicStateMerger.AreEvidenceEquivalentFacts,
            9);

        Assert.Multiple(() =>
        {
            Assert.That(merged.HasPotentialImpurity, Is.True);
            Assert.That(merged.FirstImpureSyntaxNode, Is.SameAs(firstNode));
            Assert.That(merged.FirstImpurityEvidence.Category, Is.EqualTo(earliestEvidence.Category));
            Assert.That(merged.DelegateTargetMap, Has.Count.EqualTo(1));
            Assert.That(merged.DelegateTargetMap.KeyComparer, Is.SameAs(SymbolEqualityComparer.Default));
            Assert.That(merged.DelegateTargetMap[delegateSymbol].IsUnresolved, Is.True);
            Assert.That(merged.FlowCaptures[capture].IsPure, Is.False);
            Assert.That(merged.FlowCaptureTargets[capture].IsUnresolved, Is.True);
            Assert.That(merged.FlowCaptureSymbols, Is.Empty);
            Assert.That(merged.PathState.NormalizedProofKey, Is.EqualTo(expectedPath.NormalizedProofKey));
        });
    }

    private static PurityAnalysisState CreateState(
        SyntaxNode? impurityNode,
        PurityEvidence evidence,
        ISymbol delegateSymbol,
        ISymbol? uniqueDelegateSymbol,
        PotentialTargets targets,
        PurityAnalysisResult captureResult,
        IMethodSymbol? captureTarget,
        ISymbol captureSymbol,
        int version)
    {
        var delegates = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default)
            .Add(delegateSymbol, targets);
        if (uniqueDelegateSymbol != null)
            delegates = delegates.Add(uniqueDelegateSymbol, PotentialTargets.Empty);
        var capture = default(CaptureId);
        return new PurityAnalysisState(
            true,
            impurityNode,
            delegates,
            ImmutableDictionary<CaptureId, PurityAnalysisResult>.Empty.Add(capture, captureResult),
            ImmutableDictionary<CaptureId, PotentialTargets>.Empty.Add(
                capture,
                captureTarget == null ? PotentialTargets.Unresolved : PotentialTargets.FromSingle(captureTarget)),
            evidence,
            new SymbolicState().WithSymbolVersion("value", version),
            ImmutableDictionary<CaptureId, ISymbol>.Empty.Add(capture, captureSymbol));
    }

    private static (ISymbol Delegate, ISymbol Other, SyntaxNode First, SyntaxNode Second) CreateSymbols()
    {
        const string source = "class C { void A() { } void B() { } void M() { int d = 0, e = 0; } }";
        var fixture = RoslynTestFixture.CreateCompilation(source, nameof(PurityAnalysisStateMergerTests));
        var methods = fixture.Root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var variables = fixture.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToArray();
        return (
            fixture.SemanticModel.GetDeclaredSymbol(variables[0])!,
            fixture.SemanticModel.GetDeclaredSymbol(variables[1])!,
            methods[0],
            methods[1]);
    }
}
