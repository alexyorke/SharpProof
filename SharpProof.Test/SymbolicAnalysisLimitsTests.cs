using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicAnalysisLimitsTests
{
    [Test]
    public void AnalysisLimits_DefaultsAndOverridesAreStable()
    {
        var defaults = SymbolicAnalysisLimits.Default;

        Assert.That(defaults.MaxMergedIfElseFacts, Is.EqualTo(16));
        Assert.That(defaults.MaxMergedSwitchFacts, Is.EqualTo(32));
        Assert.That(defaults.MaxMergedTryFacts, Is.EqualTo(16));
        Assert.That(defaults.MaxTryCompletionBranches, Is.EqualTo(8));
        Assert.That(defaults.MaxFiniteForeachElementFacts, Is.EqualTo(8));
        Assert.That(defaults.MaxScopedBlockCompletionStatements, Is.EqualTo(32));
        Assert.That(defaults.MaxStructuralNullStateDepth, Is.EqualTo(4));
        Assert.That(defaults.MaxMergedPathConditions, Is.EqualTo(32));
        Assert.That(defaults.MaxMergeableFactsPerTargetPerState, Is.EqualTo(4));
        Assert.That(defaults.MaxFactChoiceCombinationsPerTarget, Is.EqualTo(64));
        Assert.That(defaults.MaxGuardFactsPerTargetPerState, Is.EqualTo(6));

        var overridden = defaults.WithOverrides(
            maxMergedIfElseFacts: 3,
            maxFiniteForeachElementFacts: 5,
            maxStructuralNullStateDepth: 7,
            maxMergedPathConditions: 11);

        Assert.That(overridden.MaxMergedIfElseFacts, Is.EqualTo(3));
        Assert.That(overridden.MaxFiniteForeachElementFacts, Is.EqualTo(5));
        Assert.That(overridden.MaxStructuralNullStateDepth, Is.EqualTo(7));
        Assert.That(overridden.MaxMergedPathConditions, Is.EqualTo(11));
        Assert.That(overridden.MaxMergedSwitchFacts, Is.EqualTo(defaults.MaxMergedSwitchFacts));
    }

    [Test]
    public void AnalysisLimits_RejectNonPositiveValues()
    {
        Assert.That(
            () => new SymbolicAnalysisLimits(maxMergedIfElseFacts: 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => SymbolicAnalysisLimits.Default.WithOverrides(maxStructuralNullStateDepth: -1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void AnalysisLimitScope_DeduplicatesEventsAndPreservesNestedSourceLocations()
    {
        var sourceNode = CSharpSyntaxTree.ParseText("class C { void M() { } }")
            .GetRoot()
            .DescendantNodes()
            .Single(node => node.RawKind == (int)SyntaxKind.MethodDeclaration);

        using var outer = SymbolicAnalysisLimitContext.Push();
        SymbolicAnalysisLimitContext.Record(
            SymbolicAnalysisLimitKind.IfElseFactMerge,
            2,
            3,
            sourceNode,
            "test.if");
        SymbolicAnalysisLimitContext.Record(
            SymbolicAnalysisLimitKind.IfElseFactMerge,
            2,
            5,
            sourceNode,
            "test.if");

        using (SymbolicAnalysisLimitContext.Push())
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.ForeachElementFacts,
                1,
                4,
                sourceNode,
                "test.foreach");

        var info = outer.Snapshot();

        Assert.That(info.IsTruncated, Is.True);
        Assert.That(info.Events, Has.Count.EqualTo(2));
        Assert.That(
            info.Events.Single(item => item.Kind == SymbolicAnalysisLimitKind.IfElseFactMerge).Observed,
            Is.EqualTo(5));
        Assert.That(info.Events.All(item => item.SourceSpanStart == sourceNode.SpanStart), Is.True);
        Assert.That(
            info.Events.Select(item => item.Code),
            Is.EquivalentTo(new[]
            {
                "analysis_limit.if_else_fact_merge",
                "analysis_limit.foreach_element_facts"
            }));
    }
}
