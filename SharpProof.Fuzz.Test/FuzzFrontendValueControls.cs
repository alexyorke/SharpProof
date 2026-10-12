using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzFrontendValueControls
{
    [Test]
    public void BoxedUnsignedIntegerComparisonRequiresRepresentableEqualValue()
    {
        object zero = 0UL;
        object largestRepresentable = (ulong)long.MaxValue;
        object differentValue = 1UL;
        object largestUnsigned = ulong.MaxValue;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                FrontendDifferentialOracle.IntegralValueEquals(zero, 0L),
                Is.True);
            Assert.That(
                FrontendDifferentialOracle.IntegralValueEquals(
                    largestRepresentable, long.MaxValue),
                Is.True);
            Assert.That(
                FrontendDifferentialOracle.IntegralValueEquals(differentValue, 0L),
                Is.False);
            Assert.That(
                FrontendDifferentialOracle.IntegralValueEquals(largestUnsigned, -1L),
                Is.False);
        }
    }

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
