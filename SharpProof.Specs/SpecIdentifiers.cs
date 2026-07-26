namespace SharpProof.Specs;

public readonly struct SpecId : IEquatable<SpecId> {
    internal SpecId(long scope, int value) =>
        (Scope, Value) = (scope, value);

    internal long Scope { get; }
    public int Value { get; }
    public bool IsDefault => Scope == 0;

    public bool Equals(SpecId other) => Scope == other.Scope && Value == other.Value;
    public override bool Equals(object? obj) => obj is SpecId other && Equals(other);
    public override int GetHashCode() =>
        unchecked(((int)Scope * 397) ^ (int)(Scope >> 32) ^ Value);
    public override string ToString() => "spec" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(SpecId left, SpecId right) => left.Equals(right);
    public static bool operator !=(SpecId left, SpecId right) => !left.Equals(right);
}

public readonly struct SpecVarId : IEquatable<SpecVarId> {
    internal SpecVarId(SpecId spec, int value) =>
        (Spec, Value) = (spec, value);

    public SpecId Spec { get; }
    public int Value { get; }
    public bool IsDefault => Spec.IsDefault;

    public bool Equals(SpecVarId other) => Spec == other.Spec && Value == other.Value;
    public override bool Equals(object? obj) => obj is SpecVarId other && Equals(other);
    public override int GetHashCode() =>
        unchecked(Spec.GetHashCode() * 397 ^ Value);
    public override string ToString() =>
        Spec + ".var" + Value.ToString(CultureInfo.InvariantCulture);
    public static bool operator ==(SpecVarId left, SpecVarId right) => left.Equals(right);
    public static bool operator !=(SpecVarId left, SpecVarId right) => !left.Equals(right);
}
