namespace SharpProof.Effects;

public readonly record struct EffectCapabilitySet
{
    private const EffectCapabilityKind UnknownMarker =
        EffectCapabilityKind.Unknown & ~EffectCapabilityKind.AllKnown;

    public EffectCapabilitySet(EffectCapabilityKind kinds)
    {
        if ((kinds & ~EffectCapabilityKind.Unknown) != 0 ||
            ((kinds & UnknownMarker) != 0 &&
             kinds != EffectCapabilityKind.Unknown))
        {
            throw new ArgumentOutOfRangeException(nameof(kinds));
        }

        Kinds = kinds;
    }

    public static EffectCapabilitySet Empty => default;
    public static EffectCapabilitySet Unknown { get; } = new(EffectCapabilityKind.Unknown);

    public EffectCapabilityKind Kinds
    {
        get;
    }
    public bool IsUnknown => (Kinds & UnknownMarker) != 0;
    public bool IsEmpty => Kinds == EffectCapabilityKind.None;

    public bool Contains(EffectCapabilityKind capability)
    {
        return (Kinds & capability) == capability;
    }

    public bool IsSubsetOf(EffectCapabilitySet other)
    {
        return (Kinds & ~other.Kinds) == 0;
    }

    public EffectCapabilitySet Union(EffectCapabilitySet other)
    {
        return new(Kinds | other.Kinds);
    }
}

internal sealed class EffectDirectWitness(
    EffectContractKind effects,
    EffectContractCapabilityKind capabilities,
    INamedTypeSymbol? exceptionType,
    EffectDirectEventKind eventKind,
    string detail,
    IOperation origin)
{
    internal EffectDirectEventKind EventKind { get; } = eventKind;
    internal EffectContractKind Effects { get; } = effects;
    internal EffectContractCapabilityKind Capabilities { get; } = capabilities;
    internal INamedTypeSymbol? ExceptionType { get; } = exceptionType;
    internal string Kind { get; } =
        EffectDirectEventKinds.ToWireName(eventKind);
    internal string Detail { get; } = detail;
    internal IOperation Origin { get; } =
        ArgumentNullGuard.NotNull(origin, nameof(origin));
    internal Location Location { get; } = origin.Syntax.GetLocation();
}

internal static class EffectDirectEventKinds
{
    internal static readonly (EffectDirectEventKind Event, string WireName)[] WireNames =
        EffectContractMappingCatalog.DirectEvents;

    internal static EffectDirectEventKind FromWireName(string kind)
    {
        foreach (var mapping in WireNames)
        {
            if (string.Equals(
                    mapping.WireName,
                    kind,
                    StringComparison.Ordinal))
            {
                return mapping.Event;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind));
    }

    internal static string ToWireName(EffectDirectEventKind kind)
    {
        foreach (var mapping in WireNames)
        {
            if (mapping.Event == kind)
            {
                return mapping.WireName;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind));
    }
}

public readonly struct EffectThrowSet : IEquatable<EffectThrowSet>
{
    private readonly ImmutableArray<INamedTypeSymbol> _types;

    private EffectThrowSet(ImmutableArray<INamedTypeSymbol> types, bool includesUnknown)
    {
        (_types, IncludesUnknown) = (types, includesUnknown);
    }

    public static EffectThrowSet Empty => default;
    public static EffectThrowSet Unknown { get; } = new([], true);

    public ImmutableArray<INamedTypeSymbol> Types => _types.IsDefault ? [] : _types;
    public bool IncludesUnknown
    {
        get;
    }
    public bool IsEmpty => !IncludesUnknown && _types.IsDefaultOrEmpty;

    public static EffectThrowSet Create(
        IEnumerable<INamedTypeSymbol> types, bool includesUnknown = false)
    {
        types = ArgumentNullGuard.NotNull(types, nameof(types));

        var distinct = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in types)
        {
            distinct.Add(type ?? throw new ArgumentException(
                "Exception type sets cannot contain null.", nameof(types)));
        }

        return distinct.Count == 0
            ? includesUnknown ? Unknown : Empty
            : new EffectThrowSet([
                .. distinct.OrderBy(static type => type, EffectSymbolComparer<INamedTypeSymbol>.Instance)
            ],
                includesUnknown);
    }

    public bool Contains(INamedTypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));

        return IncludesUnknown ||
               Types.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, type));
    }

    public bool IsSubsetOf(EffectThrowSet other)
    {
        if (IsEmpty || other.IncludesUnknown)
        {
            return true;
        }

        if (IncludesUnknown)
        {
            return false;
        }

        foreach (var type in Types)
        {
            if (!other.Contains(type))
            {
                return false;
            }
        }

        return true;
    }

    public EffectThrowSet Union(EffectThrowSet other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return Create(Types.Concat(other.Types), IncludesUnknown || other.IncludesUnknown);
    }

    public bool Equals(EffectThrowSet other)
    {
        return IncludesUnknown == other.IncludesUnknown &&
        Types.AsEnumerable().SequenceEqual(other.Types, SymbolEqualityComparer.Default);
    }

    public override bool Equals(object? obj)
    {
        return obj is EffectThrowSet other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = IncludesUnknown ? 19 : 17;
            foreach (var type in Types)
            {
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(type);
            }

            return hash;
        }
    }

    public static bool operator ==(EffectThrowSet left, EffectThrowSet right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EffectThrowSet left, EffectThrowSet right)
    {
        return !left.Equals(right);
    }
}

internal sealed class EffectSymbolComparer<TSymbol> : IComparer<TSymbol>
    where TSymbol : class, ISymbol
{
    internal static EffectSymbolComparer<TSymbol> Instance { get; } = new();
    private EffectSymbolComparer()
    {
    }

    public int Compare(TSymbol? left, TSymbol? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return -1;
        }

        if (right == null)
        {
            return 1;
        }

        var result = string.Compare(
            CanonicalIdentity(left),
            CanonicalIdentity(right),
            StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        if (SymbolEqualityComparer.Default.Equals(left, right))
        {
            return 0;
        }

        var leftLocation = left.Locations.FirstOrDefault(static location => location.IsInSource);
        var rightLocation = right.Locations.FirstOrDefault(static location => location.IsInSource);
        result = string.Compare(leftLocation?.SourceTree?.FilePath,
            rightLocation?.SourceTree?.FilePath, StringComparison.Ordinal);
        return result != 0
            ? result
            : (leftLocation?.SourceSpan.Start ?? -1)
                .CompareTo(rightLocation?.SourceSpan.Start ?? -1);
    }

    private static string CanonicalIdentity(TSymbol symbol)
    {
        return symbol is ITypeSymbol type
            ? CompilerIdentityBridge.CreateTypeDisplay(type)
            : CompilerIdentityBridge.CreateSymbolDisplay(symbol);
    }
}

internal static class EffectTypeFacts
{
    internal static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol expectedBase)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedBase))
            {
                return true;
            }
        }

        return false;
    }
}
