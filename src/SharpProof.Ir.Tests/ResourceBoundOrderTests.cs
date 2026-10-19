using System.Numerics;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceBoundOrderTests
{
    private static ResourceExpression Input(int ordinal = 0)
    {
        return ResourceExpression.InputSize(new ResourceInput(ordinal, ResourceInputKind.NonNegativeScalar, "n", 100));
    }

    private static ResourceExpression Linear(ResourceExpression input, int coefficient, int constant)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ResourceExpression.Add(ResourceExpression.Multiply(input, ResourceExpression.Literal(coefficient)), ResourceExpression.Literal(constant));
    }

    [Test]
    public void LinearProofsRespectEveryIndependentlyEvaluatedInput()
    {
        var input = Input();
        int proved = 0, unknown = 0;
        for (int scenario = 0; scenario < 256; scenario++)
        {
            int leftCoefficient = scenario % 4, rightCoefficient = (scenario / 4) % 4;
            int leftConstant = (scenario / 16) % 4, rightConstant = (scenario / 64) % 4;
            var left = Linear(input, leftCoefficient, leftConstant);
            var right = Linear(input, rightCoefficient, rightConstant);
            if (!ResourceBoundOrder.Proves(left, right, TestContext.CurrentContext.CancellationToken))
            { unknown++; continue; }
            proved++;
            for (int value = -3; value <= 100; value++)
            {
                var size = Math.Max(0, value);
                var actualLeft = leftCoefficient * size + leftConstant;
                var actualRight = rightCoefficient * size + rightConstant;
                Assert.That(actualLeft, Is.LessThanOrEqualTo(actualRight));
            }
        }
        Assert.That(proved, Is.GreaterThan(0));
        Assert.That(unknown, Is.GreaterThan(0));
    }

    [Test]
    public void RankAndAlignmentRulesMatchIndependentIntegerArithmetic()
    {
        var input = Input();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var expressions = new[] { ResourceExpression.DivideCeiling(input, 3), ResourceExpression.Difference(input, 4),
            ResourceExpression.Geometric(input, 2, true, cancellation), ResourceExpression.Geometric(input, 2, false, cancellation) };
        foreach (var expression in expressions)
        {
            Assert.That(ResourceBoundOrder.Proves(expression, input, cancellation), Is.True);
            for (int value = 0; value <= 100; value++)
            { Assert.That(expression.Evaluate(_ => value, cancellation), Is.LessThanOrEqualTo(new BigInteger(value))); }
        }
        var aligned = ResourceExpression.Aligned(input, 8);
        Assert.That(ResourceBoundOrder.Proves(aligned, Linear(input, 1, 7), cancellation), Is.True);
        Assert.That(ResourceBoundOrder.Proves(aligned, input, cancellation), Is.False);
        Assert.That(aligned.Evaluate(static _ => 1, cancellation), Is.EqualTo(new BigInteger(8)));
    }

    [Test]
    public void MaximumsAndPhysicalDomainsAreConservative()
    {
        var input = Input();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var maximum = ResourceExpression.Max(Linear(input, 2, 0), Linear(input, 3, 1));
        Assert.That(ResourceBoundOrder.Proves(maximum, Linear(input, 4, 1), cancellation), Is.True);
        Assert.That(ResourceBoundOrder.Proves(ResourceExpression.Multiply(input, input), ResourceExpression.Literal(10000), cancellation), Is.True);
        Assert.That(ResourceBoundOrder.Proves(ResourceExpression.Multiply(input, input), input, cancellation), Is.False);
    }

    [Test]
    public void EqualDisplayLabelsDoNotProveDifferentInputsComparable()
    {
        var first = Input(0);
        var second = Input(1);
        Assert.That(first.ToDisplayString(), Is.EqualTo(second.ToDisplayString()));
        Assert.That(ResourceBoundOrder.Proves(first, second, TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [Test]
    public void CancelledComparisonDoesNotCreateAProof()
    {
        Action compare = () => ResourceBoundOrder.Proves(Input(), Input(), new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(compare);
    }
    [Test]
    public void RightHandAlternativesAndRightHandConstantFactorsAreSound()
    {
        var first = Input();
        var second = Input(1);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(ResourceBoundOrder.Proves(first, ResourceExpression.Max(second, first), cancellation), Is.True);
        Assert.That(ResourceBoundOrder.Proves(ResourceExpression.Multiply(first, ResourceExpression.Literal(2)), Linear(first, 3, 0), cancellation), Is.True);
        var zeroDomain = ResourceExpression.InputSize(new ResourceInput(2, ResourceInputKind.NonNegativeScalar, "empty", 0));
        Assert.That(ResourceBoundOrder.Proves(ResourceExpression.One, zeroDomain, cancellation), Is.False);
    }

    [Test]
    public void ArithmeticCollectionHasASharedExpandedNodeBudget()
    {
        var expression = Input();
        for (int level = 0; level < 11; level++)
        { expression = ResourceExpression.Add(expression, expression); }
        Assert.That(expression.NodeCount, Is.LessThanOrEqualTo(ResourceExpression.MaximumNodes));
        Action compare = () => ResourceBoundOrder.Proves(expression, Input(1), TestContext.CurrentContext.CancellationToken);
        Assert.Throws<ResourceExpressionLimitException>(compare);
    }

    [Test]
    public void UndefinedMetricsCannotDeclareAnArithmeticContract()
    {
        Action construct = () => _ = new ResourceBoundConstraint((ResourceCostMetric)999, ResourceExpression.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(construct);
    }
}
