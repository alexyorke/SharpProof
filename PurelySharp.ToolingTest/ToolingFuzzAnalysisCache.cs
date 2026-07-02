using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using PurelySharp.Tools.Fuzz;

namespace PurelySharp.Test;

internal static class ToolingFuzzAnalysisCache
{
    private static readonly Lazy<Task<ImmutableDictionary<string, FuzzCaseAnalysis>>> RegistryEntryAnalyses =
        new(CreateRegistryEntryAnalysesAsync);

    public static Task<ImmutableDictionary<string, FuzzCaseAnalysis>> GetRegistryEntryAnalysesAsync()
    {
        return RegistryEntryAnalyses.Value;
    }

    private static async Task<ImmutableDictionary<string, FuzzCaseAnalysis>> CreateRegistryEntryAnalysesAsync()
    {
        var generator = new FuzzCaseGenerator(20260614);
        var analyses = await Task.WhenAll(
            FuzzCaseGenerator.RegistryEntries.Select(
                (entry, index) => FuzzRunner.AnalyzeCaseAsync(
                    generator.GenerateForRegistryEntry(entry, index),
                    repeatAnalyzer: false)));

        return FuzzCaseGenerator.RegistryEntries
            .Zip(analyses, static (entry, analysis) => new KeyValuePair<string, FuzzCaseAnalysis>(entry.Id, analysis))
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
