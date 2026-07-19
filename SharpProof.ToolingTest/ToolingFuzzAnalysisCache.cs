using System.Collections.Immutable;
using SharpProof.Tools.Fuzz;

namespace SharpProof.Test;

internal static class ToolingFuzzAnalysisCache
{
    private static readonly int AnalysisParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

    private static readonly Lazy<Task<ImmutableDictionary<string, FuzzCaseAnalysis>>> RegistryEntryAnalyses =
        new(CreateRegistryEntryAnalysesAsync);

    public static Task<ImmutableDictionary<string, FuzzCaseAnalysis>> GetRegistryEntryAnalysesAsync()
    {
        return RegistryEntryAnalyses.Value;
    }

    private static async Task<ImmutableDictionary<string, FuzzCaseAnalysis>> CreateRegistryEntryAnalysesAsync()
    {
        var generator = new FuzzCaseGenerator(20260614);
        var cases = FuzzCaseGenerator.RegistryEntries
            .Select((entry, index) => generator.GenerateForRegistryEntry(entry, index))
            .ToImmutableArray();
        var analyses = await ToolingFuzzTestRunner.AnalyzeCasesAsync(
            cases,
            false,
            AnalysisParallelism);

        return FuzzCaseGenerator.RegistryEntries
            .Zip(analyses, static (entry, analysis) => new KeyValuePair<string, FuzzCaseAnalysis>(entry.Id, analysis))
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
