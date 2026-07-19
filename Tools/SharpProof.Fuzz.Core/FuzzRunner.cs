namespace SharpProof.Tools.Fuzz;

public static class FuzzRunner
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private static readonly Lazy<ImmutableArray<MetadataReference>> MetadataReferences =
        new(CreateMetadataReferences);

    private static readonly CSharpCompilationOptions SafeCompilationOptions =
        CreateCompilationOptions(false);

    private static readonly CSharpCompilationOptions UnsafeCompilationOptions =
        CreateCompilationOptions(true);

    private static readonly AnalyzerOptions SharedAnalyzerOptions =
        new(
            ImmutableArray<AdditionalText>.Empty,
            new FixedAnalyzerConfigOptionsProvider(
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true")));

    private static readonly ImmutableArray<DiagnosticAnalyzer> SharedAnalyzers =
        ImmutableArray.Create<DiagnosticAnalyzer>(new SharpProofAnalyzer());

    private static readonly CompilationWithAnalyzersOptions SharedCompilationWithAnalyzersOptions =
        new(
            SharedAnalyzerOptions,
            null,
            true,
            false,
            false);

    private static readonly Regex GeneratedTypeNameRegex =
        new(@"\bI?FuzzCase\d+_[A-Za-z0-9_]+(?:Value)?\b", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static AnalyzerRunResult EmptyAnalyzerRun =>
        new(ImmutableArray<Diagnostic>.Empty, ImmutableArray<string>.Empty);

    public static async Task<FuzzRunSummary> RunAsync(FuzzOptions options,
        CancellationToken cancellationToken = default)
    {
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

    private static DateTimeOffset CreateDeadline(DateTimeOffset startedUtc, TimeSpan duration)
    {
        var maxDuration = DateTimeOffset.MaxValue - startedUtc;
        return duration >= maxDuration
            ? DateTimeOffset.MaxValue
            : startedUtc + duration;
    }

    internal static async Task<FuzzRunSummary> RunCoreAsync(
        FuzzOptions options,
        DateTimeOffset startedUtc,
        Func<int, FuzzCase> createCase,
        string samplerMode,
        int? maxIterations,
        DateTimeOffset? deadline,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var interestingDirectory = Path.Combine(options.OutputDirectory, "interesting-cases");
        Directory.CreateDirectory(interestingDirectory);

        var stopwatch = Stopwatch.StartNew();
        var builder = new FuzzRunSummaryBuilder(options, startedUtc, samplerMode);
        var savedInterestingCases = 0;
        var savedInterestingCaseKeys = new HashSet<string>(StringComparer.Ordinal);
        var savedInterestingCasesByFamily = new Dictionary<string, int>(StringComparer.Ordinal);
        var nextCheckpointAt = options.CheckpointEvery > 0 ? options.CheckpointEvery : int.MaxValue;

        while (!cancellationToken.IsCancellationRequested)
        {
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
            var analyses =
                await AnalyzeCasesCoreAsync(cases, options.RepeatAnalyzer, options.Parallelism, cancellationToken);

            foreach (var analysis in analyses)
            {
                var updatedAnalysis = analysis;
                if (analysis.Findings.Length > 0)
                {
                    var interestingCaseKey = CreateInterestingCaseKey(analysis);
                    var savedForFamily = savedInterestingCasesByFamily.TryGetValue(analysis.Case.Family, out var count)
                        ? count
                        : 0;
                    if (savedInterestingCases < options.MaxInterestingCases &&
                        savedForFamily < options.MaxInterestingCasesPerFamily &&
                        savedInterestingCaseKeys.Add(interestingCaseKey))
                    {
                        var fileName =
                            $"{savedInterestingCases + 1:0000}-{SanitizeFileName(analysis.Case.Family)}-{analysis.NormalizedSourceHash[..12]}.cs";
                        var sourcePath = Path.Combine(interestingDirectory, fileName);
                        await File.WriteAllTextAsync(sourcePath, analysis.Case.Source, cancellationToken);
                        updatedAnalysis = analysis with
                        {
                            Findings = analysis.Findings
                                .Select(finding => finding with { SourcePath = sourcePath })
                                .ToImmutableArray()
                        };
                        savedInterestingCases++;
                        savedInterestingCasesByFamily[analysis.Case.Family] = savedForFamily + 1;
                    }
                }

                builder.Add(updatedAnalysis);

                if (options.CheckpointEvery > 0 && builder.CasesAnalyzed >= nextCheckpointAt)
                {
                    var checkpointSummary = builder.Build(DateTimeOffset.UtcNow, stopwatch.Elapsed,
                        options.OutputDirectory, savedInterestingCases);
                    await WriteArtifactsAsync(checkpointSummary, options.OutputDirectory, true, cancellationToken);
                    while (builder.CasesAnalyzed >= nextCheckpointAt) nextCheckpointAt += options.CheckpointEvery;
                }
            }
        }

        stopwatch.Stop();
        var summary = builder.Build(DateTimeOffset.UtcNow, stopwatch.Elapsed, options.OutputDirectory,
            savedInterestingCases);
        await WriteArtifactsAsync(summary, options.OutputDirectory, false, cancellationToken);
        return summary;
    }

    internal static async Task<ImmutableArray<FuzzCaseAnalysis>> AnalyzeCasesCoreAsync(
        ImmutableArray<FuzzCase> fuzzCases,
        bool repeatAnalyzer,
        int parallelism,
        CancellationToken cancellationToken)
    {
        var analyses = new FuzzCaseAnalysis[fuzzCases.Length];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, fuzzCases.Length),
            parallelOptions,
            async (index, ct) => { analyses[index] = await AnalyzeCaseAsync(fuzzCases[index], repeatAnalyzer, ct); });

        return analyses.ToImmutableArray();
    }

    public static async Task<FuzzCaseAnalysis> AnalyzeCaseAsync(
        FuzzCase fuzzCase,
        bool repeatAnalyzer = true,
        CancellationToken cancellationToken = default)
    {
        var syntaxTree =
            CSharpSyntaxTree.ParseText(fuzzCase.Source, ParseOptions, cancellationToken: cancellationToken);
        var compilation = CreateCompilation(fuzzCase.Name, syntaxTree, fuzzCase.AllowUnsafe);
        var normalizedSourceHash = ComputeStableHash(NormalizeSource(fuzzCase.Source));
        var compilerErrors = compilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToImmutableArray();

        if (compilerErrors.Length > 0)
            return new FuzzCaseAnalysis(
                fuzzCase,
                ImmutableSortedDictionary<string, int>.Empty,
                CollectSyntaxKinds(syntaxTree),
                ImmutableArray<Diagnostic>.Empty,
                ImmutableArray<string>.Empty,
                compilerErrors,
                normalizedSourceHash,
                ImmutableArray.Create(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "compilation_error",
                    "Generated source did not compile.",
                    null,
                    compilerErrors)));

        var operationKinds = CollectOperationKinds(compilation, syntaxTree, cancellationToken);
        var syntaxKinds = CollectSyntaxKinds(syntaxTree);
        var firstDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation, cancellationToken);
        var findings = Evaluate(fuzzCase, firstDiagnostics.Diagnostics, firstDiagnostics.Exceptions);
        var diagnosticSignatures = ToDiagnosticSignatures(firstDiagnostics.Diagnostics);

        if (repeatAnalyzer)
        {
            var secondDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation, cancellationToken);
            var secondDiagnosticSignatures = ToDiagnosticSignatures(secondDiagnostics.Diagnostics);
            var diagnosticsDiffer =
                !diagnosticSignatures.SequenceEqual(secondDiagnosticSignatures, StringComparer.Ordinal);
            var exceptionsDiffer = !firstDiagnostics.Exceptions.SequenceEqual(
                secondDiagnostics.Exceptions,
                StringComparer.Ordinal);
            if (diagnosticsDiffer || exceptionsDiffer)
            {
                var secondFindings = Evaluate(
                    fuzzCase,
                    secondDiagnostics.Diagnostics,
                    secondDiagnostics.Exceptions);
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
                    diagnosticSignatures
                        .Concat(secondDiagnosticSignatures)
                        .Concat(firstDiagnostics.Exceptions)
                        .Concat(secondDiagnostics.Exceptions)
                        .ToImmutableArray()));
            }

            foreach (var exception in secondDiagnostics.Exceptions)
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "analyzer_exception",
                    exception,
                    null,
                    ImmutableArray<string>.Empty));
        }

        AddMissingExpectedShapeFindings(
            fuzzCase,
            operationKinds,
            syntaxKinds,
            findings);

        return new FuzzCaseAnalysis(
            fuzzCase,
            operationKinds,
            syntaxKinds,
            firstDiagnostics.Diagnostics,
            diagnosticSignatures,
            ImmutableArray<string>.Empty,
            normalizedSourceHash,
            findings.ToImmutable());
    }

    private static void AddMissingExpectedShapeFindings(
        FuzzCase fuzzCase,
        IReadOnlyDictionary<string, int> operationKinds,
        IReadOnlyDictionary<string, int> syntaxKinds,
        ImmutableArray<FuzzFinding>.Builder findings)
    {
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
            string description)
        {
            if (expectedKinds.IsDefaultOrEmpty) return;

            foreach (var expectedKind in expectedKinds.Distinct(StringComparer.Ordinal))
                if (!observedKinds.ContainsKey(expectedKind))
                    findings.Add(new FuzzFinding(
                        fuzzCase.Name,
                        fuzzCase.Family,
                        category,
                        description,
                        null,
                        ImmutableArray.Create(expectedKind)));
        }
    }

    private static async Task<AnalyzerRunResult> GetAnalyzerDiagnosticsAsync(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        try
        {
            var compilationWithAnalyzers = compilation.WithAnalyzers(
                SharedAnalyzers,
                SharedCompilationWithAnalyzersOptions);

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
            return new AnalyzerRunResult(diagnostics, ImmutableArray<string>.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return EmptyAnalyzerRun with { Exceptions = ImmutableArray.Create(ex.ToString()) };
        }
    }

    internal static ImmutableArray<FuzzFinding>.Builder Evaluate(
        FuzzCase fuzzCase,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArray<string> analyzerExceptions)
    {
        var findings = ImmutableArray.CreateBuilder<FuzzFinding>();
        foreach (var exception in analyzerExceptions)
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "analyzer_exception",
                exception,
                null,
                ImmutableArray<string>.Empty));

        if (!analyzerExceptions.IsEmpty) return findings;

        var sp0002Diagnostics = EvaluateDiagnosticExpectation(
            fuzzCase,
            diagnostics,
            new DiagnosticExpectationPolicy(
                SharpProofDiagnostics.PurityNotVerifiedId,
                fuzzCase.Expectation.Sp0002 == Sp0002ExpectationKind.MustNotEmit,
                fuzzCase.Expectation.Sp0002 == Sp0002ExpectationKind.MustEmit,
                true,
                fuzzCase.Expectation.RequiredSp0002Properties,
                "pure_sp0002",
                "A definitely-pure generated case produced SP0002.",
                "impure_missing_sp0002",
                "A definitely-impure generated case did not produce SP0002.",
                "missing_sp0002_evidence",
                "SP0002 did not include stable category/rule/operation evidence."),
            findings);

        foreach (var diagnostic in sp0002Diagnostics)
        {
            if (fuzzCase.Expectation.Sp0002 == Sp0002ExpectationKind.MustNotEmit &&
                diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ImpurityCategoryProperty, out var category) &&
                string.Equals(category, "unsupported_operation", StringComparison.Ordinal))
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "pure_unsupported_operation",
                    "A definitely-pure generated case hit unsupported_operation.",
                    null,
                    ImmutableArray.Create(ToDiagnosticSignature(diagnostic))));
        }

        var sp0010Diagnostics = EvaluateDiagnosticExpectation(
            fuzzCase,
            diagnostics,
            new DiagnosticExpectationPolicy(
                SharpProofDiagnostics.ExceptionSummaryId,
                fuzzCase.Expectation.Sp0010 == Sp0010ExpectationKind.MustNotEmit,
                fuzzCase.Expectation.Sp0010 == Sp0010ExpectationKind.MustEmit,
                fuzzCase.Expectation.Sp0010 != Sp0010ExpectationKind.Ignore,
                fuzzCase.Expectation.RequiredSp0010Properties,
                "unexpected_sp0010",
                "A generated case unexpectedly produced SP0010.",
                "missing_sp0010",
                "A generated case expected to produce SP0010 did not do so.",
                "missing_sp0010_evidence",
                "SP0010 did not include stable exception evidence."),
            findings);

        if (fuzzCase.Expectation.Sp0010 != Sp0010ExpectationKind.Ignore)
        {
            if (!fuzzCase.Expectation.RequiredAnySp0010Properties.IsDefaultOrEmpty &&
                sp0010Diagnostics.Length > 0 &&
                !sp0010Diagnostics.Any(diagnostic =>
                    !MissingAnyRequiredProperties(diagnostic, fuzzCase.Expectation.RequiredAnySp0010Properties)))
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "missing_sp0010_edge_evidence",
                    "No emitted SP0010 included the expected additive exception-edge evidence.",
                    null,
                    ToDiagnosticSignatures(sp0010Diagnostics)));
        }

        return findings;
    }

    private static ImmutableArray<Diagnostic> EvaluateDiagnosticExpectation(
        FuzzCase fuzzCase,
        ImmutableArray<Diagnostic> diagnostics,
        DiagnosticExpectationPolicy policy,
        ImmutableArray<FuzzFinding>.Builder findings)
    {
        var matchingDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == policy.DiagnosticId)
            .ToImmutableArray();

        if (policy.MustNotEmit && !matchingDiagnostics.IsEmpty)
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                policy.UnexpectedCategory,
                policy.UnexpectedDescription,
                null,
                ToDiagnosticSignatures(matchingDiagnostics)));

        if (policy.MustEmit && matchingDiagnostics.IsEmpty)
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                policy.MissingCategory,
                policy.MissingDescription,
                null,
                ToDiagnosticSignatures(diagnostics)));

        if (policy.ValidateEvidence)
            foreach (var diagnostic in matchingDiagnostics)
                if (MissingAnyRequiredProperties(diagnostic, policy.RequiredProperties))
                    findings.Add(new FuzzFinding(
                        fuzzCase.Name,
                        fuzzCase.Family,
                        policy.MissingEvidenceCategory,
                        policy.MissingEvidenceDescription,
                        null,
                        ImmutableArray.Create(ToDiagnosticSignature(diagnostic))));

        return matchingDiagnostics;
    }

    private static bool MissingAnyRequiredProperties(Diagnostic diagnostic, ImmutableArray<string> keys)
    {
        foreach (var key in keys)
            if (MissingProperty(diagnostic, key))
                return true;

        return false;
    }

    private static bool MissingProperty(Diagnostic diagnostic, string key)
    {
        return !diagnostic.Properties.TryGetValue(key, out var value) ||
               string.IsNullOrWhiteSpace(value);
    }

    private static ImmutableSortedDictionary<string, int> CollectOperationKinds(
        Compilation compilation,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken)
    {
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        var roots = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
        foreach (var node in syntaxTree.GetRoot(cancellationToken).DescendantNodes())
        {
            var operation = semanticModel.GetOperation(node, cancellationToken);
            if (operation is null) continue;

            while (operation.Parent != null) operation = operation.Parent;
            if (!roots.Add(operation)) continue;

            foreach (var descendant in operation.DescendantsAndSelf())
                counts.Increment(descendant.Kind.ToString());
        }

        return counts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static ImmutableSortedDictionary<string, int> CollectSyntaxKinds(SyntaxTree syntaxTree)
    {
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
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GetMetadataReferences(),
            allowUnsafe ? UnsafeCompilationOptions : SafeCompilationOptions);
    }

    private static ImmutableArray<MetadataReference> GetMetadataReferences()
    {
        return MetadataReferences.Value;
    }

    private static CSharpCompilationOptions CreateCompilationOptions(bool allowUnsafe)
    {
        return new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: allowUnsafe,
            nullableContextOptions: NullableContextOptions.Enable);
    }

    private static ImmutableArray<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES was not available.");

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Append(typeof(EnforcePureAttribute).Assembly.Location)
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(group => (MetadataReference)MetadataReference.CreateFromFile(group.Key))
            .ToImmutableArray();
    }

    private static ImmutableArray<string> ToDiagnosticSignatures(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics
            .Select(ToDiagnosticSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string ToDiagnosticSignature(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        var character = lineSpan.StartLinePosition.Character + 1;
        var properties = string.Join(
            ";",
            diagnostic.Properties
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));

        return $"{diagnostic.Id}|{line}:{character}|{diagnostic.GetMessage()}|{properties}";
    }

    private static string CreateCoverageJson(FuzzRunSummary summary)
    {
        var coverage = new
        {
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
        CancellationToken cancellationToken)
    {
        var summaryPath = Path.Combine(outputDirectory, isPartial ? "summary.partial.json" : "summary.json");
        var coveragePath = Path.Combine(outputDirectory, isPartial ? "coverage.partial.json" : "coverage.json");
        var summaryJson = JsonSerializer.Serialize(summary, JsonOptions);
        await File.WriteAllTextAsync(summaryPath, summaryJson, cancellationToken);
        await File.WriteAllTextAsync(coveragePath, CreateCoverageJson(summary), cancellationToken);
        if (!isPartial)
        {
            File.Delete(Path.Combine(outputDirectory, "summary.partial.json"));
            File.Delete(Path.Combine(outputDirectory, "coverage.partial.json"));
        }
    }

    private static string CreateInterestingCaseKey(FuzzCaseAnalysis analysis)
    {
        var findingIdentity = string.Join(
            "||",
            analysis.Findings
                .OrderBy(finding => finding.Category, StringComparer.Ordinal)
                .ThenBy(finding => finding.Description, StringComparer.Ordinal)
                .Select(static finding => finding.Identity));

        return analysis.NormalizedSourceHash + "|" + findingIdentity;
    }

    private static string NormalizeSource(string source)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return GeneratedTypeNameRegex.Replace(normalized, "GeneratedTypeX");
    }

    private static string ComputeStableHash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToImmutableHashSet();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private readonly record struct DiagnosticExpectationPolicy(
        string DiagnosticId,
        bool MustNotEmit,
        bool MustEmit,
        bool ValidateEvidence,
        ImmutableArray<string> RequiredProperties,
        string UnexpectedCategory,
        string UnexpectedDescription,
        string MissingCategory,
        string MissingDescription,
        string MissingEvidenceCategory,
        string MissingEvidenceDescription);
}
