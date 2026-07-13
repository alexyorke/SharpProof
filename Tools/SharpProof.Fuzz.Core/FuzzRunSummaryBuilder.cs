namespace SharpProof.Tools.Fuzz;

internal sealed class FuzzRunSummaryBuilder
{
    private const string ConservativeExpectationBucket = "conservative";
    private const string DefinitelyImpureExpectationBucket = "definitely_impure";
    private const string DefinitelyPureExpectationBucket = "definitely_pure";
    private readonly SortedDictionary<string, int> _familyCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _findingIndices = new(StringComparer.Ordinal);
    private readonly ImmutableArray<FuzzFinding>.Builder _findings = ImmutableArray.CreateBuilder<FuzzFinding>();
    private readonly SortedDictionary<string, int> _operationKinds = new(StringComparer.Ordinal);

    private readonly FuzzOptions _options;
    private readonly SortedDictionary<string, int> _primaryShapeCounts = new(StringComparer.Ordinal);
    private readonly string _samplerMode;
    private readonly DateTimeOffset _startedUtc;
    private readonly SortedDictionary<string, int> _syntaxKinds = new(StringComparer.Ordinal);

    private int _compilationErrorCount;
    private int _sp0002Count;
    private int _sp0004Count;
    private int _sp0009Count;
    private int _sp0010Count;

    public FuzzRunSummaryBuilder(FuzzOptions options, DateTimeOffset startedUtc, string samplerMode)
    {
        _options = options;
        _startedUtc = startedUtc;
        _samplerMode = samplerMode;
    }

    public int CasesAnalyzed { get; private set; }

    public void Add(FuzzCaseAnalysis analysis)
    {
        CasesAnalyzed++;
        Increment(_familyCounts, analysis.Case.Family);
        AddAll(_operationKinds, analysis.OperationKinds);
        AddAll(_syntaxKinds, analysis.SyntaxKinds);
        if (!analysis.Case.PrimaryShapeIds.IsDefaultOrEmpty)
            foreach (var shapeId in analysis.Case.PrimaryShapeIds)
                Increment(_primaryShapeCounts, shapeId);

        _compilationErrorCount += analysis.CompilationErrors.Length > 0 ? 1 : 0;
        foreach (var finding in analysis.Findings) AddFinding(analysis.NormalizedSourceHash, finding);

        foreach (var diagnostic in analysis.Diagnostics)
            if (diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                _sp0002Count++;
            else if (diagnostic.Id == SharpProofDiagnostics.MissingEnforcePureAttributeId)
                _sp0004Count++;
            else if (diagnostic.Id == SharpProofDiagnostics.PurityExplanationId)
                _sp0009Count++;
            else if (diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId) _sp0010Count++;
    }

    public FuzzRunSummary Build(DateTimeOffset completedUtc, TimeSpan elapsed, string outputDirectory,
        int interestingCasesSaved)
    {
        var findings = _findings
            .OrderByDescending(finding => finding.OccurrenceCount)
            .ThenBy(finding => finding.Category, StringComparer.Ordinal)
            .ThenBy(finding => finding.Family, StringComparer.Ordinal)
            .ToImmutableArray();
        var analyzerExceptionCount = findings
            .Where(finding => finding.Category == "analyzer_exception")
            .Sum(finding => finding.OccurrenceCount);
        var totalFindingCount = findings.Sum(finding => finding.OccurrenceCount);
        var observedOperationKinds = _operationKinds.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        var unobservedOperationKinds = Enum.GetNames<OperationKind>()
            .Where(kind => !observedOperationKinds.Contains(kind))
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToImmutableArray();
        var actionableUnobservedOperationKinds = Enum.GetValues<OperationKind>()
            .Where(kind => !observedOperationKinds.Contains(kind.ToString()))
            .Where(RoslynShapeManifest.IsActionableUnobservedOperationKind)
            .Select(kind => kind.ToString())
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToImmutableArray();
        var manifestSurfaceCounts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            [RoslynShapeSurface.OperationKind.ToString()] = RoslynShapeManifest.OperationEntries.Length,
            [RoslynShapeSurface.SyntaxKind.ToString()] = RoslynShapeManifest.SyntaxEntries.Length,
            ["AnalyzerActionSurface"] = RoslynShapeManifest.ActionSurfaceEntries.Length
        };
        var manifestClassificationCounts = RoslynShapeManifest.EntriesByShapeId.Values
            .GroupBy(entry => entry.Classification.ToString(), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var registryExpectationCounts = CreateRegistryExpectationCounts();
        var conservativeRegistryFamilies = FuzzCaseGenerator.RegistryEntries
            .Where(static entry => IsConservativeExpectation(entry.Expectation))
            .Select(static entry => entry.Id)
            .OrderBy(family => family, StringComparer.Ordinal)
            .ToImmutableArray();
        var registryCoveredShapeIds = FuzzCaseGenerator.RegistryEntries
            .SelectMany(entry => entry.PrimaryShapeIds)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var unobservedGeneratorBackedShapes = RoslynShapeManifest.GeneratorBackedShapeIds
            .Where(shapeId => !_primaryShapeCounts.ContainsKey(shapeId))
            .OrderBy(shapeId => shapeId, StringComparer.Ordinal)
            .ToImmutableArray();

        return new FuzzRunSummary
        {
            Seed = _options.Seed,
            IterationsRequested = _options.Iterations,
            DurationSecondsRequested = _options.Duration?.TotalSeconds,
            Parallelism = _options.Parallelism,
            OutputDirectory = outputDirectory,
            StartedUtc = _startedUtc,
            CompletedUtc = completedUtc,
            ElapsedSeconds = elapsed.TotalSeconds,
            CasesAnalyzed = CasesAnalyzed,
            CompilationErrorCount = _compilationErrorCount,
            AnalyzerExceptionCount = analyzerExceptionCount,
            FindingCount = totalFindingCount,
            UniqueFindingCount = findings.Length,
            InterestingCasesSaved = interestingCasesSaved,
            Sp0002Count = _sp0002Count,
            Sp0004Count = _sp0004Count,
            Sp0009Count = _sp0009Count,
            Sp0010Count = _sp0010Count,
            FamilyCounts = _familyCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            OperationKinds = _operationKinds.ToImmutableSortedDictionary(StringComparer.Ordinal),
            SyntaxKinds = _syntaxKinds.ToImmutableSortedDictionary(StringComparer.Ordinal),
            UnobservedOperationKinds = unobservedOperationKinds,
            ActionableUnobservedOperationKinds = actionableUnobservedOperationKinds,
            SamplerMode = _samplerMode,
            ManifestSurfaceCounts = manifestSurfaceCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            ManifestClassificationCounts = manifestClassificationCounts,
            RegistryExpectationCounts = registryExpectationCounts,
            DefiniteRegistryFamilyCount = registryExpectationCounts[DefinitelyPureExpectationBucket] +
                                          registryExpectationCounts[DefinitelyImpureExpectationBucket],
            ConservativeRegistryFamilyCount = conservativeRegistryFamilies.Length,
            ConservativeRegistryFamilies = conservativeRegistryFamilies,
            GeneratorBackedShapeCount = RoslynShapeManifest.GeneratorBackedShapeIds.Length,
            GeneratorBackedShapesWithRegistryCount = registryCoveredShapeIds.Count,
            UnobservedGeneratorBackedShapes = unobservedGeneratorBackedShapes,
            PrimaryShapeCounts = _primaryShapeCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Findings = findings
        };
    }

    private static ImmutableSortedDictionary<string, int> CreateRegistryExpectationCounts()
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            [ConservativeExpectationBucket] = 0,
            [DefinitelyImpureExpectationBucket] = 0,
            [DefinitelyPureExpectationBucket] = 0
        };

        foreach (var entry in FuzzCaseGenerator.RegistryEntries)
            Increment(counts, GetExpectationBucket(entry.Expectation));

        return counts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static string GetExpectationBucket(FuzzExpectation expectation)
    {
        if (IsConservativeExpectation(expectation)) return ConservativeExpectationBucket;

        return expectation.Sp0002 == Sp0002ExpectationKind.MustNotEmit &&
               expectation.Sp0010 is Sp0010ExpectationKind.Ignore or Sp0010ExpectationKind.MustNotEmit
            ? DefinitelyPureExpectationBucket
            : DefinitelyImpureExpectationBucket;
    }

    private static bool IsConservativeExpectation(FuzzExpectation expectation)
    {
        return expectation.Sp0002 == Sp0002ExpectationKind.MayEmitConservatively ||
               expectation.Sp0010 == Sp0010ExpectationKind.MayEmitConservatively;
    }

    private void AddFinding(string normalizedSourceHash, FuzzFinding finding)
    {
        var aggregationKey = normalizedSourceHash + "|" + finding.Family + "|" + finding.Category + "|" +
                             finding.Description + "|" +
                             string.Join("||", finding.Details.OrderBy(detail => detail, StringComparer.Ordinal));
        if (_findingIndices.TryGetValue(aggregationKey, out var index))
        {
            var existing = _findings[index];
            _findings[index] = existing with
            {
                OccurrenceCount = existing.OccurrenceCount + finding.OccurrenceCount,
                SourcePath = existing.SourcePath ?? finding.SourcePath
            };
            return;
        }

        _findingIndices.Add(aggregationKey, _findings.Count);
        _findings.Add(finding);
    }

    private static void AddAll(SortedDictionary<string, int> target, IReadOnlyDictionary<string, int> source)
    {
        foreach (var pair in source)
            target[pair.Key] = target.TryGetValue(pair.Key, out var count) ? count + pair.Value : pair.Value;
    }

    private static void Increment(SortedDictionary<string, int> values, string key)
    {
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }
}
