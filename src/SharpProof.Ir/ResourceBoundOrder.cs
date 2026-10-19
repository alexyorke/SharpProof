using System.Numerics;

namespace SharpProof.Ir;

// Sufficient nonnegative arithmetic rules. Failure establishes no violation.
internal static class ResourceBoundOrder
{
    internal static bool Proves(ResourceExpression upper, ResourceExpression limit, CancellationToken cancellation)
    {
        upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        limit = ArgumentNullGuard.NotNull(limit, nameof(limit));
        var remaining = ResourceExpression.MaximumNodes;
        return Compare(upper, limit, ref remaining, cancellation);
    }

    private static bool Compare(ResourceExpression upper, ResourceExpression limit, ref int remaining, CancellationToken cancellation)
    {
        upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        limit = ArgumentNullGuard.NotNull(limit, nameof(limit));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0)
        { throw new ResourceExpressionLimitException(); }
        if (upper.Equals(limit) || upper.Maximum == 0 || upper.Maximum is { } maximum && limit.Constant is { } bound && maximum <= bound)
        { return true; }
        if (upper.Kind == ResourceExpressionKind.Maximum)
        { return Compare(upper.Operands[0], limit, ref remaining, cancellation) && Compare(upper.Operands[1], limit, ref remaining, cancellation); }
        if (limit.Kind == ResourceExpressionKind.Maximum &&
            (Compare(upper, limit.Operands[0], ref remaining, cancellation) || Compare(upper, limit.Operands[1], ref remaining, cancellation)))
        { return true; }
        var left = new Dictionary<ResourceExpression, BigInteger>();
        var right = new Dictionary<ResourceExpression, BigInteger>();
        BigInteger leftConstant = 0, rightConstant = 0;
        Collect(upper, BigInteger.One, true, left, ref leftConstant, ref remaining, cancellation);
        Collect(limit, BigInteger.One, false, right, ref rightConstant, ref remaining, cancellation);
        return leftConstant <= rightConstant && left.All(pair => right.TryGetValue(pair.Key, out var coefficient) && pair.Value <= coefficient);
    }

    private static void Collect(ResourceExpression expression, BigInteger scale, bool enlarge,
        Dictionary<ResourceExpression, BigInteger> terms, ref BigInteger constant, ref int remaining, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        terms = ArgumentNullGuard.NotNull(terms, nameof(terms));
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0 || scale.ToByteArray().Length > 8192)
        { throw new ResourceExpressionLimitException(); }
        if (expression.Constant is { } number)
        { constant += scale * number; return; }
        if (expression.Maximum == 0)
        { return; }
        if (expression.Kind == ResourceExpressionKind.Add)
        {
            Collect(expression.Operands[0], scale, enlarge, terms, ref constant, ref remaining, cancellation);
            Collect(expression.Operands[1], scale, enlarge, terms, ref constant, ref remaining, cancellation);
            return;
        }
        if (expression.Kind == ResourceExpressionKind.Multiply)
        {
            if (expression.Operands[0].Constant is { } left)
            { Collect(expression.Operands[1], scale * left, enlarge, terms, ref constant, ref remaining, cancellation); return; }
            if (expression.Operands[1].Constant is { } right)
            { Collect(expression.Operands[0], scale * right, enlarge, terms, ref constant, ref remaining, cancellation); return; }
        }
        // Each of these operations is <= its nonnegative integer operand.
        if (enlarge && expression.Kind is ResourceExpressionKind.PositiveDifference or ResourceExpressionKind.CeilingDivide or
            ResourceExpressionKind.Digits or ResourceExpressionKind.CeilingLog)
        { Collect(expression.Operands[0], scale, true, terms, ref constant, ref remaining, cancellation); return; }
        if (enlarge && expression.Kind == ResourceExpressionKind.Align)
        {
            constant += scale * (expression.Number - 1);
            Collect(expression.Operands[0], scale, true, terms, ref constant, ref remaining, cancellation);
            return;
        }
        terms.TryGetValue(expression, out var previous);
        terms[expression] = previous + scale;
    }
}
