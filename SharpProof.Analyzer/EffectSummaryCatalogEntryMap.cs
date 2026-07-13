using System.Collections.Immutable;

namespace SharpProof.Analyzer;

internal static class EffectSummaryCatalogEntryMap
{
    internal static Dictionary<string, ImmutableArray<TEntry>.Builder> Clone<TEntry>(
        ImmutableDictionary<string, ImmutableArray<TEntry>> entriesBySymbol)
    {
        var clone = new Dictionary<string, ImmutableArray<TEntry>.Builder>(StringComparer.Ordinal);
        foreach (var entry in entriesBySymbol)
        {
            var builder = ImmutableArray.CreateBuilder<TEntry>(entry.Value.Length);
            builder.AddRange(entry.Value);
            clone.Add(entry.Key, builder);
        }

        return clone;
    }

    internal static ImmutableDictionary<string, ImmutableArray<TEntry>> Freeze<TEntry>(
        Dictionary<string, ImmutableArray<TEntry>.Builder> entriesBySymbol)
    {
        return entriesBySymbol.ToImmutableDictionary(
            item => item.Key,
            item => item.Value.ToImmutable(),
            StringComparer.Ordinal);
    }

    internal static void Add<TEntry>(
        Dictionary<string, ImmutableArray<TEntry>.Builder> entriesBySymbol,
        IEnumerable<TEntry> entries,
        Func<TEntry, string> getSymbol)
    {
        foreach (var entry in entries)
        {
            var symbol = getSymbol(entry);
            if (!entriesBySymbol.TryGetValue(symbol, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<TEntry>();
                entriesBySymbol.Add(symbol, builder);
            }

            builder.Add(entry);
        }
    }
}
