namespace SharpProof.Tools.Fuzz;

internal sealed class FuzzRunSummaryBuilder(FuzzOptions options, DateTimeOffset startedUtc, string samplerMode) {
    private readonly SortedDictionary<string, int> _familyCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _diagnosticCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _findingIndices = new(StringComparer.Ordinal);
    private readonly ImmutableArray<FuzzFinding>.Builder _findings = ImmutableArray.CreateBuilder<FuzzFinding>();
    private readonly SortedDictionary<string, int> _operationKinds = new(StringComparer.Ordinal);

    private readonly FuzzOptions _options = options;
    private readonly SortedDictionary<string, int> _primaryShapeCounts = new(StringComparer.Ordinal);
    private readonly string _samplerMode = samplerMode;
    private readonly DateTimeOffset _startedUtc = startedUtc;
    private readonly SortedDictionary<string, int> _syntaxKinds = new(StringComparer.Ordinal);

    private int _compilationErrorCount;

    public int CasesAnalyzed { get; private set; }

    public void Add(FuzzCaseAnalysis analysis) {
        CasesAnalyzed++;
        _familyCounts.Increment(analysis.Case.Family);
        AddAll(_operationKinds, analysis.OperationKinds);
        AddAll(_syntaxKinds, analysis.SyntaxKinds);
        if (!analysis.Case.PrimaryShapeIds.IsDefaultOrEmpty)
            foreach (var shapeId in analysis.Case.PrimaryShapeIds)
                _primaryShapeCounts.Increment(shapeId);

        _compilationErrorCount += analysis.CompilationErrors.Length > 0 ? 1 : 0;
        foreach (var finding in analysis.Findings) AddFinding(analysis.NormalizedSourceHash, finding);

        foreach (var diagnostic in analysis.Diagnostics) _diagnosticCounts.Increment(diagnostic.Id);
    }

    public FuzzRunSummary Build(DateTimeOffset completedUtc, TimeSpan elapsed, string outputDirectory,
        int interestingCasesSaved) {
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
        var manifestSurfaceCounts = new SortedDictionary<string, int>(StringComparer.Ordinal) {
            [RoslynShapeSurface.OperationKind.ToString()] = RoslynShapeManifest.OperationEntries.Length,
            [RoslynShapeSurface.SyntaxKind.ToString()] = RoslynShapeManifest.SyntaxEntries.Length,
            ["AnalyzerActionSurface"] = RoslynShapeManifest.ActionSurfaceEntries.Count
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
            .Where(static entry => entry.Expectation.IsConservative)
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

        return new FuzzRunSummary(
            "1.3",
            _options.Seed,
            _options.Iterations,
            _options.Duration?.TotalSeconds,
            _options.Parallelism,
            outputDirectory,
            _startedUtc,
            completedUtc,
            elapsed.TotalSeconds,
            CasesAnalyzed,
            _compilationErrorCount,
            analyzerExceptionCount,
            totalFindingCount,
            findings.Length,
            interestingCasesSaved,
            DiagnosticCount("SP0002"),
            _familyCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            _operationKinds.ToImmutableSortedDictionary(StringComparer.Ordinal),
            _syntaxKinds.ToImmutableSortedDictionary(StringComparer.Ordinal),
            unobservedOperationKinds,
            actionableUnobservedOperationKinds,
            _samplerMode,
            manifestSurfaceCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            manifestClassificationCounts,
            registryExpectationCounts,
            registryExpectationCounts[FuzzExpectation.ProvenBucket] +
            registryExpectationCounts[FuzzExpectation.DisprovenBucket],
            conservativeRegistryFamilies.Length,
            conservativeRegistryFamilies,
            RoslynShapeManifest.GeneratorBackedShapeIds.Length,
            registryCoveredShapeIds.Count,
            unobservedGeneratorBackedShapes,
            _primaryShapeCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            findings);
    }

    private static ImmutableSortedDictionary<string, int> CreateRegistryExpectationCounts() {
        var buckets = new[] {
            FuzzExpectation.ConservativeBucket,
            FuzzExpectation.DisprovenBucket,
            FuzzExpectation.ProvenBucket
        };
        return buckets.ToImmutableSortedDictionary(
            static bucket => bucket,
            static bucket => FuzzCaseGenerator.RegistryEntries.Count(entry => entry.Expectation.Bucket == bucket),
            StringComparer.Ordinal);
    }

    private int DiagnosticCount(string id) => _diagnosticCounts.GetValueOrDefault(id);

    private void AddFinding(string normalizedSourceHash, FuzzFinding finding) {
        var aggregationKey = normalizedSourceHash + "|" + finding.Identity;
        if (_findingIndices.TryGetValue(aggregationKey, out var index)) {
            var existing = _findings[index];
            _findings[index] = existing with {
                OccurrenceCount = existing.OccurrenceCount + finding.OccurrenceCount,
                SourcePath = existing.SourcePath ?? finding.SourcePath
            };
            return;
        }

        _findingIndices.Add(aggregationKey, _findings.Count);
        _findings.Add(finding);
    }

    private static void AddAll(SortedDictionary<string, int> target, IReadOnlyDictionary<string, int> source) {
        foreach (var pair in source)
            target[pair.Key] = target.TryGetValue(pair.Key, out var count) ? count + pair.Value : pair.Value;
    }

}
