using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicAnalysisLimitsTests {
    [Test]
    public void AnalysisLimits_DefaultsAndOverridesAreStable() {
        var defaults = SharpProofAnalysisBudget.Default;

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

        var overridden = defaults with {
            MaxMergedIfElseFacts = 3,
            MaxFiniteForeachElementFacts = 5,
            MaxStructuralNullStateDepth = 7,
            MaxMergedPathConditions = 11
        };

        Assert.That(overridden.MaxMergedIfElseFacts, Is.EqualTo(3));
        Assert.That(overridden.MaxFiniteForeachElementFacts, Is.EqualTo(5));
        Assert.That(overridden.MaxStructuralNullStateDepth, Is.EqualTo(7));
        Assert.That(overridden.MaxMergedPathConditions, Is.EqualTo(11));
        Assert.That(overridden.MaxMergedSwitchFacts, Is.EqualTo(defaults.MaxMergedSwitchFacts));
    }

    [Test]
    public void AnalysisLimits_RejectNonPositiveValues() {
        Assert.That(
            () => new SharpProofAnalysisBudget(MaxMergedIfElseFacts: 0).Validate(),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => (SharpProofAnalysisBudget.Default with { MaxStructuralNullStateDepth = -1 }).Validate(),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void AnalysisLimitEvent_UnknownKindRemainsExplicit() {
        var item = new SymbolicAnalysisTruncationEvent(
            (SymbolicAnalysisLimitKind)int.MaxValue, 1, 2, "test", null);

        Assert.That(item.Code, Is.EqualTo("analysis_limit.unknown"));
    }

    [Test]
    public void AnalysisLimitScope_DeduplicatesEventsAndPreservesNestedSourceLocations() {
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

    [Test]
    public void AnalysisLimitScope_IsolatedEventsDoNotPropagateToParent() {
        using var outer = SymbolicAnalysisLimitContext.Push();
        using (var isolated = SymbolicAnalysisLimitContext.PushIsolated()) {
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.IfElseFactMerge,
                1,
                2,
                sourceNode: null,
                provenance: "test.isolated");
            Assert.That(isolated.Snapshot().IsTruncated, Is.True);
        }

        Assert.That(outer.Snapshot().IsTruncated, Is.False);
    }

    [Test]
    public void TruncationCombination_UsesMaximumObservationAndCanonicalOrdering() {
        var combined = SymbolicAnalysisTruncationInfo.Combine(new[]
        {
            new SymbolicAnalysisTruncationInfo(new[]
            {
                new SymbolicAnalysisTruncationEvent(
                    SymbolicAnalysisLimitKind.SwitchFactMerge,
                    2,
                    3,
                    "switch.z",
                    20),
                new SymbolicAnalysisTruncationEvent(
                    SymbolicAnalysisLimitKind.IfElseFactMerge,
                    2,
                    3,
                    "if.a",
                    10)
            }),
            new SymbolicAnalysisTruncationInfo(new[]
            {
                new SymbolicAnalysisTruncationEvent(
                    SymbolicAnalysisLimitKind.SwitchFactMerge,
                    2,
                    7,
                    "switch.z",
                    20),
                new SymbolicAnalysisTruncationEvent(
                    SymbolicAnalysisLimitKind.SwitchFactMerge,
                    2,
                    4,
                    "switch.a",
                    20)
            })
        });

        Assert.That(combined.Events.Select(static item => item.Provenance),
            Is.EqualTo(new[] { "if.a", "switch.a", "switch.z" }));
        Assert.That(combined.Events.Single(static item => item.Provenance == "switch.z").Observed,
            Is.EqualTo(7));
    }

    [Test]
    public void PathConditionMerger_ReportsEveryStateMergeCap() {
        var source = SyntaxFactory.ParseExpression("source");
        IReadOnlyList<SymbolicCondition> first = new[]
        {
            Greater("x", 1), Greater("x", 2), Greater("x", 3),
            Greater("y", 10), Greater("y", 11), Greater("y", 12),
            Greater("z", 20), Greater("z", 21), Greater("z", 22)
        };
        IReadOnlyList<SymbolicCondition> second = new[]
        {
            Greater("x", 4), Greater("x", 5), Greater("x", 6),
            Greater("y", 13), Greater("y", 14), Greater("y", 15),
            Greater("z", 23), Greater("z", 24), Greater("z", 25)
        };

        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with {
                MaxMergedPathConditions = 1,
                MaxMergeableFactsPerTargetPerState = 2,
                MaxFactChoiceCombinationsPerTarget = 1,
                MaxGuardFactsPerTargetPerState = 1
            });
        var merged = SymbolicStateMerger.MergePathConditionsAcrossAll(new[] { first, second });
        var info = scope.Snapshot();

        Assert.That(merged, Has.Length.EqualTo(1));
        Assert.That(
            info.Events.Select(item => item.Kind),
            Is.EquivalentTo(new[]
            {
                SymbolicAnalysisLimitKind.MergedPathConditions,
                SymbolicAnalysisLimitKind.MergeableFactsPerTargetPerState,
                SymbolicAnalysisLimitKind.FactChoiceCombinationsPerTarget,
                SymbolicAnalysisLimitKind.GuardFactsPerTargetPerState
            }));

        SymbolicCondition Greater(string name, int value) =>
            new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThan,
                    new SymbolicVariableTerm(name, SmtValueKind.Int),
                    new SymbolicIntegerConstantTerm(value)),
                source,
                "test.equal"));
    }

    [Test]
    public void PathConditionMerger_GroupsNegatedBooleanAndComparisonTargets() {
        var source = SyntaxFactory.ParseExpression("source");
        var flag = new SymbolicVariableTerm("flag", SmtValueKind.Bool);
        var value = new SymbolicVariableTerm("value", SmtValueKind.Int);
        var other = new SymbolicVariableTerm("other", SmtValueKind.Int);
        IReadOnlyList<SymbolicCondition> first = new SymbolicCondition[]
        {
            Truth(flag),
            new SymbolicNotCondition(Greater(value, 0)),
            Greater(other, 10)
        };
        IReadOnlyList<SymbolicCondition> second = new SymbolicCondition[]
        {
            new SymbolicNotCondition(Truth(flag)),
            new SymbolicNotCondition(Greater(value, 1)),
            Greater(other, 11)
        };

        using var scope = SymbolicAnalysisLimitContext.Push(
            SharpProofAnalysisBudget.Default with { MaxGuardFactsPerTargetPerState = 1 });
        var merged = SymbolicStateMerger.MergePathConditionsAcrossAll(new[] { first, second });

        Assert.That(merged, Has.Length.EqualTo(2));
        Assert.That(merged, Has.All.Matches<SymbolicCondition>(
            static condition => condition is SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or }));

        SymbolicCondition Truth(SymbolicTerm term) =>
            new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicTruthAtom(term), source, "test.truth"));

        SymbolicCondition Greater(SymbolicTerm term, int constant) =>
            new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThan,
                    term,
                    new SymbolicIntegerConstantTerm(constant)),
                source,
                "test.greater"));
    }


}
