namespace SharpProof.Tools.Fuzz;

public sealed record FuzzCase(
    string Name,
    string Family,
    string Source,
    bool AllowUnsafe,
    FuzzExpectation Expectation,
    ImmutableArray<string> PrimaryShapeIds = default,
    ImmutableArray<string> ExpectedOperationKinds = default,
    ImmutableArray<string> ExpectedSyntaxKinds = default);

public sealed record FuzzCaseAnalysis(
    FuzzCase Case,
    ImmutableSortedDictionary<string, int> OperationKinds,
    ImmutableSortedDictionary<string, int> SyntaxKinds,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> DiagnosticSignatures,
    ImmutableArray<string> CompilationErrors,
    string NormalizedSourceHash,
    ImmutableArray<FuzzFinding> Findings);

public sealed record FuzzFinding(
    string CaseName,
    string Family,
    string Category,
    string Description,
    string? SourcePath,
    ImmutableArray<string> Details,
    int OccurrenceCount = 1)
{
    internal string Identity => Family + "|" + Category + "|" + Description + "|" +
                                string.Join("||", Details.OrderBy(static detail => detail, StringComparer.Ordinal));
}

internal static class FuzzDictionaryExtensions
{
    internal static void Increment(this IDictionary<string, int> values, string key) =>
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
}

public sealed record FuzzRunSummary(
    string SchemaVersion,
    int Seed,
    int? IterationsRequested,
    double? DurationSecondsRequested,
    int Parallelism,
    string OutputDirectory,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    double ElapsedSeconds,
    int CasesAnalyzed,
    int CompilationErrorCount,
    int AnalyzerExceptionCount,
    int FindingCount,
    int UniqueFindingCount,
    int InterestingCasesSaved,
    int Sp0002Count,
    int Sp0004Count,
    int Sp0009Count,
    int Sp0010Count,
    ImmutableSortedDictionary<string, int> FamilyCounts,
    ImmutableSortedDictionary<string, int> OperationKinds,
    ImmutableSortedDictionary<string, int> SyntaxKinds,
    ImmutableArray<string> UnobservedOperationKinds,
    ImmutableArray<string> ActionableUnobservedOperationKinds,
    string SamplerMode,
    ImmutableSortedDictionary<string, int> ManifestSurfaceCounts,
    ImmutableSortedDictionary<string, int> ManifestClassificationCounts,
    ImmutableSortedDictionary<string, int> RegistryExpectationCounts,
    int DefiniteRegistryFamilyCount,
    int ConservativeRegistryFamilyCount,
    ImmutableArray<string> ConservativeRegistryFamilies,
    int GeneratorBackedShapeCount,
    int GeneratorBackedShapesWithRegistryCount,
    ImmutableArray<string> UnobservedGeneratorBackedShapes,
    ImmutableSortedDictionary<string, int> PrimaryShapeCounts,
    ImmutableArray<FuzzFinding> Findings);
