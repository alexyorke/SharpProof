namespace SharpProof.Analyzer;

internal interface IEffectSummaryCatalogEntry
{
    string Symbol { get; }
}

internal static class EffectSummaryCatalogSourcePriorities
{
    internal const int BuiltIn = 0;
    internal const int Additional = 1;
}

internal abstract class EffectSummaryCatalogEntry(
    string symbol,
    string displaySymbol,
    SummaryAssemblyIdentity? assemblyIdentity,
    SummaryMethodIdentity? methodIdentity,
    EffectSummaryArtifactSource? artifactSource,
    int sourcePriority,
    string? sourcePath,
    EffectSummaryCompatibilityReporter? compatibilityReporter) : IEffectSummaryCatalogEntry
{
    public string Symbol { get; } = symbol;

    protected string DisplaySymbol { get; } = displaySymbol;

    protected EffectSummaryEntryTrustMetadata Trust { get; } = new(
        assemblyIdentity,
        methodIdentity,
        artifactSource,
        sourcePriority,
        EffectSummaryCatalogSourcePriorities.BuiltIn,
        EffectSummaryCatalogSourcePriorities.Additional,
        sourcePath,
        compatibilityReporter);

    internal SummaryAssemblyIdentity? AssemblyIdentity => Trust.AssemblyIdentity;

    internal SummaryMethodIdentity? MethodIdentity => Trust.MethodIdentity;

    internal int SourcePriority => Trust.SourcePriority;

    internal string? SourcePath => Trust.SourcePath;

    internal bool IsTrustedFor(
        IMethodSymbol methodSymbol,
        ActualAssemblyIdentity? actualAssemblyIdentity,
        ActualMethodIdentity? actualMethodIdentity)
    {
        return Trust.IsTrustedFor(
            methodSymbol,
            actualAssemblyIdentity,
            actualMethodIdentity,
            DisplaySymbol);
    }

    internal bool IsTrustedFor(
        ActualAssemblyIdentity? actualAssemblyIdentity,
        ActualMethodIdentity? actualMethodIdentity)
    {
        return Trust.IsTrustedFor(actualAssemblyIdentity, actualMethodIdentity, DisplaySymbol);
    }
}

internal static class EffectSummaryCatalogEntryMap
{
    internal static IEnumerable<TEntry> EnumerateCompatible<TEntry>(
        ImmutableDictionary<string, ImmutableArray<TEntry>> entriesBySymbol,
        IMethodSymbol methodSymbol)
    {
        return Enumerate(
            entriesBySymbol,
            RoslynStructuralMethodIdentity.GetCompatibleCanonicalKeys(methodSymbol));
    }

    internal static IEnumerable<TEntry> Enumerate<TEntry>(
        ImmutableDictionary<string, ImmutableArray<TEntry>> entriesBySymbol,
        IEnumerable<string> symbolKeys)
    {
        foreach (var key in symbolKeys)
            if (entriesBySymbol.TryGetValue(key, out var entries))
                foreach (var entry in entries)
                    yield return entry;
    }

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

    internal static void AddJson<TEntry>(
        Dictionary<string, ImmutableArray<TEntry>.Builder> entriesBySymbol,
        string json,
        Func<EffectSummaryJsonDocument, IEnumerable<TEntry>> getEntries,
        Func<TEntry, string> getSymbol)
    {
        if (!EffectSummaryJsonDocument.TryParse(json, out var document, out _)) return;

        using (document)
            Add(entriesBySymbol, getEntries(document), getSymbol);
    }

    internal static void AddJson<TEntry>(
        Dictionary<string, ImmutableArray<TEntry>.Builder> entriesBySymbol,
        string json,
        int sourcePriority,
        string? sourcePath,
        EffectSummaryCompatibilityReporter? compatibilityReporter,
        Func<EffectSummaryJsonDocument, int, string?, EffectSummaryCompatibilityReporter?, IEnumerable<TEntry>>
            parseEntries)
        where TEntry : IEffectSummaryCatalogEntry
    {
        AddJson(
            entriesBySymbol,
            json,
            document => parseEntries(document, sourcePriority, sourcePath, compatibilityReporter),
            static entry => entry.Symbol);
    }
}
