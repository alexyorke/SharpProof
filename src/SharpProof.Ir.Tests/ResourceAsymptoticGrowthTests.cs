using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceAsymptoticGrowthTests
{
    [TestCase(0, "0")]
    [TestCase(1, "1")]
    [TestCase(2, "n")]
    [TestCase(3, "n^2")]
    [TestCase(4, "n * log(n)")]
    [TestCase(5, "log(n)")]
    [TestCase(6, "log(n)^2")]
    [TestCase(7, "n")]
    [TestCase(8, "n^2")]
    public void ClosedPolynomialLogarithmicExpressionsHaveCheckedGrowth(int kind, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var token = TestContext.CurrentContext.CancellationToken;
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "size", 1000));
        var log = ResourceExpression.Geometric(input, 2, false, token);
        var expression = kind switch
        {
            0 => ResourceExpression.Zero,
            1 => ResourceExpression.Literal(42),
            2 => input,
            3 => ResourceExpression.Add(ResourceExpression.Multiply(input, input), input),
            4 => ResourceExpression.Multiply(input, log),
            5 => ResourceExpression.Geometric(ResourceExpression.Multiply(input, input), 3, true, token),
            6 => ResourceExpression.Multiply(log, log),
            7 => ResourceExpression.Aligned(ResourceExpression.DivideCeiling(ResourceExpression.Difference(input, 20), 3), 8),
            8 => ResourceExpression.Multiply(ResourceExpression.Max(input, log), ResourceExpression.Max(input, log)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var growth = ResourceAsymptoticGrowth.Analyze(expression, token);
        Assert.That(growth.UnknownReason, Is.Null);
        Assert.That(growth.Order, Is.EqualTo(expected));
        Assert.That(growth.Input?.Ordinal, Is.EqualTo(kind < 2 ? null : (int?)0));
    }

    [TestCase(false, "AsymptoticMultipleInputs")]
    [TestCase(true, "AsymptoticIteratedLogarithmUnsupported")]
    public void UnsupportedGrowthHasNoPartialOrderOrInput(bool iterated, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var token = TestContext.CurrentContext.CancellationToken;
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "a", 1000));
        var expression = iterated ? ResourceExpression.Geometric(ResourceExpression.Geometric(input, 2, false, token), 2, true, token) :
            ResourceExpression.Add(input, ResourceExpression.InputSize(new ResourceInput(1, ResourceInputKind.ArrayLength, "b", 1000)));
        var growth = ResourceAsymptoticGrowth.Analyze(expression, token);
        Assert.That(growth.Order, Is.Null);
        Assert.That(growth.Input, Is.Null);
        Assert.That(growth.UnknownReason, Is.EqualTo(reason));
    }

    [Test]
    public void InputNamesDoNotEstablishIdentityAndPhysicalCapsAreNotAsymptoticConstants()
    {
        var left = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "left", 32));
        var renamed = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "renamed", 32));
        var growth = ResourceAsymptoticGrowth.Analyze(ResourceExpression.Add(left, renamed), TestContext.CurrentContext.CancellationToken);
        Assert.That(growth.Order, Is.EqualTo("n"));
        Assert.That(growth.Input!.Maximum, Is.EqualTo(new BigInteger(32)));
        var differentKind = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "left", 32));
        Assert.That(ResourceAsymptoticGrowth.Analyze(ResourceExpression.Add(left, differentKind),
            TestContext.CurrentContext.CancellationToken).UnknownReason, Is.EqualTo("AsymptoticMultipleInputs"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void MalformedDetachedExpressionsCannotSupplyGrowthOrders(int mutation)
    {
        var kind = mutation switch
        {
            0 => (ResourceExpressionKind)999,
            1 or 2 => ResourceExpressionKind.Add,
            3 => ResourceExpressionKind.Input,
            4 => ResourceExpressionKind.CeilingDivide,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var operands = mutation == 1 || mutation == 4 ? ImmutableArray.Create(ResourceExpression.One) :
            mutation == 2 ? ImmutableArray.Create(ResourceExpression.One, ResourceExpression.One) : ImmutableArray<ResourceExpression>.Empty;
        var constructor = typeof(ResourceExpression).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var expression = (ResourceExpression)constructor.Invoke([kind, mutation == 2 ? BigInteger.One : BigInteger.Zero, null, operands, null]);
        var growth = ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken);
        Assert.That(growth.Order, Is.Null);
        Assert.That(growth.UnknownReason, Is.EqualTo("MalformedAsymptoticExpression"));
    }

    [TestCase((int)ResourceExpressionKind.PositiveDifference, 2, 1, "0")]
    [TestCase((int)ResourceExpressionKind.PositiveDifference, 2, 3, "1")]
    [TestCase((int)ResourceExpressionKind.CeilingDivide, 2, 0, "0")]
    [TestCase((int)ResourceExpressionKind.CeilingDivide, 2, 3, "1")]
    [TestCase((int)ResourceExpressionKind.Align, 8, 0, "0")]
    [TestCase((int)ResourceExpressionKind.Align, 8, 3, "1")]
    [TestCase((int)ResourceExpressionKind.Digits, 2, 0, "0")]
    [TestCase((int)ResourceExpressionKind.Digits, 2, 3, "1")]
    [TestCase((int)ResourceExpressionKind.CeilingLog, 2, 1, "0")]
    [TestCase((int)ResourceExpressionKind.CeilingLog, 2, 3, "1")]
    public void DetachedUnaryConstantsAreEvaluatedBeforeClassifyingGrowth(int kind, int number, int operand, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var constructor = typeof(ResourceExpression).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var expression = (ResourceExpression)constructor.Invoke([(ResourceExpressionKind)kind, new BigInteger(number), null,
            ImmutableArray.Create(ResourceExpression.Literal(operand)), null]);
        var growth = ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken);
        Assert.That(growth.UnknownReason, Is.Null);
        Assert.That(growth.Order, Is.EqualTo(expected));
    }
    [Test]
    public void NormalizedQuadraticEnvelopeAgreesWithIndependentExecutionCounts()
    {
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", 100));
        var expression = ResourceExpression.Add(ResourceExpression.Multiply(input, input), input);
        Assert.That(ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken).Order, Is.EqualTo("n^2"));
        foreach (var size in new[] { 1, 2, 5, 16, 32 })
        {
            var observed = 0;
            for (var outer = 0; outer < size; outer++)
            {
                observed++;
                for (var inner = 0; inner < size; inner++)
                { observed++; }
            }
            Assert.That(observed, Is.InRange(size * size, 2 * size * size));
            Assert.That(expression.Evaluate(_ => size, TestContext.CurrentContext.CancellationToken), Is.EqualTo(new BigInteger(observed)));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TraversalBudgetsIgnoreForgedCachedMetadataAndStopCycles(bool cycle)
    {
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "n", 1000));
        var expression = ResourceExpression.Add(input, ResourceExpression.One);
        var field = typeof(ResourceExpression).GetField("<Operands>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (cycle)
        { field.SetValue(expression, ImmutableArray.Create(expression, ResourceExpression.One)); }
        else
        {
            for (var depth = 0; depth < 13; depth++)
            {
                var parent = ResourceExpression.Add(input, ResourceExpression.One);
                field.SetValue(parent, ImmutableArray.Create(expression, expression));
                expression = parent;
            }
        }
        var growth = ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken);
        Assert.That(growth.Order, Is.Null);
        Assert.That(growth.Input, Is.Null);
        Assert.That(growth.UnknownReason, Is.EqualTo("AsymptoticExpressionBudgetExceeded"));
    }

    [Test]
    public void DetachedConstantArithmeticCannotBypassTheNumericBudget()
    {
        var literal = ResourceExpression.Literal(BigInteger.One << 40000);
        var constructor = typeof(ResourceExpression).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var expression = (ResourceExpression)constructor.Invoke([ResourceExpressionKind.Multiply, BigInteger.Zero, null,
            ImmutableArray.Create(literal, literal), null]);
        var growth = ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken);
        Assert.That(growth.Order, Is.Null);
        Assert.That(growth.UnknownReason, Is.EqualTo("AsymptoticExpressionBudgetExceeded"));
    }

    [TestCase((int)ResourceExpressionKind.Add, 1, 2, 3, "0")]
    [TestCase((int)ResourceExpressionKind.Maximum, 1, 3, 2, "1")]
    [TestCase((int)ResourceExpressionKind.Multiply, 1, 2, 3, "0")]
    public void DetachedBinaryConstantsRetainTheirValueForSubsequentSubtraction(int kind, int left, int right, int lower, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var constructor = typeof(ResourceExpression).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var binary = (ResourceExpression)constructor.Invoke([(ResourceExpressionKind)kind, BigInteger.Zero, null,
            ImmutableArray.Create(ResourceExpression.Literal(left), ResourceExpression.Literal(right)), null]);
        var expression = (ResourceExpression)constructor.Invoke([ResourceExpressionKind.PositiveDifference, new BigInteger(lower), null,
            ImmutableArray.Create(binary), null]);
        var growth = ResourceAsymptoticGrowth.Analyze(expression, TestContext.CurrentContext.CancellationToken);
        Assert.That(growth.UnknownReason, Is.Null);
        Assert.That(growth.Order, Is.EqualTo(expected));
    }
    [Test]
    public void CancellationPropagatesRatherThanPublishingAnUnknownOrder()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => ResourceAsymptoticGrowth.Analyze(ResourceExpression.One, cancellation.Token)));
    }
}
