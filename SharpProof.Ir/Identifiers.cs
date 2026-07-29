namespace SharpProof.Ir;

internal static class IrIdentifierHash
{
    internal static int Create(long scope, int value)
    {
        return unchecked(((int)scope * 397) ^ (int)(scope >> 32) ^ value);
    }
}

public readonly record struct IrIdentityId
{
    internal IrIdentityId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "identity" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct IrId
{
    internal IrId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "ir" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct IrVarId
{
    internal IrVarId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "v" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct IrTypeId
{
    internal IrTypeId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "t" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct IrMemberId
{
    internal IrMemberId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "m" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct IrStringId
{
    internal IrStringId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "s" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct OperationId
{
    internal OperationId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "op" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct IrBlockId
{
    internal IrBlockId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "b" + Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct IrInstructionId
{
    internal IrInstructionId(long scope, int value)
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
        return IrIdentifierHash.Create(Scope, Value);
    }

    public override string ToString()
    {
        return "i" + Value.ToString(CultureInfo.InvariantCulture);
    }
}
