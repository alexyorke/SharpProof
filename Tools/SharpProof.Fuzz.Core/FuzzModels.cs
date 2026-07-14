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

public sealed record FuzzRunSummary
{
    public string SchemaVersion { get; init; } = "1.3";

    public int Seed { get; init; }

    public int? IterationsRequested { get; init; }

    public double? DurationSecondsRequested { get; init; }

    public int Parallelism { get; init; }

    public string OutputDirectory { get; init; } = "";

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset CompletedUtc { get; init; }

    public double ElapsedSeconds { get; init; }

    public int CasesAnalyzed { get; init; }

    public int CompilationErrorCount { get; init; }

    public int AnalyzerExceptionCount { get; init; }

    public int FindingCount { get; init; }

    public int UniqueFindingCount { get; init; }

    public int InterestingCasesSaved { get; init; }

    public int Sp0002Count { get; init; }

    public int Sp0004Count { get; init; }

    public int Sp0009Count { get; init; }

    public int Sp0010Count { get; init; }

    public ImmutableSortedDictionary<string, int> FamilyCounts { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public ImmutableSortedDictionary<string, int> OperationKinds { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public ImmutableSortedDictionary<string, int> SyntaxKinds { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public ImmutableArray<string> UnobservedOperationKinds { get; init; } =
        ImmutableArray<string>.Empty;

    public ImmutableArray<string> ActionableUnobservedOperationKinds { get; init; } =
        ImmutableArray<string>.Empty;

    public string SamplerMode { get; init; } = "";

    public ImmutableSortedDictionary<string, int> ManifestSurfaceCounts { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public ImmutableSortedDictionary<string, int> ManifestClassificationCounts { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public ImmutableSortedDictionary<string, int> RegistryExpectationCounts { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public int DefiniteRegistryFamilyCount { get; init; }

    public int ConservativeRegistryFamilyCount { get; init; }

    public ImmutableArray<string> ConservativeRegistryFamilies { get; init; } =
        ImmutableArray<string>.Empty;

    public int GeneratorBackedShapeCount { get; init; }

    public int GeneratorBackedShapesWithRegistryCount { get; init; }

    public ImmutableArray<string> UnobservedGeneratorBackedShapes { get; init; } =
        ImmutableArray<string>.Empty;

    public ImmutableSortedDictionary<string, int> PrimaryShapeCounts { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public ImmutableArray<FuzzFinding> Findings { get; init; } =
        ImmutableArray<FuzzFinding>.Empty;
}
