using System.Runtime.CompilerServices;

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
    private static readonly ImmutableDictionary<string, EffectDirectEventKind>
        EventsByWireName = WireNames.ToImmutableDictionary(
            static mapping => mapping.WireName,
            static mapping => mapping.Event,
            StringComparer.Ordinal);
    private static readonly ImmutableDictionary<EffectDirectEventKind, string>
        WireNamesByEvent = WireNames.ToImmutableDictionary(
            static mapping => mapping.Event,
            static mapping => mapping.WireName);

    internal static EffectDirectEventKind FromWireName(string kind)
    {
        if (kind != null && EventsByWireName.TryGetValue(kind, out var eventKind))
        {
            return eventKind;
        }

        throw new ArgumentOutOfRangeException(nameof(kind));
    }

    internal static string ToWireName(EffectDirectEventKind kind)
    {
        if (WireNamesByEvent.TryGetValue(kind, out var wireName))
        {
            return wireName;
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
    private static readonly ConditionalWeakTable<TSymbol, SymbolSortKey>
        SortKeys = new();

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

        var leftKey = GetSortKey(left);
        var rightKey = GetSortKey(right);
        var result = string.Compare(
            leftKey.Identity,
            rightKey.Identity,
            StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        if (SymbolEqualityComparer.Default.Equals(left, right))
        {
            return 0;
        }

        result = string.Compare(leftKey.SourcePath,
            rightKey.SourcePath, StringComparison.Ordinal);
        return result != 0
            ? result
            : leftKey.SourceStart.CompareTo(rightKey.SourceStart);
    }

    private static SymbolSortKey GetSortKey(TSymbol symbol)
    {
        return SortKeys.GetValue(symbol, static value => new(value));
    }

    private sealed class SymbolSortKey
    {
        internal SymbolSortKey(TSymbol symbol)
        {
            Identity = CanonicalIdentity(symbol);
            var location = symbol.Locations.FirstOrDefault(
                static candidate => candidate.IsInSource);
            SourcePath = location?.SourceTree?.FilePath;
            SourceStart = location?.SourceSpan.Start ?? -1;
        }

        internal string Identity
        {
            get;
        }

        internal string? SourcePath
        {
            get;
        }

        internal int SourceStart
        {
            get;
        }
    }

    private static string CanonicalIdentity(ISymbol symbol)
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
