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
}
