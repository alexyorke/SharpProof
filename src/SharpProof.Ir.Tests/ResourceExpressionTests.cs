using System.Numerics;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceExpressionTests
{
    private static ResourceExpression Input(int ordinal = 0, ResourceInputKind kind = ResourceInputKind.NonNegativeScalar)
    {
        return ResourceExpression.InputSize(new ResourceInput(ordinal, kind, "n", 1000));
    }

    private static BigInteger Evaluate(ResourceExpression expression, int value)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return expression.Evaluate(_ => value, TestContext.CurrentContext.CancellationToken);
    }

    [Test]
    public void MathematicalArithmeticDoesNotWrapAndProjectsPhysicalMaximums()
    {
        var input = Input();
        var cost = ResourceExpression.Add(ResourceExpression.Multiply(input, input), ResourceExpression.Literal(3));
        Assert.That(Evaluate(cost, 20), Is.EqualTo(new BigInteger(403)));
        Assert.That(cost.Maximum, Is.EqualTo(new BigInteger(1000003)));
        var large = ResourceExpression.Multiply(ResourceExpression.Literal(long.MaxValue), ResourceExpression.Literal(long.MaxValue));
        Assert.That(large.Constant, Is.GreaterThan(new BigInteger(long.MaxValue)));
    }

    [TestCase(0, 0, 0)]
    [TestCase(1, 1, 0)]
    [TestCase(2, 2, 1)]
    [TestCase(3, 2, 2)]
    [TestCase(16, 5, 4)]
    [TestCase(17, 5, 5)]
    public void GeometricOperationsHaveExactIntegerBoundaries(int value, int digits, int ceilingLog)
    {
        Assert.That(Evaluate(ResourceExpression.Geometric(Input(), 2, true, TestContext.CurrentContext.CancellationToken), value), Is.EqualTo(new BigInteger(digits)));
        Assert.That(Evaluate(ResourceExpression.Geometric(Input(), 2, false, TestContext.CurrentContext.CancellationToken), value), Is.EqualTo(new BigInteger(ceilingLog)));
    }

    [Test]
    public void DivisionDifferenceAndAlignmentPreserveNonnegativeSemantics()
    {
        var input = Input();
        Assert.That(Evaluate(ResourceExpression.Difference(input, 30), 20), Is.EqualTo(BigInteger.Zero));
        Assert.That(Evaluate(ResourceExpression.DivideCeiling(input, 3), 20), Is.EqualTo(new BigInteger(7)));
        Assert.That(Evaluate(ResourceExpression.Aligned(input, 8), 20), Is.EqualTo(new BigInteger(24)));
        Assert.That(Evaluate(input, -10), Is.EqualTo(BigInteger.Zero));
    }

    [Test]
    public void DisplayLabelsCannotAliasDifferentInputs()
    {
        var first = Input(0);
        var second = Input(1);
        Assert.That(first.ToDisplayString(), Is.EqualTo(second.ToDisplayString()));
        Assert.That(first.Equals(second), Is.False);
        var maximum = ResourceExpression.Max(first, second);
        var value = maximum.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) :
            input.Ordinal == 0 ? 2 : 7, TestContext.CurrentContext.CancellationToken);
        Assert.That(value, Is.EqualTo(new BigInteger(7)));
    }

    [Test]
    public void KindAndDeclaredDomainArePartOfInputIdentity()
    {
        var scalar = Input();
        var length = Input(kind: ResourceInputKind.ArrayLength);
        Assert.That(scalar.Equals(length), Is.False);
        Action negativeLength = () => Evaluate(length, -1);
        Action aboveMaximum = () => Evaluate(scalar, 1001);
        Assert.Throws<ArgumentOutOfRangeException>(negativeLength);
        Assert.Throws<ArgumentOutOfRangeException>(aboveMaximum);
    }

    [Test]
    public void RenamingAnInputDoesNotChangeItsSemanticIdentity()
    {
        var original = Input();
        var renamed = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "renamed", 1000));
        Assert.That(original.Equals(renamed), Is.True);
        Assert.That(original.GetHashCode(), Is.EqualTo(renamed.GetHashCode()));
        Assert.That(original.ToDisplayString(), Is.Not.EqualTo(renamed.ToDisplayString()));
    }

    [Test]
    public void InvalidExpressionsAndDomainsAreRejectedAtConstruction()
    {
        Action negative = () => ResourceExpression.Literal(-1);
        Action zeroDivisor = () => ResourceExpression.DivideCeiling(Input(), 0);
        Action unitBase = () => ResourceExpression.Geometric(Input(), 1, true, TestContext.CurrentContext.CancellationToken);
        Action invalidKind = () => _ = new ResourceInput(0, (ResourceInputKind)99, "n", 10);
        Action invalidOrdinal = () => _ = new ResourceInput(-1, ResourceInputKind.ArrayLength, "n", 10);
        Action controlName = () => _ = new ResourceInput(0, ResourceInputKind.ArrayLength, "n\n", 10);
        Assert.Throws<ArgumentOutOfRangeException>(negative);
        Assert.Throws<ArgumentOutOfRangeException>(zeroDivisor);
        Assert.Throws<ArgumentOutOfRangeException>(unitBase);
        Assert.Throws<ArgumentOutOfRangeException>(invalidKind);
        Assert.Throws<ArgumentOutOfRangeException>(invalidOrdinal);
        Assert.Throws<ArgumentException>(controlName);
    }

    [Test]
    public void DeepExpressionConstructionStopsAtItsBudget()
    {
        Action construct = () =>
        {
            var value = Input();
            for (int index = 0; index < ResourceExpression.MaximumDepth + 1; index++)
            { value = ResourceExpression.Add(value, ResourceExpression.One); }
        };
        Assert.Throws<ResourceExpressionLimitException>(construct);
    }

    [Test]
    public void OversizedOperationConstantsCannotHideBehindSmallMaximums()
    {
        var huge = BigInteger.One << 65536;
        Action construct = () => ResourceExpression.Difference(Input(), huge);
        Assert.Throws<ResourceExpressionLimitException>(construct);
    }

    [Test]
    public void EvaluationPropagatesCancellation()
    {
        Action evaluate = () => Input().Evaluate(static _ => BigInteger.One, new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(evaluate);
    }

    [Test]
    public void StructuralEqualityAndHashesAgreeAcrossIndependentConstructions()
    {
        var first = ResourceExpression.Add(Input(), ResourceExpression.Literal(3));
        var second = ResourceExpression.Add(Input(), ResourceExpression.Literal(3));
        Assert.That(first.Equals(second), Is.True);
        Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        Assert.That(ResourceExpression.Max(first, second), Is.SameAs(first));
        Assert.That(first.ToDisplayString(), Is.EqualTo("(max(0, n) + 3)"));
    }
}
