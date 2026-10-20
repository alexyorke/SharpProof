namespace SharpProof.Ir;

// A declared input assumption, never a branch-frequency estimate. Conditional
// bounds for both valuations remain part of the ordinary worst-case proof.
internal sealed class ResourceBooleanDistribution
{
    internal const int MaximumWeight = 1000000;
    internal int Ordinal { get; }
    internal string Name { get; }
    internal int TrueWeight { get; }
    internal int FalseWeight { get; }
    internal int TotalWeight => TrueWeight + FalseWeight;

    internal ResourceBooleanDistribution(int ordinal, string name, int trueWeight, int falseWeight)
    {
        name = ArgumentNullGuard.NotNull(name, nameof(name));
        if (ordinal < 0)
        { throw new ArgumentOutOfRangeException(nameof(ordinal)); }
        if (string.IsNullOrWhiteSpace(name) || name.Length > ResourceExpression.MaximumTextLength || name.Any(char.IsControl))
        { throw new ArgumentException("A canonical parameter name is required.", nameof(name)); }
        if (trueWeight is < 0 or > MaximumWeight)
        { throw new ArgumentOutOfRangeException(nameof(trueWeight)); }
        if (falseWeight is < 0 or > MaximumWeight || trueWeight + falseWeight == 0)
        { throw new ArgumentOutOfRangeException(nameof(falseWeight)); }
        Ordinal = ordinal;
        Name = name;
        TrueWeight = trueWeight;
        FalseWeight = falseWeight;
    }

    internal ResourceExpression ExpectedUpper(ResourceExpression whenFalse, ResourceExpression whenTrue)
    {
        whenFalse = ArgumentNullGuard.NotNull(whenFalse, nameof(whenFalse));
        whenTrue = ArgumentNullGuard.NotNull(whenTrue, nameof(whenTrue));
        return ResourceExpression.DivideCeiling(ResourceExpression.Add(
            ResourceExpression.Multiply(whenFalse, ResourceExpression.Literal(FalseWeight)),
            ResourceExpression.Multiply(whenTrue, ResourceExpression.Literal(TrueWeight))), TotalWeight);
    }
}
