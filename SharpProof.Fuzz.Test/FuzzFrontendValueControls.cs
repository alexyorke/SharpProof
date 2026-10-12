using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzFrontendValueControls
{
    [TestCase(true)]
    [TestCase(false)]
    public void SemanticEdgeRejectsAnArgumentOutsideItsDeclaredBooleanDomain(bool nullArgument)
    {
        var generated = new FrontendSemanticEdgeCase("bool", "bool value", "value",
            [nullArgument ? null : 5L], FrontendSubsetDecision.Exact, FrontendAbstention.None);
        Action compare = () => FrontendDifferentialOracle.CompareSemanticEdges([generated]);

        var exception = Assert.Throws<InvalidOperationException>(compare);
        Assert.That(exception!.Message, Is.EqualTo(nullArgument
            ? "A non-nullable semantic-edge variable received null."
            : "A semantic-edge value is outside the executable IR subset."));
    }

    [Test]
    public void SameSequenceArgumentRetainsIdentityAcrossBothReferencedParameters()
    {
        long[] shared = [4, 7];
        var generated = new FrontendSemanticEdgeCase("bool",
            "long[] first, long[] second", "first == second", [shared, shared],
            FrontendSubsetDecision.Exact, FrontendAbstention.None);

        var result = FrontendDifferentialOracle.CompareSemanticEdges([generated]).Single();
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Agreement), result.Detail);
        Assert.That(result.ActualDecision, Is.EqualTo(FrontendSubsetDecision.Exact));
        Assert.That(result.ActualAbstention, Is.EqualTo(FrontendAbstention.None));
    }
}
