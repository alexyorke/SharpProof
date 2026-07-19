using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicInvariantTargetFilterTests
{
    [Test]
    public void TypedWrappers_FilterThroughNormalizedGenericCore()
    {
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
                SymbolicInvariantTargetFilter.ApplyToTargets(results, filters, static item => item.Target)
                    .Select(static item => item.Target),
                Is.EqualTo(new[] { "field" }));
            Assert.That(
                SymbolicInvariantTargetFilter.ApplyToTargets(conditions, filters, static item => item.Target)
                    .Select(static item => item.Target),
                Is.EqualTo(new[] { "field" }));
        });
    }

    [Test]
    public void TypedWrappers_EmptyFilter_PreserveInputCollections()
    {
        var results = new[]
        {
            new SymbolicConditionProofResult("field == 1", SymbolicTruthValue.ProvenTrue, "test", target: "field")
        };
        var conditions = new[] { SymbolicInvariantCondition.FromConservativeUnknown(0, "unknown(field)") };

        Assert.Multiple(() =>
        {
            Assert.That(SymbolicInvariantTargetFilter.ApplyToTargets(
                    results, Array.Empty<string>(), static item => item.Target),
                Is.SameAs(results));
            Assert.That(SymbolicInvariantTargetFilter.ApplyToTargets(
                    conditions, Array.Empty<string>(), static item => item.Target),
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
}
