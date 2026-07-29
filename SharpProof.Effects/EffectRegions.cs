namespace SharpProof.Effects;

public enum EffectRegionKind {
    Receiver,
    Parameter,
    Captured,
    Static,
    Fresh,
    Ambient,
    Unknown
}

public readonly record struct EffectRegionId : IComparable<EffectRegionId> {
    public EffectRegionId(EffectRegionKind kind, int ordinal = 0) {
        if (!Enum.IsDefined(typeof(EffectRegionKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (kind is EffectRegionKind.Receiver or
            EffectRegionKind.Ambient or
            EffectRegionKind.Unknown &&
            ordinal != 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        Kind = kind;
        Ordinal = ordinal;
    }

    public static EffectRegionId Receiver { get; } = new(EffectRegionKind.Receiver);
    public static EffectRegionId Ambient { get; } = new(EffectRegionKind.Ambient);
    public static EffectRegionId Unknown { get; } = new(EffectRegionKind.Unknown);
    public static EffectRegionId Parameter(int ordinal) => new(EffectRegionKind.Parameter, ordinal);
    public static EffectRegionId Captured(int ordinal) => new(EffectRegionKind.Captured, ordinal);
    public static EffectRegionId Static(int ordinal = 0) => new(EffectRegionKind.Static, ordinal);
    public static EffectRegionId Fresh(int ordinal) => new(EffectRegionKind.Fresh, ordinal);

    public EffectRegionKind Kind { get; }
    public int Ordinal { get; }

    public int CompareTo(EffectRegionId other) =>
        Kind.CompareTo(other.Kind) is var kind && kind != 0 ? kind : Ordinal.CompareTo(other.Ordinal);
}

public readonly struct EffectRegionSet : IEquatable<EffectRegionSet> {
    private readonly ImmutableArray<EffectRegionId> _regions;

    private EffectRegionSet(ImmutableArray<EffectRegionId> regions) => _regions = regions;

    public static EffectRegionSet Empty => default;
    public static EffectRegionSet Unknown { get; } = new([EffectRegionId.Unknown]);

    public ImmutableArray<EffectRegionId> Regions => _regions.IsDefault ? [] : _regions;

    public bool IsEmpty => _regions.IsDefaultOrEmpty;
    public bool IsUnknown => !_regions.IsDefaultOrEmpty &&
        _regions.Length == 1 && _regions[0].Kind == EffectRegionKind.Unknown;

    public static EffectRegionSet Create(params EffectRegionId[] regions) => Create((IEnumerable<EffectRegionId>)regions);

    public static EffectRegionSet Create(IEnumerable<EffectRegionId> regions) {
        if (regions == null) throw new ArgumentNullException(nameof(regions));
        var distinct = regions.Distinct().OrderBy(static region => region).ToImmutableArray();
        if (distinct.Any(static region => region.Kind == EffectRegionKind.Unknown))
            return Unknown;
        return distinct.IsDefaultOrEmpty ? Empty : new EffectRegionSet(distinct);
    }

    public bool Contains(EffectRegionId region) =>
        IsUnknown || Regions.BinarySearch(region) >= 0;

    public bool IsSubsetOf(EffectRegionSet other) {
        if (IsEmpty || other.IsUnknown) return true;
        if (IsUnknown) return false;
        return Regions.All(other.Contains);
    }

    public EffectRegionSet Union(EffectRegionSet other) {
        if (IsUnknown || other.IsUnknown) return Unknown;
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return Create(Regions.Concat(other.Regions));
    }

    public bool Equals(EffectRegionSet other) => Regions.SequenceEqual(other.Regions);

    public override bool Equals(object? obj) => obj is EffectRegionSet other && Equals(other);

    public override int GetHashCode() =>
        Regions.Aggregate(17, static (hash, region) => unchecked(hash * 31 + region.GetHashCode()));

    public static bool operator ==(EffectRegionSet left, EffectRegionSet right) => left.Equals(right);
    public static bool operator !=(EffectRegionSet left, EffectRegionSet right) => !left.Equals(right);
}
