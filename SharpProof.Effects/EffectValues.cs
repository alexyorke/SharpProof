namespace SharpProof.Effects;

[Flags]
public enum EffectAllocationKind {
    None = 0,
    Managed = 1 << 0,
    Native = 1 << 1,
    ManagedAndNative = Managed | Native,
    Unknown = Managed | Native | 1 << 2
}

[Flags]
public enum EffectCapabilityKind {
    None = 0,
    IO = 1 << 0,
    FileRead = 1 << 1,
    FileWrite = 1 << 2,
    Network = 1 << 3,
    Console = 1 << 4,
    Process = 1 << 5,
    Environment = 1 << 6,
    Registry = 1 << 7,
    Clock = 1 << 8,
    Randomness = 1 << 9,
    Reflection = 1 << 10,
    Synchronization = 1 << 11,
    NativeInterop = 1 << 12,
    AllKnown = IO | FileRead | FileWrite | Network | Console | Process |
               Environment | Registry | Clock | Randomness | Reflection |
               Synchronization | NativeInterop,
    Unknown = AllKnown | 1 << 13
}

public readonly record struct EffectCapabilitySet {
    public EffectCapabilitySet(EffectCapabilityKind kinds) {
        if ((kinds & ~EffectCapabilityKind.Unknown) != 0)
            throw new ArgumentOutOfRangeException(nameof(kinds));
        Kinds = kinds;
    }

    public static EffectCapabilitySet Empty => default;
    public static EffectCapabilitySet Unknown { get; } =
        new(EffectCapabilityKind.Unknown);

    public EffectCapabilityKind Kinds { get; }
    public bool IsUnknown =>
        (Kinds & (EffectCapabilityKind)(1 << 13)) != 0;
    public bool IsEmpty => Kinds == EffectCapabilityKind.None;

    public bool Contains(EffectCapabilityKind capability) =>
        (Kinds & capability) == capability;

    public bool IsSubsetOf(EffectCapabilitySet other) =>
        (Kinds & ~other.Kinds) == 0;

    public EffectCapabilitySet Union(EffectCapabilitySet other) =>
        new(Kinds | other.Kinds);

}

public enum EffectTermination {
    Bottom,
    Terminates,
    MayDiverge,
    Unknown
}

public enum EffectCompleteness {
    Complete,
    Incomplete
}

[Flags]
public enum EffectUncertainty {
    None = 0,
    DirectCall = 1 << 0,
    Dispatch = 1 << 1,
    UnsupportedOperation = 1 << 2,
    UnmodeledCall = 1 << 3,
    Recursion = 1 << 4,
    InvalidContract = 1 << 5,
    All = DirectCall | Dispatch | UnsupportedOperation | UnmodeledCall |
          Recursion | InvalidContract,
    Unknown = All | 1 << 6
}

public readonly struct EffectThrowSet : IEquatable<EffectThrowSet> {
    private readonly ImmutableArray<INamedTypeSymbol> _types;

    private EffectThrowSet(
        ImmutableArray<INamedTypeSymbol> types, bool includesUnknown) =>
        (_types, IncludesUnknown) = (types, includesUnknown);

    public static EffectThrowSet Empty => default;
    public static EffectThrowSet Unknown { get; } =
        new([], true);

    public ImmutableArray<INamedTypeSymbol> Types =>
        _types.IsDefault ? [] : _types;
    public bool IncludesUnknown { get; }
    public bool IsEmpty => !IncludesUnknown && _types.IsDefaultOrEmpty;

    public static EffectThrowSet Create(
        IEnumerable<INamedTypeSymbol> types, bool includesUnknown = false) {
        if (types == null) throw new ArgumentNullException(nameof(types));
        var distinct = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in types)
            distinct.Add(type ?? throw new ArgumentException(
                "Exception type sets cannot contain null.",
                nameof(types)));
        return distinct.Count == 0
            ? includesUnknown ? Unknown : Empty
            : new EffectThrowSet(
                [.. distinct.OrderBy(
                    static type => type,
                    EffectSymbolComparer<INamedTypeSymbol>.Instance)],
                includesUnknown);
    }

    public bool Contains(INamedTypeSymbol type) {
        if (type == null) throw new ArgumentNullException(nameof(type));
        return IncludesUnknown ||
               Types.Any(candidate =>
                   SymbolEqualityComparer.Default.Equals(candidate, type));
    }

    public bool IsSubsetOf(EffectThrowSet other) {
        if (IsEmpty || other.IncludesUnknown) return true;
        if (IncludesUnknown) return false;
        foreach (var type in Types)
            if (!other.Contains(type))
                return false;
        return true;
    }

    public EffectThrowSet Union(EffectThrowSet other) {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return Create(
            Types.Concat(other.Types),
            IncludesUnknown || other.IncludesUnknown);
    }

    public bool Equals(EffectThrowSet other) =>
        IncludesUnknown == other.IncludesUnknown &&
        Types.AsEnumerable().SequenceEqual(
            other.Types,
            SymbolEqualityComparer.Default);

    public override bool Equals(object? obj) => obj is EffectThrowSet other && Equals(other);

    public override int GetHashCode() {
        unchecked {
            var hash = IncludesUnknown ? 19 : 17;
            foreach (var type in Types)
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(type);
            return hash;
        }
    }

    public static bool operator ==(EffectThrowSet left, EffectThrowSet right) =>
        left.Equals(right);
    public static bool operator !=(EffectThrowSet left, EffectThrowSet right) =>
        !left.Equals(right);
}

internal sealed class EffectSymbolComparer<TSymbol> : IComparer<TSymbol>
    where TSymbol : class, ISymbol {
    internal static EffectSymbolComparer<TSymbol> Instance { get; } = new();

    private EffectSymbolComparer() {
    }

    public int Compare(TSymbol? left, TSymbol? right) {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        var result = string.Compare(
            left.ContainingAssembly?.Identity.Name,
            right.ContainingAssembly?.Identity.Name,
            StringComparison.Ordinal);
        if (result != 0) return result;
        result = string.Compare(
            DocumentationCommentId.CreateDeclarationId(left) ??
            left.Kind + ":" + left.MetadataName,
            DocumentationCommentId.CreateDeclarationId(right) ??
            right.Kind + ":" + right.MetadataName,
            StringComparison.Ordinal);
        if (result != 0) return result;
        var leftLocation = left.Locations.FirstOrDefault(static location => location.IsInSource);
        var rightLocation = right.Locations.FirstOrDefault(static location => location.IsInSource);
        result = string.Compare(
            leftLocation?.SourceTree?.FilePath,
            rightLocation?.SourceTree?.FilePath,
            StringComparison.Ordinal);
        return result != 0
            ? result
            : (leftLocation?.SourceSpan.Start ?? -1)
                .CompareTo(rightLocation?.SourceSpan.Start ?? -1);
    }
}

internal static class EffectTypeFacts {
    internal static bool IsDerivedFrom(
        INamedTypeSymbol type,
        INamedTypeSymbol expectedBase) {
        for (var current = type; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    expectedBase.OriginalDefinition))
                return true;
        return false;
    }
}
