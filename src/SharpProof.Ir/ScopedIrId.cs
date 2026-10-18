namespace SharpProof.Ir;

public readonly record struct ScopedIrId<TTag>
    where TTag : struct, IIrIdentifierTag
{
    internal ScopedIrId(long scope, int value)
    {
        (Scope, Value) = (scope, value);
    }

    internal long Scope
    {
        get;
    }

    public int Value
    {
        get;
    }

    public bool IsDefault => Scope == 0;

    public override int GetHashCode()
    {
        return ScopedIdentifierHashCode.Compute(Scope, Value);
    }

    public override string ToString()
    {
        return default(TTag).Prefix +
            Value.ToString(CultureInfo.InvariantCulture);
    }
}

internal static class ScopedIdentifierHashCode
{
    internal static int Compute(long scope, int value)
    {
        return unchecked(
            ((int)scope * 397) ^
            (int)(scope >> 32) ^
            value);
    }
}
