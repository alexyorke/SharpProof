using System.Globalization;
using System.IO;
using System.Numerics;

namespace SharpProof.Ir;

// Growth of the unbounded mathematical extension of one closed bound formula.
// This class does not establish a lower bound on the source program's cost.
internal sealed class ResourceAsymptoticGrowth
{
    internal const string ModelId = "single-input-unbounded-expression-extension-v1";
    internal string? Order { get; }
    internal ResourceInput? Input { get; }
    internal string? UnknownReason { get; }

    private ResourceAsymptoticGrowth(string? order, ResourceInput? input, string? unknownReason)
    { Order = order; Input = input; UnknownReason = unknownReason; }

    internal static ResourceAsymptoticGrowth Analyze(ResourceExpression expression, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        cancellation.ThrowIfCancellationRequested();
        ResourceInput? input = null;
        var remaining = ResourceExpression.MaximumNodes;
        try
        {
            var growth = Visit(expression, 1);
            return new(Display(growth), input, null);
        }
        catch (InvalidDataException exception)
        { return new(null, null, exception.Message); }
        catch (ResourceExpressionLimitException)
        { return new(null, null, "AsymptoticExpressionBudgetExceeded"); }

        Growth Visit(ResourceExpression value, int depth)
        {
            value = ArgumentNullGuard.NotNull(value, nameof(value));
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0 || depth > ResourceExpression.MaximumDepth)
            { throw new InvalidDataException("AsymptoticExpressionBudgetExceeded"); }
            var arity = value.Kind switch
            {
                ResourceExpressionKind.Constant or ResourceExpressionKind.Input => 0,
                ResourceExpressionKind.Add or ResourceExpressionKind.Multiply or ResourceExpressionKind.Maximum => 2,
                ResourceExpressionKind.PositiveDifference or ResourceExpressionKind.CeilingDivide or
                    ResourceExpressionKind.Digits or ResourceExpressionKind.CeilingLog or ResourceExpressionKind.Align => 1,
                _ => -1
            };
            if (value.Operands.IsDefault || value.Operands.Length != arity ||
                value.Kind != ResourceExpressionKind.Input && value.Input is not null ||
                value.Kind is ResourceExpressionKind.Constant or ResourceExpressionKind.PositiveDifference && value.Number < 0 ||
                value.Kind is ResourceExpressionKind.CeilingDivide or ResourceExpressionKind.Align && value.Number <= 0 ||
                value.Kind is ResourceExpressionKind.Digits or ResourceExpressionKind.CeilingLog && value.Number <= 1 ||
                value.Kind is ResourceExpressionKind.Input or ResourceExpressionKind.Add or ResourceExpressionKind.Multiply or
                    ResourceExpressionKind.Maximum && value.Number != 0)
            { throw new InvalidDataException("MalformedAsymptoticExpression"); }
            if (value.Kind == ResourceExpressionKind.Constant)
            { return new(0, 0, value.Number == 0, value.Number); }
            if (value.Kind == ResourceExpressionKind.Input)
            {
                if (value.Input is not { } candidate)
                { throw new InvalidDataException("MalformedAsymptoticExpression"); }
                if (input is not null && !input.Equals(candidate))
                { throw new InvalidDataException("AsymptoticMultipleInputs"); }
                input = candidate;
                return new(1, 0, false);
            }
            if (value.Operands.Any(static operand => operand is null))
            { throw new InvalidDataException("MalformedAsymptoticExpression"); }
            var left = Visit(value.Operands[0], depth + 1);
            if (arity == 1)
            {
                if (left.Constant is { } number)
                {
                    var operand = ResourceExpression.Literal(number);
                    var constant = value.Kind switch
                    {
                        ResourceExpressionKind.PositiveDifference => ResourceExpression.Difference(operand, value.Number),
                        ResourceExpressionKind.CeilingDivide => ResourceExpression.DivideCeiling(operand, value.Number),
                        ResourceExpressionKind.Align => ResourceExpression.Aligned(operand, value.Number),
                        _ => ResourceExpression.Geometric(operand, value.Number, value.Kind == ResourceExpressionKind.Digits, cancellation)
                    };
                    return new(0, 0, constant.Number == 0, constant.Number);
                }
                if (value.Kind is ResourceExpressionKind.Digits or ResourceExpressionKind.CeilingLog)
                {
                    if (left.Polynomial > 0)
                    { return new(0, 1, false); }
                    // Iterated logarithms need a richer order domain. Do not
                    // report log(n) as a tight description of log(log(n)).
                    throw new InvalidDataException("AsymptoticIteratedLogarithmUnsupported");
                }
                return left;
            }
            var right = Visit(value.Operands[1], depth + 1);
            if (left.Constant is { } first && right.Constant is { } second)
            {
                var constant = ResourceExpression.Literal(value.Kind switch
                {
                    ResourceExpressionKind.Add => first + second,
                    ResourceExpressionKind.Multiply => first * second,
                    _ => BigInteger.Max(first, second)
                });
                return new(0, 0, constant.Number == 0, constant.Number);
            }
            if (value.Kind == ResourceExpressionKind.Multiply)
            {
                if (left.Zero || right.Zero)
                { return new(0, 0, true); }
                return new(left.Polynomial + right.Polynomial, left.Logarithmic + right.Logarithmic, false);
            }
            if (left.Zero)
            { return right; }
            if (right.Zero)
            { return left; }
            return left.Polynomial > right.Polynomial ||
                left.Polynomial == right.Polynomial && left.Logarithmic >= right.Logarithmic ? left : right;
        }
    }

    private static string Display(Growth growth)
    {
        if (growth.Zero)
        { return "0"; }
        var polynomial = growth.Polynomial == 0 ? "" : growth.Polynomial == 1 ? "n" :
            "n^" + growth.Polynomial.ToString(CultureInfo.InvariantCulture);
        var logarithmic = growth.Logarithmic == 0 ? "" : growth.Logarithmic == 1 ? "log(n)" :
            "log(n)^" + growth.Logarithmic.ToString(CultureInfo.InvariantCulture);
        return polynomial.Length == 0 ? logarithmic.Length == 0 ? "1" : logarithmic :
            logarithmic.Length == 0 ? polynomial : polynomial + " * " + logarithmic;
    }

    private readonly struct Growth
    {
        internal int Polynomial { get; }
        internal int Logarithmic { get; }
        internal bool Zero { get; }
        internal BigInteger? Constant { get; }
        internal Growth(int polynomial, int logarithmic, bool zero, BigInteger? constant = null)
        { Polynomial = polynomial; Logarithmic = logarithmic; Zero = zero; Constant = zero ? BigInteger.Zero : constant; }
    }
}
