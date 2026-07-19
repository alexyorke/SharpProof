namespace SharpProof.Symbolic;

internal static class SymbolicProofCacheStore
{
    private const int PerServiceEntryLimit = 2048;
    private const int ProcessFallbackEntryLimit = 4096;
    private static readonly ConditionalWeakTable<SmtAnalysisService, SymbolicProofCache> ServiceCaches = new();
    private static readonly SymbolicProofCache FallbackCache = new(ProcessFallbackEntryLimit);

    internal static SymbolicProofCache Get(SmtAnalysisService? smtAnalysis) =>
        smtAnalysis != null
            ? ServiceCaches.GetValue(
                smtAnalysis,
                static _ => new SymbolicProofCache(PerServiceEntryLimit))
            : FallbackCache;
}

internal sealed class SymbolicProofCache
{
    private const string EncodedStatePrefix = "encoded-state:";
    private const string ResultPrefix = "proof-result:";
    private readonly BoundedConcurrentCache<string, object> _values;

    internal SymbolicProofCache(int capacity)
    {
        _values = new BoundedConcurrentCache<string, object>(capacity, StringComparer.Ordinal);
    }

    internal int Count => _values.Count;
    internal long HitCount => _values.HitCount;
    internal long MissCount => _values.MissCount;
    internal long EvictionCount => _values.EvictionCount;

    internal bool TryGetResult(string key, out SymbolicIrProofResult result)
    {
        if (_values.TryGetValue(ResultPrefix + key, out var value) &&
            value is SymbolicIrProofResult cached)
        {
            result = cached;
            return true;
        }

        result = null!;
        return false;
    }

    internal void TryAddResult(string key, SymbolicIrProofResult result)
    {
        _values.TryAdd(ResultPrefix + key, result);
    }

    internal bool TryGetEncodedState(string key, out SymbolicEncodedState entry)
    {
        if (_values.TryGetValue(EncodedStatePrefix + key, out var value) &&
            value is SymbolicEncodedState cached)
        {
            entry = cached;
            return true;
        }

        entry = default;
        return false;
    }

    internal void TryAddEncodedState(string key, SymbolicEncodedState entry)
    {
        _values.TryAdd(EncodedStatePrefix + key, entry);
    }
}

internal readonly record struct SymbolicEncodedState(
    bool Success,
    ImmutableArray<SmtFormula> PathConditions,
    SymbolicUnknownReason UnknownReason);
