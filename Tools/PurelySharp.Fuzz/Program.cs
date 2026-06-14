using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer;
using PurelySharp.Attributes;

namespace PurelySharp.Tools.Fuzz;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            Console.WriteLine(FuzzOptions.Usage);
            return 0;
        }

        try
        {
            var options = FuzzOptions.Parse(args);
            var summary = await FuzzRunner.RunAsync(options);

            if (!options.Quiet)
            {
                Console.WriteLine($"PurelySharp fuzz run complete: {summary.CasesAnalyzed} cases, {summary.FindingCount} findings ({summary.UniqueFindingCount} unique), {summary.AnalyzerExceptionCount} analyzer exceptions.");
                Console.WriteLine($"Artifacts: {summary.OutputDirectory}");
            }

            return options.FailOnFindings && summary.FindingCount > 0 ? 2 : 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(FuzzOptions.Usage);
            return 64;
        }
    }
}

public sealed record FuzzOptions
{
    public const string Usage = """
Usage: PurelySharp.Fuzz [options]

Options:
  --iterations <n>         Number of generated cases. Use 0 for duration-only runs. Default: 100.
  --seconds <n>            Run duration in seconds.
  --minutes <n>            Run duration in minutes.
  --hours <n>              Run duration in hours.
  --seed <n>               Deterministic random seed. Default: 12345.
  --out <path>             Output directory. Default: artifacts/fuzz/<timestamp>.
  --max-interesting <n>    Maximum source files saved for findings. Default: 100.
  --max-interesting-per-family <n>
                           Maximum saved interesting cases per family. Default: 10.
  --checkpoint-every <n>   Write summary.partial.json and coverage.partial.json every N analyzed cases. Default: 100. Use 0 to disable.
  --parallelism <n>        Maximum concurrent analyzer tasks. Default: 4 or processor count if lower.
  --quiet                  Suppress progress output.
  --fail-on-findings       Exit with code 2 when findings are found.
  --no-repeat              Do not run repeated analyzer determinism checks.
""";

    public int? Iterations { get; init; } = 100;

    public TimeSpan? Duration { get; init; }

    public int Seed { get; init; } = 12345;

    public string OutputDirectory { get; init; } = DefaultOutputDirectory();

    public int MaxInterestingCases { get; init; } = 100;

    public int MaxInterestingCasesPerFamily { get; init; } = 10;

    public int CheckpointEvery { get; init; } = 100;

    public int Parallelism { get; init; } = DefaultParallelism();

    public bool Quiet { get; init; }

    public bool FailOnFindings { get; init; }

    public bool RepeatAnalyzer { get; init; } = true;

    public static FuzzOptions Parse(string[] args)
    {
        var options = new FuzzOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--iterations":
                    options = options with { Iterations = ReadInt(args, ref i, arg) };
                    break;
                case "--seconds":
                    options = options with { Duration = TimeSpan.FromSeconds(ReadDouble(args, ref i, arg)) };
                    break;
                case "--minutes":
                    options = options with { Duration = TimeSpan.FromMinutes(ReadDouble(args, ref i, arg)) };
                    break;
                case "--hours":
                    options = options with { Duration = TimeSpan.FromHours(ReadDouble(args, ref i, arg)) };
                    break;
                case "--seed":
                    options = options with { Seed = ReadInt(args, ref i, arg) };
                    break;
                case "--out":
                    options = options with { OutputDirectory = ReadString(args, ref i, arg) };
                    break;
                case "--max-interesting":
                    options = options with { MaxInterestingCases = ReadInt(args, ref i, arg) };
                    break;
                case "--max-interesting-per-family":
                    options = options with { MaxInterestingCasesPerFamily = ReadInt(args, ref i, arg) };
                    break;
                case "--checkpoint-every":
                    options = options with { CheckpointEvery = ReadInt(args, ref i, arg) };
                    break;
                case "--parallelism":
                    options = options with { Parallelism = ReadInt(args, ref i, arg) };
                    break;
                case "--quiet":
                    options = options with { Quiet = true };
                    break;
                case "--fail-on-findings":
                    options = options with { FailOnFindings = true };
                    break;
                case "--no-repeat":
                    options = options with { RepeatAnalyzer = false };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (options.Iterations < 0)
        {
            throw new ArgumentException("--iterations must be non-negative.");
        }

        if (options.MaxInterestingCases < 0)
        {
            throw new ArgumentException("--max-interesting must be non-negative.");
        }

        if (options.MaxInterestingCasesPerFamily < 0)
        {
            throw new ArgumentException("--max-interesting-per-family must be non-negative.");
        }

        if (options.CheckpointEvery < 0)
        {
            throw new ArgumentException("--checkpoint-every must be non-negative.");
        }

        if (options.Parallelism <= 0)
        {
            throw new ArgumentException("--parallelism must be positive.");
        }

        if (options.Iterations == 0 && options.Duration is null)
        {
            throw new ArgumentException("Duration-only runs need --seconds, --minutes, or --hours when --iterations is 0.");
        }

        return options;
    }

    private static int ReadInt(string[] args, ref int index, string option)
    {
        var value = ReadString(args, ref index, option);
        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"{option} expects an integer.");
    }

    private static double ReadDouble(string[] args, ref int index, string option)
    {
        var value = ReadString(args, ref index, option);
        return double.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException($"{option} expects a non-negative number.");
    }

    private static string ReadString(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} expects a value.");
        }

        index++;
        return args[index];
    }

    private static string DefaultOutputDirectory()
    {
        return Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "fuzz",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
    }

    private static int DefaultParallelism()
    {
        return Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    }
}

public static class FuzzRunner
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    private static readonly Regex GeneratedTypeNameRegex =
        new(@"\bI?FuzzCase\d+_[A-Za-z0-9_]+(?:Value)?\b", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<FuzzRunSummary> RunAsync(FuzzOptions options, CancellationToken cancellationToken = default)
    {
        var startedUtc = DateTimeOffset.UtcNow;
        var generator = new FuzzCaseGenerator(options.Seed);
        var maxIterations = options.Iterations is > 0 ? options.Iterations.Value : (int?)null;
        var deadline = options.Duration is { } duration ? startedUtc + duration : (DateTimeOffset?)null;

        return await RunCoreAsync(
            options,
            startedUtc,
            index => generator.Next(index),
            samplerMode: "deterministic_shape_stratified",
            maxIterations,
            deadline,
            cancellationToken);
    }

    public static async Task<FuzzRunSummary> RunCasesAsync(
        IEnumerable<FuzzCase> fuzzCases,
        FuzzOptions options,
        CancellationToken cancellationToken = default)
    {
        var cases = fuzzCases.ToImmutableArray();
        var startedUtc = DateTimeOffset.UtcNow;

        return await RunCoreAsync(
            options,
            startedUtc,
            index => cases[index],
            samplerMode: "explicit_cases",
            cases.Length,
            deadline: null,
            cancellationToken);
    }

    private static async Task<FuzzRunSummary> RunCoreAsync(
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
            if (maxIterations is { } max && builder.CasesAnalyzed >= max)
            {
                break;
            }

            if (deadline is { } end && DateTimeOffset.UtcNow >= end)
            {
                break;
            }

            var remainingCases = maxIterations is { } maximum
                ? maximum - builder.CasesAnalyzed
                : options.Parallelism * 8;
            var batchSize = Math.Max(1, Math.Min(options.Parallelism * 8, remainingCases));
            var startIndex = builder.CasesAnalyzed;
            var cases = Enumerable.Range(startIndex, batchSize)
                .Select(createCase)
                .ToImmutableArray();
            var analyses = await AnalyzeCasesAsync(cases, options.RepeatAnalyzer, options.Parallelism, cancellationToken);

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
                        var fileName = $"{savedInterestingCases + 1:0000}-{SanitizeFileName(analysis.Case.Family)}-{analysis.NormalizedSourceHash[..12]}.cs";
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
                    var checkpointSummary = builder.Build(DateTimeOffset.UtcNow, stopwatch.Elapsed, options.OutputDirectory, savedInterestingCases);
                    await WriteArtifactsAsync(checkpointSummary, options.OutputDirectory, isPartial: true, cancellationToken);
                    while (builder.CasesAnalyzed >= nextCheckpointAt)
                    {
                        nextCheckpointAt += options.CheckpointEvery;
                    }
                }
            }
        }

        stopwatch.Stop();
        var summary = builder.Build(DateTimeOffset.UtcNow, stopwatch.Elapsed, options.OutputDirectory, savedInterestingCases);
        await WriteArtifactsAsync(summary, options.OutputDirectory, isPartial: false, cancellationToken);
        return summary;
    }

    private static async Task<ImmutableArray<FuzzCaseAnalysis>> AnalyzeCasesAsync(
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
            async (index, ct) =>
            {
                analyses[index] = await AnalyzeCaseAsync(fuzzCases[index], repeatAnalyzer, ct);
            });

        return analyses.ToImmutableArray();
    }

    public static async Task<FuzzCaseAnalysis> AnalyzeCaseAsync(
        FuzzCase fuzzCase,
        bool repeatAnalyzer = true,
        CancellationToken cancellationToken = default)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(fuzzCase.Source, ParseOptions, cancellationToken: cancellationToken);
        var compilation = CreateCompilation(fuzzCase.Name, syntaxTree, fuzzCase.AllowUnsafe);
        var normalizedSourceHash = ComputeStableHash(NormalizeSource(fuzzCase.Source));
        var compilerErrors = compilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToImmutableArray();

        if (compilerErrors.Length > 0)
        {
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
        }

        var operationKinds = CollectOperationKinds(compilation, syntaxTree, cancellationToken);
        var syntaxKinds = CollectSyntaxKinds(syntaxTree);
        var firstDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation, cancellationToken);
        var findings = Evaluate(fuzzCase, firstDiagnostics.Diagnostics, firstDiagnostics.Exceptions);
        var diagnosticSignatures = ToDiagnosticSignatures(firstDiagnostics.Diagnostics);

        if (repeatAnalyzer)
        {
            var secondDiagnostics = await GetAnalyzerDiagnosticsAsync(compilation, cancellationToken);
            var secondDiagnosticSignatures = ToDiagnosticSignatures(secondDiagnostics.Diagnostics);
            if (!diagnosticSignatures.SequenceEqual(secondDiagnosticSignatures, StringComparer.Ordinal))
            {
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "nondeterministic_diagnostics",
                    "Repeated analyzer runs produced different diagnostic signatures.",
                    null,
                    diagnosticSignatures.Concat(secondDiagnosticSignatures).ToImmutableArray()));
            }

            foreach (var exception in secondDiagnostics.Exceptions)
            {
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "analyzer_exception",
                    exception,
                    null,
                    ImmutableArray<string>.Empty));
            }
        }

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

    private static AnalyzerRunResult EmptyAnalyzerRun => new(ImmutableArray<Diagnostic>.Empty, ImmutableArray<string>.Empty);

    private static async Task<AnalyzerRunResult> GetAnalyzerDiagnosticsAsync(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new FixedAnalyzerConfigOptionsProvider(
                    ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true")));
            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    options,
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
            return new AnalyzerRunResult(diagnostics, ImmutableArray<string>.Empty);
        }
        catch (Exception ex)
        {
            return EmptyAnalyzerRun with { Exceptions = ImmutableArray.Create(ex.ToString()) };
        }
    }

    private static ImmutableArray<FuzzFinding>.Builder Evaluate(
        FuzzCase fuzzCase,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArray<string> analyzerExceptions)
    {
        var findings = ImmutableArray.CreateBuilder<FuzzFinding>();
        foreach (var exception in analyzerExceptions)
        {
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "analyzer_exception",
                exception,
                null,
                ImmutableArray<string>.Empty));
        }

        var ps0002Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
            .ToImmutableArray();
        var ps0010Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId)
            .ToImmutableArray();

        if (fuzzCase.Expectation.Ps0002 == Ps0002ExpectationKind.MustNotEmit && ps0002Diagnostics.Length > 0)
        {
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "pure_ps0002",
                "A definitely-pure generated case produced PS0002.",
                null,
                ToDiagnosticSignatures(ps0002Diagnostics)));
        }

        if (fuzzCase.Expectation.Ps0002 == Ps0002ExpectationKind.MustEmit && ps0002Diagnostics.Length == 0)
        {
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "impure_missing_ps0002",
                "A definitely-impure generated case did not produce PS0002.",
                null,
                ToDiagnosticSignatures(diagnostics)));
        }

        foreach (var diagnostic in ps0002Diagnostics)
        {
            if (MissingAnyRequiredProperties(diagnostic, fuzzCase.Expectation.RequiredPs0002Properties))
            {
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "missing_ps0002_evidence",
                    "PS0002 did not include stable category/rule/operation evidence.",
                    null,
                    ImmutableArray.Create(ToDiagnosticSignature(diagnostic))));
            }

            if (fuzzCase.Expectation.Ps0002 == Ps0002ExpectationKind.MustNotEmit &&
                diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ImpurityCategoryProperty, out var category) &&
                string.Equals(category, "unsupported_operation", StringComparison.Ordinal))
            {
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "pure_unsupported_operation",
                    "A definitely-pure generated case hit unsupported_operation.",
                    null,
                    ImmutableArray.Create(ToDiagnosticSignature(diagnostic))));
            }
        }

        if (fuzzCase.Expectation.Ps0010 == Ps0010ExpectationKind.MustNotEmit && ps0010Diagnostics.Length > 0)
        {
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "unexpected_ps0010",
                "A generated case unexpectedly produced PS0010.",
                null,
                ToDiagnosticSignatures(ps0010Diagnostics)));
        }

        if (fuzzCase.Expectation.Ps0010 == Ps0010ExpectationKind.MustEmit && ps0010Diagnostics.Length == 0)
        {
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "missing_ps0010",
                "A generated case expected to produce PS0010 did not do so.",
                null,
                ToDiagnosticSignatures(diagnostics)));
        }

        if (fuzzCase.Expectation.Ps0010 != Ps0010ExpectationKind.Ignore)
        {
            foreach (var diagnostic in ps0010Diagnostics)
            {
                if (MissingAnyRequiredProperties(diagnostic, fuzzCase.Expectation.RequiredPs0010Properties))
                {
                    findings.Add(new FuzzFinding(
                        fuzzCase.Name,
                        fuzzCase.Family,
                        "missing_ps0010_evidence",
                        "PS0010 did not include stable exception evidence.",
                        null,
                        ImmutableArray.Create(ToDiagnosticSignature(diagnostic))));
                }
            }
        }

        return findings;
    }

    private static bool MissingAnyRequiredProperties(Diagnostic diagnostic, ImmutableArray<string> keys)
    {
        foreach (var key in keys)
        {
            if (MissingProperty(diagnostic, key))
            {
                return true;
            }
        }

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

        foreach (var node in syntaxTree.GetRoot(cancellationToken).DescendantNodes())
        {
            var operation = semanticModel.GetOperation(node, cancellationToken);
            if (operation is null)
            {
                continue;
            }

            foreach (var descendant in operation.DescendantsAndSelf())
            {
                Increment(counts, descendant.Kind.ToString());
            }
        }

        return counts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static ImmutableSortedDictionary<string, int> CollectSyntaxKinds(SyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        Increment(counts, ((SyntaxKind)root.RawKind).ToString());

        foreach (var nodeOrToken in root.DescendantNodesAndTokens(descendIntoTrivia: true))
        {
            Increment(counts, ((SyntaxKind)nodeOrToken.RawKind).ToString());
        }

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            Increment(counts, ((SyntaxKind)trivia.RawKind).ToString());
            var structure = trivia.GetStructure();
            if (structure is null)
            {
                continue;
            }

            Increment(counts, ((SyntaxKind)structure.RawKind).ToString());
            foreach (var nodeOrToken in structure.DescendantNodesAndTokens(descendIntoTrivia: true))
            {
                Increment(counts, ((SyntaxKind)nodeOrToken.RawKind).ToString());
            }
        }

        return counts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, SyntaxTree syntaxTree, bool allowUnsafe)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GetMetadataReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: allowUnsafe,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static ImmutableArray<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES was not available.");
        }

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
    }

    private static string CreateInterestingCaseKey(FuzzCaseAnalysis analysis)
    {
        var findingIdentity = string.Join(
            "||",
            analysis.Findings
                .OrderBy(finding => finding.Category, StringComparer.Ordinal)
                .ThenBy(finding => finding.Description, StringComparer.Ordinal)
                .Select(CreateFindingIdentity));

        return analysis.NormalizedSourceHash + "|" + findingIdentity;
    }

    private static string CreateFindingIdentity(FuzzFinding finding)
    {
        return finding.Family + "|" +
               finding.Category + "|" +
               finding.Description + "|" +
               string.Join("||", finding.Details.OrderBy(detail => detail, StringComparer.Ordinal));
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

    private static void Increment(IDictionary<string, int> values, string key)
    {
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }
}

public sealed class FuzzCaseGenerator
{
    private readonly int _seed;

    private static readonly ImmutableArray<ShapeRegistryEntry> Registry = ImmutableArray.Create(
        new ShapeRegistryEntry(
            "PureArithmetic",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildPureArithmetic),
        new ShapeRegistryEntry(
            "PureStringConcat",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray.Create("AddExpression"),
            FuzzExpectation.DefinitelyPure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildPureStringConcat),
        new ShapeRegistryEntry(
            "PureListPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ListPattern)),
            ImmutableArray.Create("ListPattern"),
            ImmutableArray.Create("ListPattern"),
            FuzzExpectation.DefinitelyPure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildPureListPattern),
        new ShapeRegistryEntry(
            "PureCollectionExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CollectionExpression)),
            ImmutableArray.Create("CollectionExpression"),
            ImmutableArray.Create("CollectionExpression"),
            FuzzExpectation.DefinitelyPure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildPureCollectionExpression),
        new ShapeRegistryEntry(
            "PureInterpolatedString",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedString)),
            ImmutableArray.Create("InterpolatedString"),
            ImmutableArray.Create("InterpolatedStringExpression"),
            FuzzExpectation.DefinitelyPure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildPureInterpolatedString),
        new ShapeRegistryEntry(
            "PureUtf8String",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Utf8String)),
            ImmutableArray.Create("Utf8String"),
            ImmutableArray.Create("Utf8StringLiteralExpression"),
            FuzzExpectation.DefinitelyPure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildPureUtf8String),
        new ShapeRegistryEntry(
            "PureArrayCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ArrayCreation)),
            ImmutableArray.Create("ArrayCreation"),
            ImmutableArray.Create("ArrayCreationExpression"),
            FuzzExpectation.DefinitelyPure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildPureArrayCreation),
        new ShapeRegistryEntry(
            "ImpureConsoleWrite",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Invocation)),
            ImmutableArray.Create("Invocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildImpureConsoleWrite),
        new ShapeRegistryEntry(
            "ImpureDynamicDispatch",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicInvocation)),
            ImmutableArray.Create("DynamicInvocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureDynamicDispatch),
        new ShapeRegistryEntry(
            "ImpureDelegateInvoke",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Invocation)),
            ImmutableArray.Create("Invocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureDelegateInvoke),
        new ShapeRegistryEntry(
            "ImpureThrowExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureThrowExpression),
        new ShapeRegistryEntry(
            "ExceptionDirectThrowInvalidOperation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            ImpureWithExceptionExpectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionDirectThrowInvalidOperation),
        new ShapeRegistryEntry(
            "ExceptionGuardedThrowArgumentNull",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            ImpureWithExceptionExpectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionGuardedThrowArgumentNull),
        new ShapeRegistryEntry(
            "ExceptionThrowExpressionFormatException",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowExpression"),
            ImpureWithExceptionExpectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionThrowExpressionFormatException),
        new ShapeRegistryEntry(
            "ExceptionCaughtInternalThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Try),
                RoslynShapeManifest.OperationShapeId(OperationKind.CatchClause)),
            ImmutableArray.Create("Try", "CatchClause", "Throw"),
            ImmutableArray.Create("TryStatement", "CatchClause", "ThrowStatement"),
            ImpureWithoutExceptionExpectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionCaughtInternalThrow),
        new ShapeRegistryEntry(
            "ExceptionDeadBranchThrow",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Conditional", "Throw"),
            ImmutableArray.Create("IfStatement", "ThrowStatement"),
            PureWithoutExceptionExpectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionDeadBranchThrow),
        new ShapeRegistryEntry(
            "ExceptionGuardedSafeDivideByZeroExcluded",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Conditional", "Binary"),
            ImmutableArray.Create("IfStatement", "DivideExpression"),
            PureWithoutExceptionExpectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionGuardedSafeDivideByZeroExcluded),
        new ShapeRegistryEntry(
            "ExceptionGuardedNullDereferenceExcluded",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("Conditional", "PropertyReference"),
            ImmutableArray.Create("IfStatement"),
            PureWithoutExceptionExpectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionGuardedNullDereferenceExcluded),
        new ShapeRegistryEntry(
            "ExceptionDefiniteDivideByZero",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray.Create("DivideExpression"),
            ExceptionWithOptionalPs0002Expectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionDefiniteDivideByZero),
        new ShapeRegistryEntry(
            "ExceptionDefiniteNullReference",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray.Create("SimpleMemberAccessExpression"),
            ExceptionWithOptionalPs0002Expectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionDefiniteNullReference),
        new ShapeRegistryEntry(
            "ExceptionUsingDisposeThrows",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration)),
            ImmutableArray.Create("UsingDeclaration"),
            ImmutableArray.Create("LocalDeclarationStatement"),
            ExceptionWithOptionalPs0002Expectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionUsingDisposeThrows),
        new ShapeRegistryEntry(
            "ExceptionInvokedLocalFunctionThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.LocalFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("LocalFunction", "Throw"),
            ImmutableArray.Create("LocalFunctionStatement", "ThrowStatement"),
            ExceptionWithOptionalPs0002Expectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionInvokedLocalFunctionThrow),
        new ShapeRegistryEntry(
            "ExceptionInvokedLambdaThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("AnonymousFunction", "Throw"),
            ImmutableArray.Create("ParenthesizedLambdaExpression", "ThrowExpression"),
            ExceptionWithOptionalPs0002Expectation(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildExceptionInvokedLambdaThrow),
        new ShapeRegistryEntry(
            "ImpureFieldWrite",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.SimpleAssignment),
                RoslynShapeManifest.OperationShapeId(OperationKind.FieldReference)),
            ImmutableArray.Create("SimpleAssignment", "FieldReference"),
            ImmutableArray.Create("SimpleAssignmentExpression"),
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureFieldWrite),
        new ShapeRegistryEntry(
            "ImpureAmbientDateTime",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray.Create("SimpleMemberAccessExpression"),
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureAmbientDateTime),
        new ShapeRegistryEntry(
            "ImpureAwaitTaskDelay",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Await)),
            ImmutableArray.Create("Await"),
            ImmutableArray.Create("AwaitExpression"),
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureAwaitTaskDelay),
        new ShapeRegistryEntry(
            "ImpureLockSection",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Lock)),
            ImmutableArray.Create("Lock"),
            ImmutableArray.Create("LockStatement"),
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureLockSection),
        new ShapeRegistryEntry(
            "ImpureUsingStandardOutput",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration)),
            ImmutableArray.Create("UsingDeclaration"),
            ImmutableArray.Create("LocalDeclarationStatement"),
            FuzzExpectation.DefinitelyImpure(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildImpureUsingStandardOutput),
        new ShapeRegistryEntry(
            "ConservativeTryCatch",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Try),
                RoslynShapeManifest.OperationShapeId(OperationKind.CatchClause)),
            ImmutableArray.Create("Try", "CatchClause"),
            ImmutableArray.Create("TryStatement", "CatchClause"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeTryCatch),
        new ShapeRegistryEntry(
            "ConservativeConditionalAccessCoalesce",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.ConditionalAccess),
                RoslynShapeManifest.OperationShapeId(OperationKind.Coalesce)),
            ImmutableArray.Create("ConditionalAccess", "Coalesce"),
            ImmutableArray.Create("ConditionalAccessExpression", "CoalesceExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeConditionalAccessCoalesce),
        new ShapeRegistryEntry(
            "ConservativeIsTypeCheck",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.IsType)),
            ImmutableArray.Create("IsType"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeIsTypeCheck),
        new ShapeRegistryEntry(
            "ConservativeNegatedPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.NegatedPattern)),
            ImmutableArray.Create("NegatedPattern"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeNegatedPattern),
        new ShapeRegistryEntry(
            "ConservativeSwitchStatement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Switch)),
            ImmutableArray.Create("Switch", "SwitchCase"),
            ImmutableArray.Create("SwitchStatement"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeSwitchStatement),
        new ShapeRegistryEntry(
            "ConservativeUsingStatement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Using)),
            ImmutableArray.Create("Using"),
            ImmutableArray.Create("UsingStatement"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeUsingStatement),
        new ShapeRegistryEntry(
            "ConservativeCompoundAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CompoundAssignment)),
            ImmutableArray.Create("CompoundAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeCompoundAssignment),
        new ShapeRegistryEntry(
            "ConservativeCoalesceAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CoalesceAssignment)),
            ImmutableArray.Create("CoalesceAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeCoalesceAssignment),
        new ShapeRegistryEntry(
            "ConservativeDeconstructionAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeconstructionAssignment)),
            ImmutableArray.Create("DeconstructionAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeDeconstructionAssignment),
        new ShapeRegistryEntry(
            "ConservativeIncrement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Increment)),
            ImmutableArray.Create("Increment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeIncrement),
        new ShapeRegistryEntry(
            "ConservativeDecrement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Decrement)),
            ImmutableArray.Create("Decrement"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeDecrement),
        new ShapeRegistryEntry(
            "ConservativeDeclarationExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeclarationExpression)),
            ImmutableArray.Create("DeclarationExpression"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeDeclarationExpression),
        new ShapeRegistryEntry(
            "ConservativeTypeParameterObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.TypeParameterObjectCreation)),
            ImmutableArray.Create("TypeParameterObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeTypeParameterObjectCreation),
        new ShapeRegistryEntry(
            "ConservativeEventAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.EventAssignment)),
            ImmutableArray.Create("EventAssignment", "EventReference"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeEventAssignment),
        new ShapeRegistryEntry(
            "ConservativeAnonymousObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousObjectCreation)),
            ImmutableArray.Create("AnonymousObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeAnonymousObjectCreation),
        new ShapeRegistryEntry(
            "ConservativeDefaultValue",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DefaultValue)),
            ImmutableArray.Create("DefaultValue"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeDefaultValue),
        new ShapeRegistryEntry(
            "ConservativeSizeOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.SizeOf)),
            ImmutableArray.Create("SizeOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeSizeOf),
        new ShapeRegistryEntry(
            "ConservativeTypeOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.TypeOf)),
            ImmutableArray.Create("TypeOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeTypeOf),
        new ShapeRegistryEntry(
            "ConservativeDynamicIndexerAccess",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicIndexerAccess)),
            ImmutableArray.Create("DynamicIndexerAccess"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeDynamicIndexerAccess),
        new ShapeRegistryEntry(
            "ConservativeDynamicObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicObjectCreation)),
            ImmutableArray.Create("DynamicObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeDynamicObjectCreation),
        new ShapeRegistryEntry(
            "ConservativeTuple",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Tuple)),
            ImmutableArray.Create("Tuple"),
            ImmutableArray.Create("TupleExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: true,
            BuildConservativeTuple),
        new ShapeRegistryEntry(
            "ConservativeInterfaceGetter",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeInterfaceGetter),
        new ShapeRegistryEntry(
            "ConservativeRecursivePattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.RecursivePattern)),
            ImmutableArray.Create("RecursivePattern"),
            ImmutableArray.Create("RecursivePattern"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeRecursivePattern),
        new ShapeRegistryEntry(
            "ConservativeSpreadCollectionExpression",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.CollectionExpression),
                RoslynShapeManifest.OperationShapeId(OperationKind.Spread)),
            ImmutableArray.Create("CollectionExpression", "Spread"),
            ImmutableArray.Create("CollectionExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeSpreadCollectionExpression),
        new ShapeRegistryEntry(
            "ConservativeSwitchExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.SwitchExpression)),
            ImmutableArray.Create("SwitchExpression"),
            ImmutableArray.Create("SwitchExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeSwitchExpression),
        new ShapeRegistryEntry(
            "ConservativeRangeSlice",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Range)),
            ImmutableArray.Create("Range"),
            ImmutableArray.Create("RangeExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeRangeSlice),
        new ShapeRegistryEntry(
            "ConservativeYieldReturn",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.YieldReturn)),
            ImmutableArray.Create("YieldReturn"),
            ImmutableArray.Create("YieldReturnStatement"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeYieldReturn),
        new ShapeRegistryEntry(
            "ConservativeWithExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.With)),
            ImmutableArray.Create("With"),
            ImmutableArray.Create("WithExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeWithExpression),
        new ShapeRegistryEntry(
            "ConservativeAnonymousFunction",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction)),
            ImmutableArray.Create("AnonymousFunction"),
            ImmutableArray.Create("SimpleLambdaExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeAnonymousFunction),
        new ShapeRegistryEntry(
            "ConservativeDelegateCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DelegateCreation)),
            ImmutableArray.Create("DelegateCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeDelegateCreation),
        new ShapeRegistryEntry(
            "ConservativeImplicitIndexerReference",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ImplicitIndexerReference)),
            ImmutableArray.Create("ImplicitIndexerReference"),
            ImmutableArray.Create("ElementAccessExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeImplicitIndexerReference),
        new ShapeRegistryEntry(
            "ConservativeInterpolatedStringHandler",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringHandlerCreation),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAppendLiteral),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAppendFormatted)),
            ImmutableArray.Create("InterpolatedStringHandlerCreation", "InterpolatedStringAppendLiteral", "InterpolatedStringAppendFormatted"),
            ImmutableArray.Create("InterpolatedStringExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeInterpolatedStringHandler),
        new ShapeRegistryEntry(
            "ConservativeFunctionPointer",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.FunctionPointerInvocation)),
            ImmutableArray.Create("FunctionPointerInvocation"),
            ImmutableArray.Create("FunctionPointerType"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: true,
            AllowEffectPreservingWrappers: false,
            BuildConservativeFunctionPointer),
        new ShapeRegistryEntry(
            "ConservativeNestedLambdaLocalFunction",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.LocalFunction)),
            ImmutableArray.Create("AnonymousFunction", "LocalFunction"),
            ImmutableArray.Create("SimpleLambdaExpression", "LocalFunctionStatement"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeNestedLambdaLocalFunction),
        new ShapeRegistryEntry(
            "ConservativeTuplePatternSwitch",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Tuple),
                RoslynShapeManifest.OperationShapeId(OperationKind.SwitchExpression)),
            ImmutableArray.Create("Tuple", "SwitchExpression"),
            ImmutableArray.Create("TupleExpression", "SwitchExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeTuplePatternSwitch),
        new ShapeRegistryEntry(
            "ConservativeUsingAwaitDelegateFlow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration),
                RoslynShapeManifest.OperationShapeId(OperationKind.Await),
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction)),
            ImmutableArray.Create("UsingDeclaration", "Await", "AnonymousFunction"),
            ImmutableArray.Create("LocalDeclarationStatement", "AwaitExpression", "ParenthesizedLambdaExpression"),
            FuzzExpectation.Conservative(),
            AllowUnsafe: false,
            AllowEffectPreservingWrappers: false,
            BuildConservativeUsingAwaitDelegateFlow));

    private static readonly ImmutableSortedDictionary<string, ImmutableArray<ShapeRegistryEntry>> RegistryByPrimaryShape =
        Registry
            .SelectMany(
                registryEntry => registryEntry.PrimaryShapeIds.Select(
                    shapeId => new KeyValuePair<string, ShapeRegistryEntry>(shapeId, registryEntry)))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(
                group => group.Key,
                group => group.Select(pair => pair.Value)
                    .Distinct()
                    .OrderBy(registryEntry => registryEntry.Id, StringComparer.Ordinal)
                    .ToImmutableArray(),
                StringComparer.Ordinal);

    private static readonly ImmutableArray<string> OrderedGeneratorBackedShapeIds =
        RegistryByPrimaryShape.Keys.ToImmutableArray();

    public static ImmutableArray<ShapeRegistryEntry> RegistryEntries => Registry;

    public FuzzCaseGenerator(int seed)
    {
        _seed = seed;
    }

    public FuzzCase Next(int index)
    {
        var shapeId = OrderedGeneratorBackedShapeIds[index % OrderedGeneratorBackedShapeIds.Length];
        var variant = index / OrderedGeneratorBackedShapeIds.Length;
        return GenerateForShapeCore(shapeId, variant, index);
    }

    public FuzzCase GenerateForShape(string shapeId, int variant)
    {
        var index = HashCode.Combine(shapeId, variant) & int.MaxValue;
        return GenerateForShapeCore(shapeId, variant, index);
    }

    private FuzzCase GenerateForShapeCore(string shapeId, int variant, int index)
    {
        if (!RegistryByPrimaryShape.TryGetValue(shapeId, out var entries))
        {
            throw new ArgumentException($"Unknown generator-backed shape '{shapeId}'.", nameof(shapeId));
        }

        var entry = entries[variant % entries.Length];
        var entryVariant = variant / entries.Length;
        return GenerateForRegistryEntry(entry, index, entryVariant);
    }

    public FuzzCase GenerateForRegistryEntry(ShapeRegistryEntry registryEntry, int index, int variant = 0)
    {
        var random = CreateRandom(HashCode.Combine(index, variant, registryEntry.Id));
        var className = $"FuzzCase{index}_{registryEntry.Id}_V{variant}";
        var source = registryEntry.Build(index, random, className);
        return new FuzzCase(
            Name: $"{index:000000}-{registryEntry.Id}",
            Family: registryEntry.Id,
            Source: source,
            AllowUnsafe: registryEntry.AllowUnsafe ||
                         source.Contains("unsafe", StringComparison.Ordinal) ||
                         source.Contains("delegate*", StringComparison.Ordinal),
            Expectation: registryEntry.Expectation,
            PrimaryShapeIds: registryEntry.PrimaryShapeIds,
            ExpectedOperationKinds: registryEntry.ExpectedOperationKinds,
            ExpectedSyntaxKinds: registryEntry.ExpectedSyntaxKinds);
    }

    private static string BuildPureArithmetic(int index, Random random, string className)
    {
        var expression = random.Next(4) switch
        {
            0 => "x + 1",
            1 => "(x * 3) - 7",
            2 => "(x / 2) + 9",
            _ => "unchecked((x << 1) ^ 17)"
        };

        return BuildClass(
            className,
            BuildIntMethodFromExpression(expression, random));
    }

    private static string BuildPureStringConcat(int index, Random random, string className)
    {
        var expression = "(left + right).Length";

        return BuildClass(
            className,
            $$"""
                [EnforcePure]
                public int TestMethod(string left, string right)
                {
            {{Indent(BuildReturnBody(expression, random), 8)}}
                }
            """);
    }

    private static string BuildPureInterpolatedString(int index, Random random, string className)
    {
        var expression = random.Next(2) == 0
            ? "$\"value={x}\".Length"
            : "$\"sum={x + 1}\".Length";

        return BuildClass(
            className,
            BuildIntMethodFromExpression(expression, random));
    }

    private static string BuildPureUtf8String(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return "abc"u8.Length;
                }
            """);
    }

    private static string BuildPureArrayCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var values = new int[] { 1, x, 3 };
                    return values[1];
                }
            """);
    }

    private static string BuildPureListPattern(int index, Random random, string className)
    {
        var expression = random.Next(2) == 0
            ? "values is [1, .., 3] ? 1 : 0"
            : "values is [_, .. var rest] ? rest.Length : 0";

        return BuildClass(
            className,
            BuildIntMethodFromExpression(expression, random, "int[] values"));
    }

    private static string BuildPureCollectionExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    int[] values = [1, x, 3];
                    return values.Length;
                }
            """);
    }

    private static string BuildImpureConsoleWrite(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public void TestMethod()
                {
                    Console.WriteLine("impure");
                }
            """);
    }

    private static string BuildImpureDynamicDispatch(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public string TestMethod(dynamic value)
                {
                    return value.ToString();
                }
            """);
    }

    private static string BuildImpureDelegateInvoke(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public void TestMethod(Action action)
                {
                    action();
                }
            """);
    }

    private static string BuildImpureThrowExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    throw new InvalidOperationException("fuzz");
                }
            """);
    }

    private static string BuildExceptionDirectThrowInvalidOperation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    throw new InvalidOperationException("fuzz");
                }
            """);
    }

    private static string BuildExceptionGuardedThrowArgumentNull(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    if (text == null)
                    {
                        throw new ArgumentNullException(nameof(text));
                    }

                    return text.Length;
                }
            """);
    }

    private static string BuildExceptionThrowExpressionFormatException(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    return string.IsNullOrWhiteSpace(text)
                        ? throw new FormatException("fuzz")
                        : text.Length;
                }
            """);
    }

    private static string BuildExceptionCaughtInternalThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    try
                    {
                        throw new InvalidOperationException("fuzz");
                    }
                    catch (InvalidOperationException)
                    {
                        return 1;
                    }
                }
            """);
    }

    private static string BuildExceptionDeadBranchThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    if (false)
                    {
                        throw new InvalidOperationException("fuzz");
                    }

                    return 1;
                }
            """);
    }

    private static string BuildExceptionGuardedSafeDivideByZeroExcluded(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int divisor)
                {
                    if (divisor != 0)
                    {
                        return 10 / divisor;
                    }

                    return 1;
                }
            """);
    }

    private static string BuildExceptionGuardedNullDereferenceExcluded(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    if (text == null)
                    {
                        return 0;
                    }

                    return text.Length;
                }
            """);
    }

    private static string BuildExceptionDefiniteDivideByZero(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    var zero = 0;
                    return 10 / zero;
                }
            """);
    }

    private static string BuildExceptionDefiniteNullReference(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    string text = null;
                    return text.Length;
                }
            """);
    }

    private static string BuildExceptionUsingDisposeThrows(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                private sealed class ThrowingDisposable : IDisposable
                {
                    public void Dispose()
                    {
                        throw new ObjectDisposedException("fuzz");
                    }
                }

                [EnforcePure]
                public int TestMethod()
                {
                    using var disposable = new ThrowingDisposable();
                    return 1;
                }
            """);
    }

    private static string BuildExceptionInvokedLocalFunctionThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    int Local()
                    {
                        throw new InvalidOperationException("fuzz");
                    }

                    return Local();
                }
            """);
    }

    private static string BuildExceptionInvokedLambdaThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    Func<int> local = () => throw new FormatException("fuzz");
                    return local();
                }
            """);
    }

    private static string BuildImpureFieldWrite(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                private int _value;

                [EnforcePure]
                public void TestMethod(int value)
                {
                    _value = value;
                }
            """);
    }

    private static string BuildImpureAmbientDateTime(int index, Random random, string className)
    {
        return BuildClass(
            className,
            BuildIntMethodFromExpression("DateTime.Now.Day", random));
    }

    private static string BuildImpureAwaitTaskDelay(int index, Random random, string className)
    {
        return $$"""
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public class {{className}}
{
    [EnforcePure]
    public async Task<int> TestMethod()
    {
        await Task.Delay(1);
        return 1;
    }
}
""";
    }

    private static string BuildImpureLockSection(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                private readonly object _gate = new object();

                [EnforcePure]
                public int TestMethod()
                {
                    lock (_gate)
                    {
                        return 1;
                    }
                }
            """);
    }

    private static string BuildImpureUsingStandardOutput(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    using var stream = Console.OpenStandardOutput();
                    return 1;
                }
            """);
    }

    private static string BuildConservativeTryCatch(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    try
                    {
                        if (x < 0)
                        {
                            throw new ArgumentOutOfRangeException(nameof(x));
                        }

                        return x + 1;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return 0;
                    }
                }
            """);
    }

    private static string BuildConservativeConditionalAccessCoalesce(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text, string fallback)
                {
                    return text?.Trim().Length ?? fallback.Length;
                }
            """);
    }

    private static string BuildConservativeIsTypeCheck(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(object value)
                {
                    return value is string ? 1 : 0;
                }
            """);
    }

    private static string BuildConservativeNegatedPattern(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(object value)
                {
                    return value is not int ? 1 : 0;
                }
            """);
    }

    private static string BuildConservativeSwitchStatement(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int value)
                {
                    switch (value)
                    {
                        case 0:
                            return 0;
                        case 1:
                        case 2:
                            return 1;
                        default:
                            return value;
                    }
                }
            """);
    }

    private static string BuildConservativeUsingStatement(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    using (var writer = new System.IO.StringWriter())
                    {
                        return 1;
                    }
                }
            """);
    }

    private static string BuildConservativeCompoundAssignment(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var value = x;
                    value += 2;
                    return value;
                }
            """);
    }

    private static string BuildConservativeCoalesceAssignment(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public string TestMethod(string value)
                {
                    value ??= "fallback";
                    return value;
                }
            """);
    }

    private static string BuildConservativeDeconstructionAssignment(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x, int y)
                {
                    var left = x;
                    var right = y;
                    (left, right) = (right, left);
                    return left - right;
                }
            """);
    }

    private static string BuildConservativeIncrement(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var value = x;
                    value++;
                    return value;
                }
            """);
    }

    private static string BuildConservativeDecrement(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var value = x;
                    value--;
                    return value;
                }
            """);
    }

    private static string BuildConservativeDeclarationExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    return int.TryParse(text, out var value) ? value : 0;
                }
            """);
    }

    private static string BuildConservativeTypeParameterObjectCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public T TestMethod<T>() where T : new()
                {
                    return new T();
                }
            """);
    }

    private static string BuildConservativeEventAssignment(int index, Random random, string className)
    {
        return $$"""
using System;
using PurelySharp.Attributes;

public sealed class {{className}}Source
{
    public event EventHandler Changed
    {
        add { }
        remove { }
    }
}

public class {{className}}
{
    [EnforcePure]
    public void TestMethod({{className}}Source source)
    {
        source.Changed += Handle;
    }

    private static void Handle(object sender, EventArgs args) { }
}
""";
    }

    private static string BuildConservativeAnonymousObjectCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var item = new { Value = x, Next = x + 1 };
                    return item.Value + item.Next;
                }
            """);
    }

    private static string BuildConservativeDefaultValue(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return default(int);
                }
            """);
    }

    private static string BuildConservativeSizeOf(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return sizeof(int);
                }
            """);
    }

    private static string BuildConservativeTypeOf(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return typeof(int).Name.Length;
                }
            """);
    }

    private static string BuildConservativeDynamicIndexerAccess(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(dynamic values)
                {
                    return values[0];
                }
            """);
    }

    private static string BuildConservativeDynamicObjectCreation(int index, Random random, string className)
    {
        return $$"""
using PurelySharp.Attributes;

public sealed class {{className}}Widget
{
    public {{className}}Widget(int value)
    {
    }
}

public class {{className}}
{
    [EnforcePure]
    public int TestMethod(dynamic value)
    {
        var widget = new {{className}}Widget(value);
        return 1;
    }
}
""";
    }

    private static string BuildConservativeTuple(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var pair = (Left: x, Right: x + 1);
                    return pair.Left + pair.Right;
                }
            """);
    }

    private static string BuildConservativeInterfaceGetter(int index, Random random, string className)
    {
        return $$"""
using PurelySharp.Attributes;

public interface I{{className}}Value
{
    int Value { get; }
}

public class {{className}}
{
    [EnforcePure]
    public int TestMethod(I{{className}}Value value)
    {
        return value.Value;
    }
}
""";
    }

    private static string BuildConservativeRecursivePattern(int index, Random random, string className)
    {
        return $$"""
using PurelySharp.Attributes;

public sealed class {{className}}Node
{
    public {{className}}Node? Next { get; set; }
    public int Value { get; set; }
}

public class {{className}}
{
    [EnforcePure]
    public int TestMethod({{className}}Node node)
    {
        return node is { Next: { Value: > 0 } } ? 1 : 0;
    }
}
""";
    }

    private static string BuildConservativeSpreadCollectionExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int[] values)
                {
                    int[] copy = [0, .. values, 9];
                    return copy.Length;
                }
            """);
    }

    private static string BuildConservativeSwitchExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    return x switch
                    {
                        < 0 => -1,
                        0 => 0,
                        _ => 1
                    };
                }
            """);
    }

    private static string BuildConservativeRangeSlice(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    return text[1..^1].Length;
                }
            """);
    }

    private static string BuildConservativeYieldReturn(int index, Random random, string className)
    {
        return $$"""
using System.Collections.Generic;
using PurelySharp.Attributes;

public class {{className}}
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(int x)
    {
        yield return x + 1;
        yield break;
    }
}
""";
    }

    private static string BuildConservativeWithExpression(int index, Random random, string className)
    {
        return $$"""
using System;
using PurelySharp.Attributes;

public record {{className}}Data(int Value, int Other);

public class {{className}}
{
    [EnforcePure]
    public int TestMethod({{className}}Data data, int x)
    {
        var updated = data with { Value = x };
        return updated.Value;
    }
}
""";
    }

    private static string BuildConservativeAnonymousFunction(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    Func<int, int> project = static value => value + 1;
                    return project(x);
                }
            """);
    }

    private static string BuildConservativeDelegateCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    Func<int, int> project = Project;
                    return project(x);
                }

                private static int Project(int value)
                {
                    return value + 1;
                }
            """);
    }

    private static string BuildConservativeImplicitIndexerReference(int index, Random random, string className)
    {
        return $$"""
using System;
using PurelySharp.Attributes;

public sealed class {{className}}Bag
{
    public int Length => 3;
    public int this[int index] => index + 10;
}

public class {{className}}
{
    [EnforcePure]
    public int TestMethod({{className}}Bag bag)
    {
        return bag[^1];
    }
}
""";
    }

    private static string BuildConservativeInterpolatedStringHandler(int index, Random random, string className)
    {
        return $$"""
using System;
using System.Runtime.CompilerServices;
using PurelySharp.Attributes;

[InterpolatedStringHandler]
public ref struct {{className}}Handler
{
    public {{className}}Handler(int literalLength, int formattedCount) { }
    public void AppendLiteral(string value) { }
    public void AppendFormatted<T>(T value) { }
}

public class {{className}}
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        Log($"value={value}");
    }

    private void Log({{className}}Handler handler) { }
}
""";
    }

    private static string BuildConservativeFunctionPointer(int index, Random random, string className)
    {
        return $$"""
using PurelySharp.Attributes;

public unsafe class {{className}}
{
    [EnforcePure]
    public int TestMethod(delegate*<int, int> pointer)
    {
        return pointer(1);
    }
}
""";
    }

    private static string BuildConservativeNestedLambdaLocalFunction(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    int Outer(int seed)
                    {
                        Func<int, int> addSeed = value =>
                        {
                            Func<int, int> inner = local => local + seed;
                            return inner(value);
                        };

                        return addSeed(x);
                    }

                    return Outer(1);
                }
            """);
    }

    private static string BuildConservativeTuplePatternSwitch(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x, int y)
                {
                    var pair = (x, y);
                    return pair switch
                    {
                        (> 0, > 0) => 1,
                        (0, _) => 0,
                        _ => -1
                    };
                }
            """);
    }

    private static string BuildConservativeUsingAwaitDelegateFlow(int index, Random random, string className)
    {
        return $$"""
using System;
using System.Threading.Tasks;
using PurelySharp.Attributes;

public class {{className}}
{
    [EnforcePure]
    public async Task<int> TestMethod()
    {
        using var stream = Console.OpenStandardOutput();
        Func<Task<int>> factory = async () =>
        {
            await Task.Delay(1);
            return stream.CanWrite ? 1 : 0;
        };

        return await factory();
    }
}
""";
    }

    private Random CreateRandom(int index)
    {
        return new Random(HashCode.Combine(_seed, index, 0x51ED270B));
    }

    private static FuzzExpectation ImpureWithExceptionExpectation()
    {
        return new FuzzExpectation(
            Ps0002ExpectationKind.MustEmit,
            Ps0010ExpectationKind.MustEmit,
            ImmutableArray.Create(
                PurelySharpDiagnostics.ImpurityCategoryProperty,
                PurelySharpDiagnostics.ImpurityRuleProperty,
                PurelySharpDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                PurelySharpDiagnostics.ExceptionTypesProperty,
                PurelySharpDiagnostics.ExceptionCategoriesProperty,
                PurelySharpDiagnostics.ExceptionSourcesProperty));
    }

    private static FuzzExpectation ImpureWithoutExceptionExpectation()
    {
        return new FuzzExpectation(
            Ps0002ExpectationKind.MustEmit,
            Ps0010ExpectationKind.MustNotEmit,
            ImmutableArray.Create(
                PurelySharpDiagnostics.ImpurityCategoryProperty,
                PurelySharpDiagnostics.ImpurityRuleProperty,
                PurelySharpDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                PurelySharpDiagnostics.ExceptionTypesProperty,
                PurelySharpDiagnostics.ExceptionCategoriesProperty,
                PurelySharpDiagnostics.ExceptionSourcesProperty));
    }

    private static FuzzExpectation ExceptionWithOptionalPs0002Expectation()
    {
        return new FuzzExpectation(
            Ps0002ExpectationKind.MayEmitConservatively,
            Ps0010ExpectationKind.MustEmit,
            ImmutableArray.Create(
                PurelySharpDiagnostics.ImpurityCategoryProperty,
                PurelySharpDiagnostics.ImpurityRuleProperty,
                PurelySharpDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                PurelySharpDiagnostics.ExceptionTypesProperty,
                PurelySharpDiagnostics.ExceptionCategoriesProperty,
                PurelySharpDiagnostics.ExceptionSourcesProperty));
    }

    private static FuzzExpectation PureWithoutExceptionExpectation()
    {
        return new FuzzExpectation(
            Ps0002ExpectationKind.MustNotEmit,
            Ps0010ExpectationKind.MustNotEmit,
            ImmutableArray.Create(
                PurelySharpDiagnostics.ImpurityCategoryProperty,
                PurelySharpDiagnostics.ImpurityRuleProperty,
                PurelySharpDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                PurelySharpDiagnostics.ExceptionTypesProperty,
                PurelySharpDiagnostics.ExceptionCategoriesProperty,
                PurelySharpDiagnostics.ExceptionSourcesProperty));
    }

    private static string BuildIntMethodFromExpression(string expression, Random random, string parameterList = "int x")
    {
        return $$"""
            [EnforcePure]
            public int TestMethod({{parameterList}})
            {
{{Indent(BuildReturnBody(expression, random), 8)}}
            }
""";
    }

    private static string BuildReturnBody(string expression, Random random)
    {
        return random.Next(5) switch
        {
            0 => $"return {expression};",
            1 => $"var value = {expression};\nreturn value;",
            2 => $"if (true)\n{{\n    return {expression};\n}}\nreturn 0;",
            3 => $"return true ? {expression} : 0;",
            _ => $"int Local() => {expression};\nreturn Local();"
        };
    }

    private static string BuildClass(string className, string members)
    {
        return $$"""
using System;
using PurelySharp.Attributes;

public class {{className}}
{
{{Indent(members, 4)}}
}
""";
    }

    private static string Indent(string text, int spaces)
    {
        var padding = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Length == 0 ? line : padding + line));
    }
}

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
    int OccurrenceCount = 1);

public sealed record FuzzRunSummary
{
    public string SchemaVersion { get; init; } = "1.2";

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

    public int Ps0002Count { get; init; }

    public int Ps0004Count { get; init; }

    public int Ps0009Count { get; init; }

    public int Ps0010Count { get; init; }

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

    public int GeneratorBackedShapeCount { get; init; }

    public int GeneratorBackedShapesWithRegistryCount { get; init; }

    public ImmutableArray<string> UnobservedGeneratorBackedShapes { get; init; } =
        ImmutableArray<string>.Empty;

    public ImmutableSortedDictionary<string, int> PrimaryShapeCounts { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public ImmutableArray<FuzzFinding> Findings { get; init; } =
        ImmutableArray<FuzzFinding>.Empty;
}

internal sealed class FuzzRunSummaryBuilder
{
    private readonly FuzzOptions _options;
    private readonly DateTimeOffset _startedUtc;
    private readonly string _samplerMode;
    private readonly SortedDictionary<string, int> _familyCounts = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _operationKinds = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _syntaxKinds = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _primaryShapeCounts = new(StringComparer.Ordinal);
    private readonly ImmutableArray<FuzzFinding>.Builder _findings = ImmutableArray.CreateBuilder<FuzzFinding>();
    private readonly Dictionary<string, int> _findingIndices = new(StringComparer.Ordinal);

    private int _compilationErrorCount;
    private int _ps0002Count;
    private int _ps0004Count;
    private int _ps0009Count;
    private int _ps0010Count;

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
        {
            foreach (var shapeId in analysis.Case.PrimaryShapeIds)
            {
                Increment(_primaryShapeCounts, shapeId);
            }
        }
        _compilationErrorCount += analysis.CompilationErrors.Length > 0 ? 1 : 0;
        foreach (var finding in analysis.Findings)
        {
            AddFinding(analysis.NormalizedSourceHash, finding);
        }

        foreach (var diagnostic in analysis.Diagnostics)
        {
            if (diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
            {
                _ps0002Count++;
            }
            else if (diagnostic.Id == PurelySharpDiagnostics.MissingEnforcePureAttributeId)
            {
                _ps0004Count++;
            }
            else if (diagnostic.Id == PurelySharpDiagnostics.PurityExplanationId)
            {
                _ps0009Count++;
            }
            else if (diagnostic.Id == PurelySharpDiagnostics.ExceptionSummaryId)
            {
                _ps0010Count++;
            }
        }
    }

    public FuzzRunSummary Build(DateTimeOffset completedUtc, TimeSpan elapsed, string outputDirectory, int interestingCasesSaved)
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
        var nonActionableUnobservedOperationKinds = ImmutableHashSet.Create(
            OperationKind.None,
            OperationKind.UnaryOperator,
            OperationKind.BinaryOperator,
            OperationKind.BinaryPattern,
            OperationKind.Branch,
            OperationKind.Parenthesized,
            OperationKind.Empty,
            OperationKind.FlowAnonymousFunction,
            OperationKind.Labeled,
            OperationKind.Loop,
            OperationKind.MemberInitializer,
            OperationKind.PropertyInitializer,
            OperationKind.TranslatedQuery,
            OperationKind.OmittedArgument,
            OperationKind.ParameterInitializer,
            OperationKind.TupleBinary,
            OperationKind.TupleBinaryOperator,
            OperationKind.MethodBody,
            OperationKind.ConstructorBody,
            OperationKind.Discard,
            OperationKind.FlowCapture,
            OperationKind.FlowCaptureReference,
            OperationKind.IsNull,
            OperationKind.CaughtException,
            OperationKind.StaticLocalInitializationSemaphore,
            Enum.Parse<OperationKind>("CollectionElementInitializer"));
        var actionableUnobservedOperationKinds = Enum.GetValues<OperationKind>()
            .Where(kind => !observedOperationKinds.Contains(kind.ToString()))
            .Where(kind => !nonActionableUnobservedOperationKinds.Contains(kind))
            .Where(kind =>
            {
                var shapeId = RoslynShapeManifest.OperationShapeId(kind);
                return RoslynShapeManifest.EntriesByShapeId.TryGetValue(shapeId, out var manifestEntry) &&
                       manifestEntry.Classification != ShapeClassification.ParentHandled &&
                       manifestEntry.Classification != ShapeClassification.CSharpNotApplicable &&
                       manifestEntry.Classification != ShapeClassification.SyntaxShadow;
            })
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
            Ps0002Count = _ps0002Count,
            Ps0004Count = _ps0004Count,
            Ps0009Count = _ps0009Count,
            Ps0010Count = _ps0010Count,
            FamilyCounts = _familyCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            OperationKinds = _operationKinds.ToImmutableSortedDictionary(StringComparer.Ordinal),
            SyntaxKinds = _syntaxKinds.ToImmutableSortedDictionary(StringComparer.Ordinal),
            UnobservedOperationKinds = unobservedOperationKinds,
            ActionableUnobservedOperationKinds = actionableUnobservedOperationKinds,
            SamplerMode = _samplerMode,
            ManifestSurfaceCounts = manifestSurfaceCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            ManifestClassificationCounts = manifestClassificationCounts,
            GeneratorBackedShapeCount = RoslynShapeManifest.GeneratorBackedShapeIds.Length,
            GeneratorBackedShapesWithRegistryCount = registryCoveredShapeIds.Count,
            UnobservedGeneratorBackedShapes = unobservedGeneratorBackedShapes,
            PrimaryShapeCounts = _primaryShapeCounts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Findings = findings
        };
    }

    private void AddFinding(string normalizedSourceHash, FuzzFinding finding)
    {
        var aggregationKey = normalizedSourceHash + "|" + finding.Family + "|" + finding.Category + "|" + finding.Description + "|" +
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
        {
            target[pair.Key] = target.TryGetValue(pair.Key, out var count) ? count + pair.Value : pair.Value;
        }
    }

    private static void Increment(SortedDictionary<string, int> values, string key)
    {
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }
}

internal sealed record AnalyzerRunResult(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> Exceptions);

internal sealed class FixedAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _globalOptions;
    private readonly AnalyzerConfigOptions _emptyOptions =
        new FixedAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

    public FixedAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
    {
        _globalOptions = new FixedAnalyzerConfigOptions(globalOptions);
    }

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
}

internal sealed class FixedAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly ImmutableDictionary<string, string> _values;

    public FixedAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
    {
        _values = values;
    }

    public override bool TryGetValue(string key, out string value)
    {
        return _values.TryGetValue(key, out value!);
    }
}
