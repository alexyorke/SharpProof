using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicInvariantTargetFilterTests
{
    [Test]
    public void TypedWrappers_FilterThroughNormalizedGenericCore()
    {
        var summaries = new[]
        {
            CreateSummary("field"),
            CreateSummary("other")
        };
        var results = new[]
        {
            new SymbolicConditionProofResult("field == 1", SymbolicTruthValue.ProvenTrue, "test", target: "field"),
            new SymbolicConditionProofResult("other == 1", SymbolicTruthValue.ProvenTrue, "test", target: "other")
        };
        var conditions = new[]
        {
            SymbolicInvariantCondition.FromConservativeUnknown(0, "unknown(field)"),
            SymbolicInvariantCondition.FromConservativeUnknown(1, "unknown(other)")
        };
        var filters = new[] { "  field  " };

        Assert.Multiple(() =>
        {
            Assert.That(
                SymbolicInvariantTargetFilter.ApplyToProofSummaries(summaries, filters).Select(static item => item.Target),
                Is.EqualTo(new[] { "field" }));
            Assert.That(
                SymbolicInvariantTargetFilter.ApplyToProofResults(results, filters).Select(static item => item.Target),
                Is.EqualTo(new[] { "field" }));
            Assert.That(
                SymbolicInvariantTargetFilter.ApplyToConditions(conditions, filters).Select(static item => item.Target),
                Is.EqualTo(new[] { "field" }));
        });
    }

    [Test]
    public void TypedWrappers_EmptyFilter_PreserveInputCollections()
    {
        var summaries = new[] { CreateSummary("field") };
        var results = new[]
        {
            new SymbolicConditionProofResult("field == 1", SymbolicTruthValue.ProvenTrue, "test", target: "field")
        };
        var conditions = new[] { SymbolicInvariantCondition.FromConservativeUnknown(0, "unknown(field)") };

        Assert.Multiple(() =>
        {
            Assert.That(SymbolicInvariantTargetFilter.ApplyToProofSummaries(summaries, Array.Empty<string>()),
                Is.SameAs(summaries));
            Assert.That(SymbolicInvariantTargetFilter.ApplyToProofResults(results, Array.Empty<string>()),
                Is.SameAs(results));
            Assert.That(SymbolicInvariantTargetFilter.ApplyToConditions(conditions, Array.Empty<string>()),
                Is.SameAs(conditions));
        });
    }

    [Test]
    public void ApplyToTargets_NormalizesRawFilterTargets()
    {
        var targets = new[] { "field", "other" };

        var filtered = SymbolicInvariantTargetFilter.ApplyToTargets(
            targets,
            new[] { "  field  " },
            static target => target);

        Assert.That(filtered, Is.EqualTo(new[] { "field" }));
    }

    [Test]
    public void GetUnmatchedTargetFilters_NormalizesRawFilterTargets()
    {
        var unmatched = SymbolicInvariantTargetFilter.GetUnmatchedTargetFilters(
            new[] { "  field  ", " other " },
            new[] { "field" });

        Assert.That(unmatched, Is.EqualTo(new[] { "other" }));
    }

    private static SymbolicConditionProofSummary CreateSummary(string target)
    {
        return new SymbolicConditionProofSummary(
            $"{target} == 1",
            unknownCount: 0,
            provenTrueCount: 1,
            provenFalseCount: 0,
            unreachableCount: 0,
            target: target);
    }
}
