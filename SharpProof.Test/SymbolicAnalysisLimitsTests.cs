using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

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

    [Test]
    public void PathConditionMerger_ReportsEveryStateMergeCap()
    {
        static SmtFormula Equal(string name, int value)
        {
            return new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtVariable(name, SmtValueKind.Int),
                new SmtIntegerConstant(value));
        }

        var first = ImmutableArray.Create(
            Equal("x", 1),
            Equal("x", 2),
            Equal("x", 3),
            Equal("y", 10),
            Equal("y", 11),
            Equal("y", 12));
        var second = ImmutableArray.Create(
            Equal("x", 4),
            Equal("x", 5),
            Equal("x", 6),
            Equal("y", 13),
            Equal("y", 14),
            Equal("y", 15));

        using var scope = SymbolicAnalysisLimitContext.Push();
        var merged = SmtPathConditionMerger.MergeAcrossAll(
            new[] { first, second },
            new SmtPathConditionMergeOptions(
                maxMergedPathConditions: 1,
                maxFactsPerTargetPerState: 2,
                maxFactChoiceCombinationsPerTarget: 1,
                maxGuardFactsPerTargetPerState: 1));
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
    }

    [Test]
    public void QueryOptions_ExposeProgramPointTruncationInFullAndCompactResults()
    {
        const string source = """
                              public sealed class Sample
                              {
                                  public void Visit()
                                  {
                                      foreach (var value in new[] { 1, 2, 3 })
                                      {
                                          _ = value;
                                      }

                                      _ = 0;
                                  }
                              }
                              """;
        var limits = SymbolicAnalysisLimits.Default.WithOverrides(maxFiniteForeachElementFacts: 1);
        var options = new SymbolicQueryOptions().WithAnalysisLimits(limits);

        var result = new SymbolicQueryService().Query(new SymbolicQueryRequest(
            SymbolicSourceInput.FromText(source, "Sample.cs"),
            SymbolicQueryTarget.AllLines(),
            options));

        Assert.That(options.AnalysisLimits, Is.SameAs(limits));
        Assert.That(result.AnalysisTruncation.IsTruncated, Is.True);
        Assert.That(
            result.AnalysisTruncation.Events.Select(static item => item.Code),
            Does.Contain("analysis_limit.foreach_element_facts"));
        Assert.That(result.ToCompactResult().AnalysisTruncation.IsTruncated, Is.True);
    }
}
