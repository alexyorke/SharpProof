using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
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
    public void AnalysisLimitScope_IsolatedEventsDoNotPropagateToParent()
    {
        using var outer = SymbolicAnalysisLimitContext.Push();
        using (var isolated = SymbolicAnalysisLimitContext.PushIsolated())
        {
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
    public void TruncationCombination_UsesMaximumObservationAndCanonicalOrdering()
    {
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
    public void PathConditionMerger_ReportsEveryStateMergeCap()
    {
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
            SymbolicAnalysisLimits.Default.WithOverrides(
                maxMergedPathConditions: 1,
                maxMergeableFactsPerTargetPerState: 2,
                maxFactChoiceCombinationsPerTarget: 1,
                maxGuardFactsPerTargetPerState: 1));
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
    public void PathConditionMerger_GroupsNegatedBooleanAndComparisonTargets()
    {
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
            SymbolicAnalysisLimits.Default.WithOverrides(maxGuardFactsPerTargetPerState: 1));
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

        var result = new SymbolicQueryService().Query(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "Sample.cs"),
            SymbolicQueryTarget.AllLines(),
            options));

        Assert.That(options.AnalysisLimits, Is.SameAs(limits));
        Assert.That(result.AnalysisTruncation.IsTruncated, Is.True);
        Assert.That(
            result.AnalysisTruncation.Events.Select(static item => item.Code),
            Does.Contain("analysis_limit.foreach_element_facts"));
        var compact = result.ToCompactResult();
        Assert.That(compact.GetProperty("analysisTruncation").GetProperty("isTruncated").GetBoolean(), Is.True);
        Assert.That(compact.GetProperty("analysisSummary").GetProperty("analysisTruncated").GetBoolean(), Is.True);
        Assert.That(compact.GetProperty("analysisSummary").GetProperty("hasUnresolvedAnalysis").GetBoolean(), Is.True);
        Assert.That(result.ToCompactResult()
            .GetProperty("analysisTruncation").GetProperty("isTruncated").GetBoolean(), Is.True);
    }

    [Test]
    public void RuntimeHazardQuery_ExposesCandidateTruncationInFullAndCompactResults()
    {
        const string source = """
                              #nullable enable
                              public sealed class Sample
                              {
                                  public int Visit()
                                  {
                                      foreach (var value in new string?[] { null, "ok" })
                                      {
                                          return value.Length;
                                      }

                                      return 0;
                                  }
                              }
                              """;
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var limits = SymbolicAnalysisLimits.Default.WithOverrides(maxFiniteForeachElementFacts: 1);
        var options = new SymbolicQueryOptions(smtAnalysis: smtAnalysis).WithAnalysisLimits(limits);

        var result = new SymbolicQueryService().QueryRuntimeHazards(new SymbolicQueryContext(
            SymbolicSourceInput.FromText(source, "Sample.cs"),
            SymbolicQueryTarget.AllLines(),
            options),
            new SymbolicRuntimeHazardQueryOptions(
                includeUnprovenCandidates: true,
                kinds: new[] { SymbolicRuntimeHazardKind.NullDereference }));

        Assert.That(result.Hazards, Is.Not.Empty);
        Assert.That(result.AnalysisTruncation.IsTruncated, Is.True);
        Assert.That(result.Hazards.Any(static hazard => hazard.AnalysisTruncation.IsTruncated), Is.True);
        var compact = result.ToCompactResult();
        Assert.That(compact.GetProperty("analysisTruncation").GetProperty("isTruncated").GetBoolean(), Is.True);
        Assert.That(
            compact.GetProperty("hazards").EnumerateArray().Any(static hazard =>
                hazard.GetProperty("analysisTruncation").GetProperty("isTruncated").GetBoolean()),
            Is.True);
    }

    [Test]
    public void ProgramPointQueries_ReportEveryStructuralTruncationFamily()
    {
        var defaults = SymbolicAnalysisLimits.Default;
        var cases = new (string Code, string Source, SymbolicAnalysisLimits Limits)[]
        {
            (
                "analysis_limit.if_else_fact_merge",
                """
                public sealed class Sample
                {
                    public int Visit(bool selectFirst)
                    {
                        int first;
                        int second;
                        if (selectFirst)
                        {
                            first = 1;
                            second = 2;
                        }
                        else
                        {
                            first = 3;
                            second = 4;
                        }

                        return first + second;
                    }
                }
                """,
                defaults.WithOverrides(maxMergedIfElseFacts: 1)),
            (
                "analysis_limit.switch_fact_merge",
                """
                public sealed class Sample
                {
                    public int Visit(int choice)
                    {
                        int first;
                        int second;
                        switch (choice)
                        {
                            case 0:
                                first = 1;
                                second = 2;
                                break;
                            default:
                                first = 3;
                                second = 4;
                                break;
                        }

                        return first + second;
                    }
                }
                """,
                defaults.WithOverrides(maxMergedSwitchFacts: 1)),
            (
                "analysis_limit.try_fact_merge",
                """
                using System;

                public sealed class Sample
                {
                    public int Visit()
                    {
                        int first;
                        int second;
                        try
                        {
                            first = 1;
                            second = 2;
                        }
                        catch (Exception)
                        {
                            first = 1;
                            second = 2;
                        }

                        return first + second;
                    }
                }
                """,
                defaults.WithOverrides(maxMergedTryFacts: 1)),
            (
                "analysis_limit.try_completion_branches",
                """
                using System;

                public sealed class Sample
                {
                    public int Visit()
                    {
                        try
                        {
                            _ = 1;
                        }
                        catch (InvalidOperationException)
                        {
                            _ = 2;
                        }

                        return 0;
                    }
                }
                """,
                defaults.WithOverrides(maxTryCompletionBranches: 1)),
            (
                "analysis_limit.scoped_block_completion_statements",
                """
                public sealed class Sample
                {
                    public int Visit()
                    {
                        int value = 0;
                        {
                            value = 1;
                            value = 2;
                        }

                        return value;
                    }
                }
                """,
                defaults.WithOverrides(maxScopedBlockCompletionStatements: 1)),
            (
                "analysis_limit.foreach_element_facts",
                """
                public sealed class Sample
                {
                    public int Visit()
                    {
                        foreach (var value in new[] { 1, 2 })
                        {
                            return value;
                        }

                        return 0;
                    }
                }
                """,
                defaults.WithOverrides(maxFiniteForeachElementFacts: 1)),
            (
                "analysis_limit.structural_null_state_depth",
                """
                #nullable enable
                public sealed class Sample
                {
                    public object? Visit(bool enabled, object? first, object? second, object? third)
                    {
                        object? value = enabled ? first ?? second : third;
                        return value;
                    }
                }
                """,
                defaults.WithOverrides(maxStructuralNullStateDepth: 1))
        };

        Assert.Multiple(() =>
        {
            foreach (var item in cases)
            {
                var options = new SymbolicQueryOptions().WithAnalysisLimits(item.Limits);
                var result = new SymbolicQueryService().Query(new SymbolicQueryContext(
                    SymbolicSourceInput.FromText(item.Source, "Sample.cs"),
                    SymbolicQueryTarget.AllLines(),
                    options));

                Assert.That(
                    result.AnalysisTruncation.Events.Select(static item => item.Code),
                    Does.Contain(item.Code),
                    item.Code);
            }
        });
    }
}
