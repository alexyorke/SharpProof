using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicInvariantTargetFilterTests
{
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