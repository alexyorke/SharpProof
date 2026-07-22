namespace SharpProof.Tools.Fuzz;
public static class FuzzRunner {
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
    private static readonly Lazy<ImmutableArray<MetadataReference>> MetadataReferences =
        new(CreateMetadataReferences);
    private static readonly CSharpCompilationOptions SafeCompilationOptions =
        CreateCompilationOptions(false);
    private static readonly CSharpCompilationOptions UnsafeCompilationOptions =
        CreateCompilationOptions(true);
    private static readonly AnalyzerOptions SharedAnalyzerOptions =
        new([], new FixedAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string>.Empty));
    private static readonly ImmutableArray<DiagnosticAnalyzer> SharedAnalyzers =
        [new SharpProofAnalyzer()];
    private static readonly CompilationWithAnalyzersOptions SharedCompilationWithAnalyzersOptions =
        new(SharedAnalyzerOptions, null, true, false, false);
    private static readonly Regex GeneratedTypeNameRegex =
        new(@"\bI?FuzzCase\d+_[A-Za-z0-9_]+(?:Value)?\b", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static async Task<FuzzRunSummary> RunAsync(FuzzOptions options, CancellationToken cancellationToken = default) {
        var startedUtc = DateTimeOffset.UtcNow;
        var generator = new FuzzCaseGenerator(options.Seed);
        var maxIterations = options.Iterations is > 0 ? options.Iterations.Value : (int?)null;
        var deadline = options.Duration is { } duration ? CreateDeadline(startedUtc, duration) : (DateTimeOffset?)null;
        return await RunCoreAsync(
            options,
            startedUtc,
            index => generator.Next(index),
            "deterministic_shape_stratified",
            maxIterations,
            deadline,
            cancellationToken);
    }
    private static DateTimeOffset CreateDeadline(DateTimeOffset startedUtc, TimeSpan duration) =>
        duration >= DateTimeOffset.MaxValue - startedUtc
            ? DateTimeOffset.MaxValue
            : startedUtc + duration;
    internal static async Task<FuzzRunSummary> RunCoreAsync(
        FuzzOptions options,
        DateTimeOffset startedUtc,
        Func<int, FuzzCase> createCase,
        string samplerMode,
        int? maxIterations,
        DateTimeOffset? deadline,
        CancellationToken cancellationToken) {
        Directory.CreateDirectory(options.OutputDirectory);
        var interestingDirectory = Path.Combine(options.OutputDirectory, "interesting-cases");
        Directory.CreateDirectory(interestingDirectory);
        var stopwatch = Stopwatch.StartNew();
        var builder = new FuzzRunSummaryBuilder(options, startedUtc, samplerMode);
        var savedInterestingCases = 0;
        var savedInterestingCaseKeys = new HashSet<string>(StringComparer.Ordinal);
        var savedInterestingCasesByFamily = new Dictionary<string, int>(StringComparer.Ordinal);
        var nextCheckpointAt = options.CheckpointEvery > 0 ? options.CheckpointEvery : int.MaxValue;
        while (!cancellationToken.IsCancellationRequested) {
            if (maxIterations is { } max && builder.CasesAnalyzed >= max) break;
            if (deadline is { } end && DateTimeOffset.UtcNow >= end) break;
            var remainingCases = maxIterations is { } maximum
                ? maximum - builder.CasesAnalyzed
                : options.Parallelism * 8;
            var batchSize = Math.Max(1, Math.Min(options.Parallelism * 8, remainingCases));
            var startIndex = builder.CasesAnalyzed;
            var cases = Enumerable.Range(startIndex, batchSize)
                .Select(createCase)
                .ToImmutableArray();
            ImmutableArray<FuzzCaseAnalysis> analyses;
            using (var deadlineCancellation = CreateDeadlineCancellation(deadline, cancellationToken)) {
                try {
                    analyses = await AnalyzeCasesCoreAsync(
                        cases,
                        options.RepeatAnalyzer,
                        options.Parallelism,
                        deadlineCancellation?.Token ?? cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                                                         deadline is { } batchEnd &&
                                                         DateTimeOffset.UtcNow >= batchEnd) {
                    break;
                }
            }
            foreach (var analysis in analyses) {
                var updatedAnalysis = analysis;
                if (analysis.Findings.Length > 0) {
                    var interestingCaseKey = CreateInterestingCaseKey(analysis);
                    var savedForFamily = savedInterestingCasesByFamily.TryGetValue(analysis.Case.Family, out var count)
                        ? count
                        : 0;
                    if (savedInterestingCases < options.MaxInterestingCases &&
                        savedForFamily < options.MaxInterestingCasesPerFamily &&
                        savedInterestingCaseKeys.Add(interestingCaseKey)) {
                        var fileName =
                            $"{savedInterestingCases + 1:0000}-{SanitizeFileName(analysis.Case.Family)}-{analysis.NormalizedSourceHash[..12]}.cs";
                        var sourcePath = Path.Combine(interestingDirectory, fileName);
                        await File.WriteAllTextAsync(sourcePath, analysis.Case.Source, cancellationToken);
                        updatedAnalysis = analysis with {
                            Findings = [.. analysis.Findings.Select(finding => finding with { SourcePath = sourcePath })]
                        };
                        savedInterestingCases++;
                        savedInterestingCasesByFamily[analysis.Case.Family] = savedForFamily + 1;
                    }
                }
                builder.Add(updatedAnalysis);
                if (options.CheckpointEvery > 0 && builder.CasesAnalyzed >= nextCheckpointAt) {
                    var checkpointSummary = builder.Build(DateTimeOffset.UtcNow, stopwatch.Elapsed,
                        options.OutputDirectory, savedInterestingCases);
                    await WriteArtifactsAsync(checkpointSummary, options.OutputDirectory, true, cancellationToken);
                    while (builder.CasesAnalyzed >= nextCheckpointAt) nextCheckpointAt += options.CheckpointEvery;
                }
            }
        }
        stopwatch.Stop();
        var summary = builder.Build(DateTimeOffset.UtcNow, stopwatch.Elapsed, options.OutputDirectory, savedInterestingCases);
        await WriteArtifactsAsync(summary, options.OutputDirectory, false, cancellationToken);
        return summary;
    }
    internal static CancellationTokenSource? CreateDeadlineCancellation(
        DateTimeOffset? deadline,
        CancellationToken userCancellationToken) {
        if (deadline == null) return null;
        var remaining = deadline.Value - DateTimeOffset.UtcNow;
        var source = CancellationTokenSource.CreateLinkedTokenSource(userCancellationToken);
        if (remaining <= TimeSpan.Zero) {
            source.Cancel();
            return source;
        }
        var maximumDelay = TimeSpan.FromMilliseconds(int.MaxValue);
        source.CancelAfter(remaining > maximumDelay ? maximumDelay : remaining);
        return source;
    }
    internal static async Task<ImmutableArray<FuzzCaseAnalysis>> AnalyzeCasesCoreAsync(
        ImmutableArray<FuzzCase> fuzzCases,
        bool repeatAnalyzer,
        int parallelism,
        CancellationToken cancellationToken) {
        var analyses = new FuzzCaseAnalysis[fuzzCases.Length];
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken
        };
        await Parallel.ForEachAsync(
            Enumerable.Range(0, fuzzCases.Length),
            parallelOptions,
            async (index, ct) => analyses[index] = await AnalyzeCaseAsync(fuzzCases[index], repeatAnalyzer, ct));
        return [.. analyses];
    }
    public static async Task<FuzzCaseAnalysis> AnalyzeCaseAsync(
        FuzzCase fuzzCase,
        bool repeatAnalyzer = true,
        CancellationToken cancellationToken = default) {
        var syntaxTree =
            CSharpSyntaxTree.ParseText(fuzzCase.Source, ParseOptions, cancellationToken: cancellationToken);
        var compilation = CreateCompilation(fuzzCase.Name, syntaxTree, fuzzCase.AllowUnsafe);
        var normalizedSourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeSource(fuzzCase.Source))));
        var compilerErrors = compilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToImmutableArray();
        if (compilerErrors.Length > 0)
            return new FuzzCaseAnalysis(
                fuzzCase,
                ImmutableSortedDictionary<string, int>.Empty,
                CollectSyntaxKinds(syntaxTree),
                [],
                null,
                [],
                compilerErrors,
                normalizedSourceHash,
                [new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "compilation_error",
                    "Generated source did not compile.",
                    null,
                    compilerErrors)]);
        var operationKinds = CollectOperationKinds(compilation, syntaxTree, cancellationToken);
        var syntaxKinds = CollectSyntaxKinds(syntaxTree);
        var effects = AnalyzeEffects(fuzzCase, syntaxTree, cancellationToken);
        var firstDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation, cancellationToken);
        var findings = Evaluate(fuzzCase, effects, firstDiagnostics.Diagnostics, firstDiagnostics.Exceptions);
        var diagnosticSignatures = ToDiagnosticSignatures(firstDiagnostics.Diagnostics);
        if (repeatAnalyzer) {
            var secondDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation, cancellationToken);
            var secondDiagnosticSignatures = ToDiagnosticSignatures(secondDiagnostics.Diagnostics);
            var diagnosticsDiffer =
                !diagnosticSignatures.SequenceEqual(secondDiagnosticSignatures, StringComparer.Ordinal);
            var exceptionsDiffer = !firstDiagnostics.Exceptions.SequenceEqual(secondDiagnostics.Exceptions, StringComparer.Ordinal);
            if (diagnosticsDiffer || exceptionsDiffer) {
                var secondFindings = Evaluate(fuzzCase, effects, secondDiagnostics.Diagnostics, secondDiagnostics.Exceptions);
                var stableFindings = findings
                    .Where(first => secondFindings.Any(second =>
                        string.Equals(first.Category, second.Category, StringComparison.Ordinal) &&
                        string.Equals(first.Description, second.Description, StringComparison.Ordinal)))
                    .ToArray();
                findings.Clear();
                findings.AddRange(stableFindings);
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "nondeterministic_diagnostics",
                    "Repeated analyzer runs produced different diagnostics or analyzer exceptions.",
                    null,
                    [.. diagnosticSignatures
, .. secondDiagnosticSignatures, .. firstDiagnostics.Exceptions, .. secondDiagnostics.Exceptions]));
            }
        }
        AddMissingExpectedShapeFindings(fuzzCase, operationKinds, syntaxKinds, findings);
        return new FuzzCaseAnalysis(
            fuzzCase,
            operationKinds,
            syntaxKinds,
            firstDiagnostics.Diagnostics,
            effects,
            diagnosticSignatures,
            [],
            normalizedSourceHash,
            findings.ToImmutable());
    }
    private static void AddMissingExpectedShapeFindings(
        FuzzCase fuzzCase,
        IReadOnlyDictionary<string, int> operationKinds,
        IReadOnlyDictionary<string, int> syntaxKinds,
        ImmutableArray<FuzzFinding>.Builder findings) {
        AddMissing(
            fuzzCase.ExpectedOperationKinds,
            operationKinds,
            "missing_expected_operation_kind",
            "Generated source did not contain an operation kind declared by its registry entry.");
        AddMissing(
            fuzzCase.ExpectedSyntaxKinds,
            syntaxKinds,
            "missing_expected_syntax_kind",
            "Generated source did not contain a syntax kind declared by its registry entry.");
        return;
        void AddMissing(
            ImmutableArray<string> expectedKinds,
            IReadOnlyDictionary<string, int> observedKinds,
            string category,
            string description) {
            if (expectedKinds.IsDefaultOrEmpty) return;
            foreach (var expectedKind in expectedKinds.Distinct(StringComparer.Ordinal))
                if (!observedKinds.ContainsKey(expectedKind))
                    findings.Add(new FuzzFinding(
                        fuzzCase.Name,
                        fuzzCase.Family,
                        category,
                        description,
                        null,
                        [expectedKind]));
        }
    }
    private static async Task<AnalyzerRunResult> GetAnalyzerDiagnosticsAsync(Compilation compilation, CancellationToken cancellationToken) {
        try {
            var compilationWithAnalyzers = compilation.WithAnalyzers(SharedAnalyzers, SharedCompilationWithAnalyzersOptions);
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
            return new AnalyzerRunResult(diagnostics, []);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            return new AnalyzerRunResult([], [ex.ToString()]);
        }
    }
    internal static ImmutableArray<FuzzFinding>.Builder Evaluate(
        FuzzCase fuzzCase,
        SharpProofAnalysisResult effects,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArray<string> analyzerExceptions) {
        var findings = ImmutableArray.CreateBuilder<FuzzFinding>();
        foreach (var exception in analyzerExceptions.Distinct(StringComparer.Ordinal))
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "analyzer_exception",
                exception,
                null,
                []));
        if (!analyzerExceptions.IsEmpty) return findings;
        if (effects.Status is SharpProofQueryStatus.Failed or SharpProofQueryStatus.Canceled ||
            effects.MethodEffects == null) {
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "symbolic_analysis_failure",
                effects.Error?.Message ?? "Symbolic analysis did not return method effects.",
                null,
                effects.Error == null
                    ? ["status=" + effects.Status]
                    : ["status=" + effects.Status, "error=" + effects.Error.Code]));
            return findings;
        }
        EvaluateEffectExpectation(fuzzCase, effects, diagnostics, findings);
        return findings;
    }
    private static SharpProofAnalysisResult AnalyzeEffects(FuzzCase fuzzCase, SyntaxTree syntaxTree, CancellationToken cancellationToken) {
        var method = syntaxTree.GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(node => node.Identifier.ValueText == "TestMethod");
        SyntaxNode? targetNode = method.Body?.Statements.FirstOrDefault();
        targetNode ??= method.ExpressionBody?.Expression;
        targetNode ??= method;
        var linePosition = syntaxTree.GetLineSpan(targetNode.Span, cancellationToken).StartLinePosition;
        using var session = SharpProofAnalysisSession.FromText(fuzzCase.Source, fuzzCase.Name + ".cs");
        return session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: linePosition.Line + 1, Column: linePosition.Character + 1),
            fuzzCase.Expectation.ProofCondition == null
                ? SharpProofAnalysisFacet.Effects
                : SharpProofAnalysisFacet.Effects | SharpProofAnalysisFacet.ProofFacts,
            fuzzCase.Expectation.ProofCondition), cancellationToken);
    }
    private static void EvaluateEffectExpectation(
        FuzzCase fuzzCase,
        SharpProofAnalysisResult result,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArray<FuzzFinding>.Builder findings) {
        if (result.MethodEffects!.Purity != fuzzCase.Expectation.PurityVerdict)
            Add(
                "unexpected_purity_verdict",
                $"Expected {fuzzCase.Expectation.PurityVerdict}, observed {result.MethodEffects.Purity}.",
                [.. result.UnknownReasons.Select(static reason => reason.Category + ":" + reason.Code)]);
        var observed = result.MethodEffects?.Effects ?? SharpProofEffect.None;
        foreach (var expected in fuzzCase.Expectation.RequiredEffects)
            if ((observed & expected) != expected)
                Add(
                    "missing_expected_effect",
                    "Expected effect was not observed: " + expected,
                    ["observed=" + observed]);
        foreach (var forbidden in fuzzCase.Expectation.ForbiddenEffects)
            if ((observed & forbidden) != 0)
                Add("unexpected_effect", "Forbidden effect was observed: " + forbidden, ["observed=" + observed]);
        foreach (var category in fuzzCase.Expectation.RequiredUnknownCategories)
            if (!result.UnknownReasons.Any(reason => string.Equals(reason.Category, category, StringComparison.Ordinal)))
                Add(
                    "missing_unknown_reason",
                    "Expected unknown category was not observed: " + category,
                    [.. result.UnknownReasons.Select(static reason => reason.Category)]);
        foreach (var diagnosticId in fuzzCase.Expectation.RequiredDiagnosticIds)
            if (!diagnostics.Any(diagnostic => diagnostic.Id == diagnosticId))
                Add(
                    "missing_expected_diagnostic",
                    "Expected diagnostic was not observed: " + diagnosticId,
                    ToDiagnosticSignatures(diagnostics));
        if (fuzzCase.Expectation.ProofStatus != null) {
            var proof = result.ProofFacts.SingleOrDefault();
            if (proof == null || proof.Status != fuzzCase.Expectation.ProofStatus)
                Add(
                    "unexpected_proof_status",
                    $"Expected {fuzzCase.Expectation.ProofStatus}, observed {proof?.Status ?? "missing"}.",
                    [.. result.ProofFacts.Select(static fact => fact.Condition + ":" + fact.Status)]);
            else if (fuzzCase.Expectation.RequireCounterexample && proof.Counterexample == null)
                Add(
                    "missing_counterexample",
                    "The expected compact Z3 counterexample was not produced.",
                    [proof.Reason]);
        }
        var enforcePureFailure = diagnostics.Any(static diagnostic => diagnostic.Id == "SP0002");
        if ((result.MethodEffects!.Purity == SharpProofVerdict.Proven) == enforcePureFailure)
            Add(
                "enforce_pure_projection_mismatch",
                "[EnforcePure] diagnostic did not match the canonical purity verdict.",
                ["verdict=" + result.MethodEffects.Purity, "diagnostic=" + enforcePureFailure]);
        return;
        void Add(string category, string description, ImmutableArray<string> details) =>
            findings.Add(new FuzzFinding(fuzzCase.Name, fuzzCase.Family, category, description, null, details));
    }
    private static ImmutableSortedDictionary<string, int> CollectOperationKinds(
        Compilation compilation,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken) {
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var roots = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
        foreach (var node in syntaxTree.GetRoot(cancellationToken).DescendantNodes()) {
            var operation = semanticModel.GetOperation(node, cancellationToken);
            if (operation is null) continue;
            while (operation.Parent != null) operation = operation.Parent;
            if (!roots.Add(operation)) continue;
            foreach (var descendant in operation.DescendantsAndSelf())
                counts.Increment(descendant.Kind.ToString());
        }
        return counts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }
    private static ImmutableSortedDictionary<string, int> CollectSyntaxKinds(SyntaxTree syntaxTree) {
        var root = syntaxTree.GetRoot();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        counts.Increment(((SyntaxKind)root.RawKind).ToString());
        foreach (var nodeOrToken in root.DescendantNodesAndTokens(descendIntoTrivia: true))
            counts.Increment(((SyntaxKind)nodeOrToken.RawKind).ToString());
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true)
                     .Where(static trivia => !trivia.HasStructure))
            counts.Increment(((SyntaxKind)trivia.RawKind).ToString());
        return counts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }
    private static CSharpCompilation CreateCompilation(string assemblyName, SyntaxTree syntaxTree, bool allowUnsafe)
        => CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GetMetadataReferences(),
            allowUnsafe ? UnsafeCompilationOptions : SafeCompilationOptions);
    private static ImmutableArray<MetadataReference> GetMetadataReferences() => MetadataReferences.Value;
    private static CSharpCompilationOptions CreateCompilationOptions(bool allowUnsafe) => new(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: allowUnsafe,
            nullableContextOptions: NullableContextOptions.Enable);
    private static ImmutableArray<MetadataReference> CreateMetadataReferences() {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES was not available.");
        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Append(typeof(EnforcePureAttribute).Assembly.Location)
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(group => (MetadataReference)MetadataReference.CreateFromFile(group.Key))];
    }
    private static ImmutableArray<string> ToDiagnosticSignatures(IEnumerable<Diagnostic> diagnostics) => [.. diagnostics
            .Select(ToDiagnosticSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)];
    private static string ToDiagnosticSignature(Diagnostic diagnostic) {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        var character = lineSpan.StartLinePosition.Character + 1;
        return $"{diagnostic.Id}|{line}:{character}|{diagnostic.GetMessage()}";
    }
    private static string CreateCoverageJson(FuzzRunSummary summary) {
        var coverage = new {
            summary.SchemaVersion,
            summary.Seed,
            summary.CasesAnalyzed,
            summary.FindingCount,
            summary.UniqueFindingCount,
            summary.InterestingCasesSaved,
            summary.OperationKinds,
            summary.SyntaxKinds,
            summary.UnobservedOperationKinds,
            summary.ActionableUnobservedOperationKinds,
            summary.FamilyCounts,
            summary.SamplerMode,
            summary.ManifestSurfaceCounts,
            summary.ManifestClassificationCounts,
            summary.RegistryExpectationCounts,
            summary.DefiniteRegistryFamilyCount,
            summary.ConservativeRegistryFamilyCount,
            summary.ConservativeRegistryFamilies,
            summary.GeneratorBackedShapeCount,
            summary.GeneratorBackedShapesWithRegistryCount,
            summary.UnobservedGeneratorBackedShapes,
            summary.PrimaryShapeCounts
        };
        return JsonSerializer.Serialize(coverage, JsonOptions);
    }
    private static async Task WriteArtifactsAsync(
        FuzzRunSummary summary,
        string outputDirectory,
        bool isPartial,
        CancellationToken cancellationToken) {
        var summaryPath = Path.Combine(outputDirectory, isPartial ? "summary.partial.json" : "summary.json");
        var coveragePath = Path.Combine(outputDirectory, isPartial ? "coverage.partial.json" : "coverage.json");
        var summaryJson = JsonSerializer.Serialize(summary, JsonOptions);
        await File.WriteAllTextAsync(summaryPath, summaryJson, cancellationToken);
        await File.WriteAllTextAsync(coveragePath, CreateCoverageJson(summary), cancellationToken);
        if (!isPartial) {
            File.Delete(Path.Combine(outputDirectory, "summary.partial.json"));
            File.Delete(Path.Combine(outputDirectory, "coverage.partial.json"));
        }
    }
    private static string CreateInterestingCaseKey(FuzzCaseAnalysis analysis) {
        var findingIdentity = string.Join(
            "||",
            analysis.Findings
                .OrderBy(finding => finding.Category, StringComparer.Ordinal)
                .ThenBy(finding => finding.Description, StringComparer.Ordinal)
                .Select(static finding => finding.Identity));
        return analysis.NormalizedSourceHash + "|" + findingIdentity;
    }
    private static string NormalizeSource(string source) => GeneratedTypeNameRegex.Replace(
        source.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(), "GeneratedTypeX");
    private static string SanitizeFileName(string value) => new([.. value.Select(ch =>
        Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)]);
}
