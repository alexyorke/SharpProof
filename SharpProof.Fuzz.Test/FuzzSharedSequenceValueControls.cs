using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzSharedSequenceValueControls
{
    [Test]
    public void BothReferencedSequenceParametersRetainTheirLengths()
    {
        long[] values = [4, 7];
        var generated = new FrontendSemanticEdgeCase("bool",
            "long[] first, long[] second", "first.Length == second.Length", [values, values],
            FrontendSubsetDecision.Exact, FrontendAbstention.None);

        var result = FrontendDifferentialOracle.CompareSemanticEdges([generated]).Single();
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement), result.Detail);
        Assert.That(result.ActualDecision, Is.EqualTo(FrontendSubsetDecision.Exact));
        Assert.That(result.ActualAbstention, Is.EqualTo(FrontendAbstention.None));
    }
}
