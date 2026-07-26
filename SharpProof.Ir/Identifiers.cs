namespace SharpProof.Ir;

internal static class IrIdentifierHash {
    internal static int Create(long scope, int value) {
        unchecked {
            return ((int)scope * 397) ^ (int)(scope >> 32) ^ value;
        }
    }
}

public readonly record struct IrIdentityId {
    internal IrIdentityId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() =>
        "identity" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct IrId {
    internal IrId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "ir" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct IrVarId {
    internal IrVarId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "v" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct IrTypeId {
    internal IrTypeId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "t" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct IrMemberId {
    internal IrMemberId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "m" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct IrStringId {
    internal IrStringId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "s" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct OperationId {
    internal OperationId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "op" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct IrBlockId {
    internal IrBlockId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "b" + Value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct IrInstructionId {
    internal IrInstructionId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "i" + Value.ToString(CultureInfo.InvariantCulture);
}
