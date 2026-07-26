namespace SharpProof.Ir;

internal static class IrIdentifierHash {
    internal static int Create(long scope, int value) {
        unchecked {
            return ((int)scope * 397) ^ (int)(scope >> 32) ^ value;
        }
    }
}

public readonly struct IrIdentityId : IEquatable<IrIdentityId> {
    internal IrIdentityId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrIdentityId other) =>
        Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) =>
        obj is IrIdentityId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() =>
        "identity" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrIdentityId left, IrIdentityId right) =>
        left.Equals(right);
    public static bool operator !=(IrIdentityId left, IrIdentityId right) =>
        !left.Equals(right);
}

public readonly struct IrId : IEquatable<IrId> {
    internal IrId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is IrId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "ir" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrId left, IrId right) => left.Equals(right);
    public static bool operator !=(IrId left, IrId right) => !left.Equals(right);
}

public readonly struct IrVarId : IEquatable<IrVarId> {
    internal IrVarId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrVarId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is IrVarId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "v" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrVarId left, IrVarId right) => left.Equals(right);
    public static bool operator !=(IrVarId left, IrVarId right) => !left.Equals(right);
}

public readonly struct IrTypeId : IEquatable<IrTypeId> {
    internal IrTypeId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrTypeId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is IrTypeId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "t" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrTypeId left, IrTypeId right) => left.Equals(right);
    public static bool operator !=(IrTypeId left, IrTypeId right) => !left.Equals(right);
}

public readonly struct IrMemberId : IEquatable<IrMemberId> {
    internal IrMemberId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrMemberId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is IrMemberId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "m" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrMemberId left, IrMemberId right) => left.Equals(right);
    public static bool operator !=(IrMemberId left, IrMemberId right) => !left.Equals(right);
}

public readonly struct IrStringId : IEquatable<IrStringId> {
    internal IrStringId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrStringId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is IrStringId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "s" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrStringId left, IrStringId right) => left.Equals(right);
    public static bool operator !=(IrStringId left, IrStringId right) => !left.Equals(right);
}

public readonly struct OperationId : IEquatable<OperationId> {
    internal OperationId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(OperationId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is OperationId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "op" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(OperationId left, OperationId right) => left.Equals(right);
    public static bool operator !=(OperationId left, OperationId right) => !left.Equals(right);
}

public readonly struct IrBlockId : IEquatable<IrBlockId> {
    internal IrBlockId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrBlockId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is IrBlockId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "b" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrBlockId left, IrBlockId right) => left.Equals(right);
    public static bool operator !=(IrBlockId left, IrBlockId right) => !left.Equals(right);
}

public readonly struct IrInstructionId : IEquatable<IrInstructionId> {
    internal IrInstructionId(long scope, int value) {
        Scope = scope;
        Value = value;
    }

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(IrInstructionId other) =>
        Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) =>
        obj is IrInstructionId other && Equals(other);
    public override int GetHashCode() => IrIdentifierHash.Create(Scope, Value);
    public override string ToString() => "i" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(IrInstructionId left, IrInstructionId right) =>
        left.Equals(right);
    public static bool operator !=(IrInstructionId left, IrInstructionId right) =>
        !left.Equals(right);
}
