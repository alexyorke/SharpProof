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
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Tools.Fuzz;

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
                Console.WriteLine(
                    $"SharpProof fuzz run complete: {summary.CasesAnalyzed} cases, {summary.FindingCount} findings ({summary.UniqueFindingCount} unique), {summary.AnalyzerExceptionCount} analyzer exceptions.");
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
                                Usage: SharpProof.Fuzz [options]

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
                    options = options with { Duration = ReadDuration(args, ref i, arg, TimeSpan.FromSeconds) };
                    break;
                case "--minutes":
                    options = options with { Duration = ReadDuration(args, ref i, arg, TimeSpan.FromMinutes) };
                    break;
                case "--hours":
                    options = options with { Duration = ReadDuration(args, ref i, arg, TimeSpan.FromHours) };
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

        if (options.Iterations < 0) throw new ArgumentException("--iterations must be non-negative.");

        if (options.MaxInterestingCases < 0) throw new ArgumentException("--max-interesting must be non-negative.");

        if (options.MaxInterestingCasesPerFamily < 0)
            throw new ArgumentException("--max-interesting-per-family must be non-negative.");

        if (options.CheckpointEvery < 0) throw new ArgumentException("--checkpoint-every must be non-negative.");

        if (options.Parallelism <= 0) throw new ArgumentException("--parallelism must be positive.");

        if (options.Iterations == 0 && options.Duration is null)
            throw new ArgumentException(
                "Duration-only runs need --seconds, --minutes, or --hours when --iterations is 0.");

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
        return double.TryParse(value, out var parsed) && double.IsFinite(parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException($"{option} expects a finite non-negative number.");
    }

    private static TimeSpan ReadDuration(
        string[] args,
        ref int index,
        string option,
        Func<double, TimeSpan> createDuration)
    {
        var value = ReadDouble(args, ref index, option);
        try
        {
            return createDuration(value);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException($"{option} expects a duration within TimeSpan range.", ex);
        }
    }

    private static string ReadString(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length) throw new ArgumentException($"{option} expects a value.");

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
            "explicit_cases",
            cases.Length,
            null,
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

    public static Task<ImmutableArray<FuzzCaseAnalysis>> AnalyzeCasesAsync(
        IEnumerable<FuzzCase> fuzzCases,
        bool repeatAnalyzer = true,
        int? parallelism = null,
        CancellationToken cancellationToken = default)
    {
        var cases = fuzzCases.ToImmutableArray();
        var degreeOfParallelism = parallelism is > 0 ? parallelism.Value : DefaultAnalysisParallelism();
        return AnalyzeCasesCoreAsync(cases, repeatAnalyzer, degreeOfParallelism, cancellationToken);
    }

    private static async Task<ImmutableArray<FuzzCaseAnalysis>> AnalyzeCasesCoreAsync(
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
            if (!diagnosticSignatures.SequenceEqual(secondDiagnosticSignatures, StringComparer.Ordinal))
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "nondeterministic_diagnostics",
                    "Repeated analyzer runs produced different diagnostic signatures.",
                    null,
                    diagnosticSignatures.Concat(secondDiagnosticSignatures).ToImmutableArray()));

            foreach (var exception in secondDiagnostics.Exceptions)
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "analyzer_exception",
                    exception,
                    null,
                    ImmutableArray<string>.Empty));
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

    private static ImmutableArray<FuzzFinding>.Builder Evaluate(
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

        var sp0002Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
            .ToImmutableArray();
        var sp0010Diagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId)
            .ToImmutableArray();

        if (fuzzCase.Expectation.Sp0002 == Sp0002ExpectationKind.MustNotEmit && sp0002Diagnostics.Length > 0)
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "pure_sp0002",
                "A definitely-pure generated case produced SP0002.",
                null,
                ToDiagnosticSignatures(sp0002Diagnostics)));

        if (fuzzCase.Expectation.Sp0002 == Sp0002ExpectationKind.MustEmit && sp0002Diagnostics.Length == 0)
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "impure_missing_sp0002",
                "A definitely-impure generated case did not produce SP0002.",
                null,
                ToDiagnosticSignatures(diagnostics)));

        foreach (var diagnostic in sp0002Diagnostics)
        {
            if (MissingAnyRequiredProperties(diagnostic, fuzzCase.Expectation.RequiredSp0002Properties))
                findings.Add(new FuzzFinding(
                    fuzzCase.Name,
                    fuzzCase.Family,
                    "missing_sp0002_evidence",
                    "SP0002 did not include stable category/rule/operation evidence.",
                    null,
                    ImmutableArray.Create(ToDiagnosticSignature(diagnostic))));

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

        if (fuzzCase.Expectation.Sp0010 == Sp0010ExpectationKind.MustNotEmit && sp0010Diagnostics.Length > 0)
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "unexpected_sp0010",
                "A generated case unexpectedly produced SP0010.",
                null,
                ToDiagnosticSignatures(sp0010Diagnostics)));

        if (fuzzCase.Expectation.Sp0010 == Sp0010ExpectationKind.MustEmit && sp0010Diagnostics.Length == 0)
            findings.Add(new FuzzFinding(
                fuzzCase.Name,
                fuzzCase.Family,
                "missing_sp0010",
                "A generated case expected to produce SP0010 did not do so.",
                null,
                ToDiagnosticSignatures(diagnostics)));

        if (fuzzCase.Expectation.Sp0010 != Sp0010ExpectationKind.Ignore)
        {
            foreach (var diagnostic in sp0010Diagnostics)
                if (MissingAnyRequiredProperties(diagnostic, fuzzCase.Expectation.RequiredSp0010Properties))
                    findings.Add(new FuzzFinding(
                        fuzzCase.Name,
                        fuzzCase.Family,
                        "missing_sp0010_evidence",
                        "SP0010 did not include stable exception evidence.",
                        null,
                        ImmutableArray.Create(ToDiagnosticSignature(diagnostic))));

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

        foreach (var node in syntaxTree.GetRoot(cancellationToken).DescendantNodes())
        {
            var operation = semanticModel.GetOperation(node, cancellationToken);
            if (operation is null) continue;

            foreach (var descendant in operation.DescendantsAndSelf()) Increment(counts, descendant.Kind.ToString());
        }

        return counts.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static ImmutableSortedDictionary<string, int> CollectSyntaxKinds(SyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        Increment(counts, ((SyntaxKind)root.RawKind).ToString());

        foreach (var nodeOrToken in root.DescendantNodesAndTokens(descendIntoTrivia: true))
            Increment(counts, ((SyntaxKind)nodeOrToken.RawKind).ToString());

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            Increment(counts, ((SyntaxKind)trivia.RawKind).ToString());
            var structure = trivia.GetStructure();
            if (structure is null) continue;

            Increment(counts, ((SyntaxKind)structure.RawKind).ToString());
            foreach (var nodeOrToken in structure.DescendantNodesAndTokens(descendIntoTrivia: true))
                Increment(counts, ((SyntaxKind)nodeOrToken.RawKind).ToString());
        }

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

    private static int DefaultAnalysisParallelism()
    {
        return Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
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
    private static readonly Lazy<ImmutableSortedDictionary<string, ImmutableArray<ShapeRegistryEntry>>>
        RegistryByPrimaryShape =
            new(() => RegistryEntries
                .SelectMany(registryEntry => registryEntry.PrimaryShapeIds.Select(shapeId =>
                    new KeyValuePair<string, ShapeRegistryEntry>(shapeId, registryEntry)))
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToImmutableSortedDictionary(
                    group => group.Key,
                    group => group.Select(pair => pair.Value)
                        .Distinct()
                        .OrderBy(registryEntry => registryEntry.Id, StringComparer.Ordinal)
                        .ToImmutableArray(),
                    StringComparer.Ordinal));

    private static readonly Lazy<ImmutableArray<string>> OrderedGeneratorBackedShapeIds =
        new(() => RegistryByPrimaryShape.Value.Keys.ToImmutableArray());

    private readonly int _seed;

    public FuzzCaseGenerator(int seed)
    {
        _seed = seed;
    }

    public static ImmutableArray<ShapeRegistryEntry> RegistryEntries { get; } = ImmutableArray.Create(
        new ShapeRegistryEntry(
            "PureArithmetic",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureArithmetic),
        new ShapeRegistryEntry(
            "PureStringConcat",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray.Create("AddExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureStringConcat),
        new ShapeRegistryEntry(
            "PureListPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ListPattern)),
            ImmutableArray.Create("ListPattern"),
            ImmutableArray.Create("ListPattern"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureListPattern),
        new ShapeRegistryEntry(
            "PureCollectionExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CollectionExpression)),
            ImmutableArray.Create("CollectionExpression"),
            ImmutableArray.Create("CollectionExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureCollectionExpression),
        new ShapeRegistryEntry(
            "PureInterpolatedString",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedString)),
            ImmutableArray.Create("InterpolatedString"),
            ImmutableArray.Create("InterpolatedStringExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureInterpolatedString),
        new ShapeRegistryEntry(
            "PureUtf8String",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Utf8String)),
            ImmutableArray.Create("Utf8String"),
            ImmutableArray.Create("Utf8StringLiteralExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureUtf8String),
        new ShapeRegistryEntry(
            "PureArrayCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ArrayCreation)),
            ImmutableArray.Create("ArrayCreation"),
            ImmutableArray.Create("ArrayCreationExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureArrayCreation),
        new ShapeRegistryEntry(
            "PureNestedOwnershipChain",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference", "SimpleAssignment", "ObjectCreation"),
            ImmutableArray.Create("SimpleMemberAccessExpression", "SimpleAssignmentExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureNestedOwnershipChain),
        new ShapeRegistryEntry(
            "ImpureOwnershipEscapeChain",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ObjectCreation)),
            ImmutableArray.Create("ObjectCreation", "PropertyReference", "Return"),
            ImmutableArray.Create("ObjectCreationExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureOwnershipEscapeChain),
        new ShapeRegistryEntry(
            "ImpureConsoleWrite",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Invocation)),
            ImmutableArray.Create("Invocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureConsoleWrite),
        new ShapeRegistryEntry(
            "ImpureDynamicDispatch",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicInvocation)),
            ImmutableArray.Create("DynamicInvocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDynamicDispatch),
        new ShapeRegistryEntry(
            "ImpureDelegateInvoke",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Invocation)),
            ImmutableArray.Create("Invocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDelegateInvoke),
        new ShapeRegistryEntry(
            "ImpureThrowExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureThrowExpression),
        new ShapeRegistryEntry(
            "ExceptionDirectThrowInvalidOperation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            ImpureWithExceptionExpectation(),
            false,
            false,
            BuildExceptionDirectThrowInvalidOperation),
        new ShapeRegistryEntry(
            "ExceptionGuardedThrowArgumentNull",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            ImpureWithExceptionExpectation(),
            false,
            false,
            BuildExceptionGuardedThrowArgumentNull),
        new ShapeRegistryEntry(
            "ExceptionThrowExpressionFormatException",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowExpression"),
            ImpureWithExceptionExpectation(),
            false,
            false,
            BuildExceptionThrowExpressionFormatException),
        new ShapeRegistryEntry(
            "ExceptionCaughtInternalThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Try),
                RoslynShapeManifest.OperationShapeId(OperationKind.CatchClause)),
            ImmutableArray.Create("Try", "CatchClause", "Throw"),
            ImmutableArray.Create("TryStatement", "CatchClause", "ThrowStatement"),
            ImpureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionCaughtInternalThrow),
        new ShapeRegistryEntry(
            "ExceptionDeadBranchThrow",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Conditional", "Throw"),
            ImmutableArray.Create("IfStatement", "ThrowStatement"),
            PureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionDeadBranchThrow),
        new ShapeRegistryEntry(
            "ExceptionGuardedSafeDivideByZeroExcluded",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Conditional", "Binary"),
            ImmutableArray.Create("IfStatement", "DivideExpression"),
            PureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionGuardedSafeDivideByZeroExcluded),
        new ShapeRegistryEntry(
            "ExceptionGuardedNullDereferenceExcluded",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("Conditional", "PropertyReference"),
            ImmutableArray.Create("IfStatement"),
            PureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionGuardedNullDereferenceExcluded),
        new ShapeRegistryEntry(
            "ExceptionDefiniteDivideByZero",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray.Create("DivideExpression"),
            ExceptionWithOptionalSp0002Expectation(),
            false,
            false,
            BuildExceptionDefiniteDivideByZero),
        new ShapeRegistryEntry(
            "ExceptionDefiniteNullReference",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray.Create("SimpleMemberAccessExpression"),
            ExceptionWithOptionalSp0002Expectation(),
            false,
            false,
            BuildExceptionDefiniteNullReference),
        new ShapeRegistryEntry(
            "ExceptionUsingDisposeThrows",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration)),
            ImmutableArray.Create("UsingDeclaration"),
            ImmutableArray.Create("LocalDeclarationStatement"),
            ExceptionWithOptionalSp0002Expectation(),
            false,
            false,
            BuildExceptionUsingDisposeThrows),
        new ShapeRegistryEntry(
            "ExceptionInvokedLocalFunctionThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.LocalFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("LocalFunction", "Throw"),
            ImmutableArray.Create("LocalFunctionStatement", "ThrowStatement"),
            ExceptionWithOptionalSp0002Expectation().RequireExceptionEdgesOnAnySp0010(),
            false,
            false,
            BuildExceptionInvokedLocalFunctionThrow),
        new ShapeRegistryEntry(
            "ExceptionInvokedLambdaThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("AnonymousFunction", "Throw"),
            ImmutableArray.Create("ParenthesizedLambdaExpression", "ThrowExpression"),
            ExceptionWithOptionalSp0002Expectation().RequireExceptionEdgesOnAnySp0010(),
            false,
            false,
            BuildExceptionInvokedLambdaThrow),
        new ShapeRegistryEntry(
            "ImpureFieldWrite",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.SimpleAssignment),
                RoslynShapeManifest.OperationShapeId(OperationKind.FieldReference)),
            ImmutableArray.Create("SimpleAssignment", "FieldReference"),
            ImmutableArray.Create("SimpleAssignmentExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureFieldWrite),
        new ShapeRegistryEntry(
            "ImpureAmbientDateTime",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray.Create("SimpleMemberAccessExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureAmbientDateTime),
        new ShapeRegistryEntry(
            "ImpureAwaitTaskDelay",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Await)),
            ImmutableArray.Create("Await"),
            ImmutableArray.Create("AwaitExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureAwaitTaskDelay),
        new ShapeRegistryEntry(
            "ImpureLockSection",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Lock)),
            ImmutableArray.Create("Lock"),
            ImmutableArray.Create("LockStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureLockSection),
        new ShapeRegistryEntry(
            "ImpureUsingStandardOutput",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration)),
            ImmutableArray.Create("UsingDeclaration"),
            ImmutableArray.Create("LocalDeclarationStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureUsingStandardOutput),
        new ShapeRegistryEntry(
            "ImpureTryCatch",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Try),
                RoslynShapeManifest.OperationShapeId(OperationKind.CatchClause)),
            ImmutableArray.Create("Try", "CatchClause"),
            ImmutableArray.Create("TryStatement", "CatchClause"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureTryCatch),
        new ShapeRegistryEntry(
            "PureConditionalAccessCoalesce",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.ConditionalAccess),
                RoslynShapeManifest.OperationShapeId(OperationKind.Coalesce)),
            ImmutableArray.Create("ConditionalAccess", "Coalesce"),
            ImmutableArray.Create("ConditionalAccessExpression", "CoalesceExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureConditionalAccessCoalesce),
        new ShapeRegistryEntry(
            "PureIsTypeCheck",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.IsType)),
            ImmutableArray.Create("IsType"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureIsTypeCheck),
        new ShapeRegistryEntry(
            "PureNegatedPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.NegatedPattern)),
            ImmutableArray.Create("NegatedPattern"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureNegatedPattern),
        new ShapeRegistryEntry(
            "PureSwitchStatement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Switch)),
            ImmutableArray.Create("Switch", "SwitchCase"),
            ImmutableArray.Create("SwitchStatement"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureSwitchStatement),
        new ShapeRegistryEntry(
            "ImpureUsingStatement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Using)),
            ImmutableArray.Create("Using"),
            ImmutableArray.Create("UsingStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureUsingStatement),
        new ShapeRegistryEntry(
            "PureCompoundAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CompoundAssignment)),
            ImmutableArray.Create("CompoundAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureCompoundAssignment),
        new ShapeRegistryEntry(
            "PureCoalesceAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CoalesceAssignment)),
            ImmutableArray.Create("CoalesceAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureCoalesceAssignment),
        new ShapeRegistryEntry(
            "PureDeconstructionAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeconstructionAssignment)),
            ImmutableArray.Create("DeconstructionAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDeconstructionAssignment),
        new ShapeRegistryEntry(
            "PureIncrement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Increment)),
            ImmutableArray.Create("Increment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureIncrement),
        new ShapeRegistryEntry(
            "PureDecrement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Decrement)),
            ImmutableArray.Create("Decrement"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDecrement),
        new ShapeRegistryEntry(
            "ImpureDeclarationExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeclarationExpression)),
            ImmutableArray.Create("DeclarationExpression"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureDeclarationExpression),
        new ShapeRegistryEntry(
            "PureDeclarationPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeclarationPattern)),
            ImmutableArray.Create("DeclarationPattern"),
            ImmutableArray.Create("DeclarationPattern"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDeclarationPattern),
        new ShapeRegistryEntry(
            "ImpureTypeParameterObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.TypeParameterObjectCreation)),
            ImmutableArray.Create("TypeParameterObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureTypeParameterObjectCreation),
        new ShapeRegistryEntry(
            "ImpureEventAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.EventAssignment)),
            ImmutableArray.Create("EventAssignment", "EventReference"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureEventAssignment),
        new ShapeRegistryEntry(
            "PureAnonymousObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousObjectCreation)),
            ImmutableArray.Create("AnonymousObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureAnonymousObjectCreation),
        new ShapeRegistryEntry(
            "PureDefaultValue",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DefaultValue)),
            ImmutableArray.Create("DefaultValue"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDefaultValue),
        new ShapeRegistryEntry(
            "PureSizeOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.SizeOf)),
            ImmutableArray.Create("SizeOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureSizeOf),
        new ShapeRegistryEntry(
            "PureTypeOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.TypeOf)),
            ImmutableArray.Create("TypeOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureTypeOf),
        new ShapeRegistryEntry(
            "PureNameOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.NameOf)),
            ImmutableArray.Create("NameOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureNameOf),
        new ShapeRegistryEntry(
            "ImpureDynamicIndexerAccess",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicIndexerAccess)),
            ImmutableArray.Create("DynamicIndexerAccess"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDynamicIndexerAccess),
        new ShapeRegistryEntry(
            "ImpureDynamicObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicObjectCreation)),
            ImmutableArray.Create("DynamicObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDynamicObjectCreation),
        new ShapeRegistryEntry(
            "PureTuple",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Tuple)),
            ImmutableArray.Create("Tuple"),
            ImmutableArray.Create("TupleExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureTuple),
        new ShapeRegistryEntry(
            "ImpureInterfaceGetter",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureInterfaceGetter),
        new ShapeRegistryEntry(
            "PureRecursivePattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.RecursivePattern)),
            ImmutableArray.Create("RecursivePattern"),
            ImmutableArray.Create("RecursivePattern"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureRecursivePattern),
        new ShapeRegistryEntry(
            "PureSpreadCollectionExpression",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.CollectionExpression),
                RoslynShapeManifest.OperationShapeId(OperationKind.Spread)),
            ImmutableArray.Create("CollectionExpression", "Spread"),
            ImmutableArray.Create("CollectionExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureSpreadCollectionExpression),
        new ShapeRegistryEntry(
            "PureSwitchExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.SwitchExpression)),
            ImmutableArray.Create("SwitchExpression"),
            ImmutableArray.Create("SwitchExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureSwitchExpression),
        new ShapeRegistryEntry(
            "PureRangeSlice",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Range)),
            ImmutableArray.Create("Range"),
            ImmutableArray.Create("RangeExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureRangeSlice),
        new ShapeRegistryEntry(
            "PureYieldReturn",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.YieldReturn)),
            ImmutableArray.Create("YieldReturn"),
            ImmutableArray.Create("YieldReturnStatement"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureYieldReturn),
        new ShapeRegistryEntry(
            "ImpureWithExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.With)),
            ImmutableArray.Create("With"),
            ImmutableArray.Create("WithExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureWithExpression),
        new ShapeRegistryEntry(
            "PureAnonymousFunction",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction)),
            ImmutableArray.Create("AnonymousFunction"),
            ImmutableArray.Create("SimpleLambdaExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureAnonymousFunction),
        new ShapeRegistryEntry(
            "PureDelegateCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DelegateCreation)),
            ImmutableArray.Create("DelegateCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureDelegateCreation),
        new ShapeRegistryEntry(
            "PureImplicitIndexerReference",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ImplicitIndexerReference)),
            ImmutableArray.Create("ImplicitIndexerReference"),
            ImmutableArray.Create("ElementAccessExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureImplicitIndexerReference),
        new ShapeRegistryEntry(
            "PureInterpolatedStringHandler",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringHandlerCreation),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAddition),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAppendLiteral),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAppendFormatted),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringHandlerArgumentPlaceholder)),
            ImmutableArray.Create("InterpolatedStringHandlerCreation", "InterpolatedStringAddition",
                "InterpolatedStringAppendLiteral", "InterpolatedStringAppendFormatted",
                "InterpolatedStringHandlerArgumentPlaceholder"),
            ImmutableArray.Create("InterpolatedStringExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureInterpolatedStringHandler),
        new ShapeRegistryEntry(
            "ImpureAddressOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AddressOf)),
            ImmutableArray.Create("AddressOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            true,
            false,
            BuildImpureAddressOf),
        new ShapeRegistryEntry(
            "PureInlineArrayAccess",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.InlineArrayAccess)),
            ImmutableArray.Create("InlineArrayAccess"),
            ImmutableArray.Create("ElementAccessExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureInlineArrayAccess),
        new ShapeRegistryEntry(
            "ImpureFunctionPointer",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.FunctionPointerInvocation)),
            ImmutableArray.Create("FunctionPointerInvocation"),
            ImmutableArray.Create("FunctionPointerType"),
            FuzzExpectation.DefinitelyImpure(),
            true,
            false,
            BuildImpureFunctionPointer),
        new ShapeRegistryEntry(
            "PureNestedLambdaLocalFunction",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.LocalFunction)),
            ImmutableArray.Create("AnonymousFunction", "LocalFunction"),
            ImmutableArray.Create("SimpleLambdaExpression", "LocalFunctionStatement"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureNestedLambdaLocalFunction),
        new ShapeRegistryEntry(
            "PureTuplePatternSwitch",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Tuple),
                RoslynShapeManifest.OperationShapeId(OperationKind.SwitchExpression)),
            ImmutableArray.Create("Tuple", "SwitchExpression"),
            ImmutableArray.Create("TupleExpression", "SwitchExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureTuplePatternSwitch),
        new ShapeRegistryEntry(
            "ImpureUsingAwaitDelegateFlow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration),
                RoslynShapeManifest.OperationShapeId(OperationKind.Await),
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction)),
            ImmutableArray.Create("UsingDeclaration", "Await", "AnonymousFunction"),
            ImmutableArray.Create("LocalDeclarationStatement", "AwaitExpression", "ParenthesizedLambdaExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureUsingAwaitDelegateFlow));

    public FuzzCase Next(int index)
    {
        var shapeIds = OrderedGeneratorBackedShapeIds.Value;
        var shapeId = shapeIds[index % shapeIds.Length];
        var variant = index / shapeIds.Length;
        return GenerateForShapeCore(shapeId, variant, index);
    }

    private FuzzCase GenerateForShapeCore(string shapeId, int variant, int index)
    {
        if (!RegistryByPrimaryShape.Value.TryGetValue(shapeId, out var entries))
            throw new ArgumentException($"Unknown generator-backed shape '{shapeId}'.", nameof(shapeId));

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
            $"{index:000000}-{registryEntry.Id}",
            registryEntry.Id,
            source,
            registryEntry.AllowUnsafe ||
            source.Contains("unsafe", StringComparison.Ordinal) ||
            source.Contains("delegate*", StringComparison.Ordinal),
            registryEntry.Expectation,
            registryEntry.PrimaryShapeIds,
            registryEntry.ExpectedOperationKinds,
            registryEntry.ExpectedSyntaxKinds);
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

    private static string BuildPureNestedOwnershipChain(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

                 public sealed class {{className}}Box
                 {
                     public int Value;
                 }

                 public sealed class {{className}}Middle
                 {
                     public {{className}}Box Value { get; init; }
                 }

                 public sealed class {{className}}Outer
                 {
                     public {{className}}Middle Value { get; init; }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod()
                     {
                         var outer = new {{className}}Outer { Value = new {{className}}Middle { Value = new {{className}}Box() } };
                         outer.Value.Value.Value = 1;
                         return outer.Value.Value.Value;
                     }
                 }
                 """;
    }

    private static string BuildImpureOwnershipEscapeChain(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

                 public sealed class {{className}}Box
                 {
                     public int Value;
                 }

                 public sealed class {{className}}Middle
                 {
                     public {{className}}Box Value { get; init; }
                 }

                 public sealed class {{className}}Outer
                 {
                     public {{className}}Middle Value { get; init; }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public {{className}}Outer TestMethod()
                     {
                         return new {{className}}Outer { Value = new {{className}}Middle { Value = new {{className}}Box() } };
                     }
                 }
                 """;
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
                 using SharpProof.Attributes;

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

    private static string BuildImpureTryCatch(int index, Random random, string className)
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

    private static string BuildPureConditionalAccessCoalesce(int index, Random random, string className)
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

    private static string BuildPureIsTypeCheck(int index, Random random, string className)
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

    private static string BuildPureNegatedPattern(int index, Random random, string className)
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

    private static string BuildPureSwitchStatement(int index, Random random, string className)
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

    private static string BuildImpureUsingStatement(int index, Random random, string className)
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

    private static string BuildPureCompoundAssignment(int index, Random random, string className)
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

    private static string BuildPureCoalesceAssignment(int index, Random random, string className)
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

    private static string BuildPureDeconstructionAssignment(int index, Random random, string className)
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

    private static string BuildPureIncrement(int index, Random random, string className)
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

    private static string BuildPureDecrement(int index, Random random, string className)
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

    private static string BuildImpureDeclarationExpression(int index, Random random, string className)
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

    private static string BuildPureDeclarationPattern(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(object value)
                {
                    return value is int number ? number : 0;
                }
            """);
    }

    private static string BuildImpureTypeParameterObjectCreation(int index, Random random, string className)
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

    private static string BuildImpureEventAssignment(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using SharpProof.Attributes;

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

    private static string BuildPureAnonymousObjectCreation(int index, Random random, string className)
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

    private static string BuildPureDefaultValue(int index, Random random, string className)
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

    private static string BuildPureSizeOf(int index, Random random, string className)
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

    private static string BuildPureTypeOf(int index, Random random, string className)
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

    private static string BuildPureNameOf(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int value)
                {
                    return nameof(value).Length;
                }
            """);
    }

    private static string BuildImpureDynamicIndexerAccess(int index, Random random, string className)
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

    private static string BuildImpureDynamicObjectCreation(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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

    private static string BuildPureTuple(int index, Random random, string className)
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

    private static string BuildImpureInterfaceGetter(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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

    private static string BuildPureRecursivePattern(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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

    private static string BuildPureSpreadCollectionExpression(int index, Random random, string className)
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

    private static string BuildPureSwitchExpression(int index, Random random, string className)
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

    private static string BuildPureRangeSlice(int index, Random random, string className)
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

    private static string BuildPureYieldReturn(int index, Random random, string className)
    {
        return $$"""
                 using System.Collections.Generic;
                 using SharpProof.Attributes;

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

    private static string BuildImpureWithExpression(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using SharpProof.Attributes;

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

    private static string BuildPureAnonymousFunction(int index, Random random, string className)
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

    private static string BuildPureDelegateCreation(int index, Random random, string className)
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

    private static string BuildPureImplicitIndexerReference(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using SharpProof.Attributes;

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

    private static string BuildPureInterpolatedStringHandler(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using System.Runtime.CompilerServices;
                 using SharpProof.Attributes;

                 [InterpolatedStringHandler]
                 public ref struct {{className}}Handler
                 {
                     public {{className}}Handler(int literalLength, int formattedCount, int value) { }
                     public void AppendLiteral(string value) { }
                     public void AppendFormatted<T>(T value) { }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public void TestMethod(int value)
                     {
                         Log(value, $"left={value}" + $"right={value}");
                     }

                     private void Log(int value, [InterpolatedStringHandlerArgument("value")] {{className}}Handler handler) { }
                 }
                 """;
    }

    private static string BuildImpureAddressOf(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

                 public unsafe class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod()
                     {
                         int value = 1;
                         int* pointer = &value;
                         return *pointer;
                     }
                 }
                 """;
    }

    private static string BuildPureInlineArrayAccess(int index, Random random, string className)
    {
        return $$"""
                 using System.Runtime.CompilerServices;
                 using SharpProof.Attributes;

                 [InlineArray(4)]
                 public struct {{className}}Buffer
                 {
                     private int _element0;
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod()
                     {
                         {{className}}Buffer buffer = default;
                         return buffer[0];
                     }
                 }
                 """;
    }

    private static string BuildImpureFunctionPointer(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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

    private static string BuildPureNestedLambdaLocalFunction(int index, Random random, string className)
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

    private static string BuildPureTuplePatternSwitch(int index, Random random, string className)
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

    private static string BuildImpureUsingAwaitDelegateFlow(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using System.Threading.Tasks;
                 using SharpProof.Attributes;

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
            Sp0002ExpectationKind.MustEmit,
            Sp0010ExpectationKind.MustEmit,
            ImmutableArray.Create(
                SharpProofDiagnostics.ImpurityCategoryProperty,
                SharpProofDiagnostics.ImpurityRuleProperty,
                SharpProofDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                SharpProofDiagnostics.ExceptionTypesProperty,
                SharpProofDiagnostics.ExceptionCategoriesProperty,
                SharpProofDiagnostics.ExceptionSourcesProperty),
            ImmutableArray<string>.Empty);
    }

    private static FuzzExpectation ImpureWithoutExceptionExpectation()
    {
        return new FuzzExpectation(
            Sp0002ExpectationKind.MustEmit,
            Sp0010ExpectationKind.MustNotEmit,
            ImmutableArray.Create(
                SharpProofDiagnostics.ImpurityCategoryProperty,
                SharpProofDiagnostics.ImpurityRuleProperty,
                SharpProofDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                SharpProofDiagnostics.ExceptionTypesProperty,
                SharpProofDiagnostics.ExceptionCategoriesProperty,
                SharpProofDiagnostics.ExceptionSourcesProperty),
            ImmutableArray<string>.Empty);
    }

    private static FuzzExpectation ExceptionWithOptionalSp0002Expectation()
    {
        return new FuzzExpectation(
            Sp0002ExpectationKind.MayEmitConservatively,
            Sp0010ExpectationKind.MustEmit,
            ImmutableArray.Create(
                SharpProofDiagnostics.ImpurityCategoryProperty,
                SharpProofDiagnostics.ImpurityRuleProperty,
                SharpProofDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                SharpProofDiagnostics.ExceptionTypesProperty,
                SharpProofDiagnostics.ExceptionCategoriesProperty,
                SharpProofDiagnostics.ExceptionSourcesProperty),
            ImmutableArray<string>.Empty);
    }

    private static FuzzExpectation PureWithoutExceptionExpectation()
    {
        return new FuzzExpectation(
            Sp0002ExpectationKind.MustNotEmit,
            Sp0010ExpectationKind.MustNotEmit,
            ImmutableArray.Create(
                SharpProofDiagnostics.ImpurityCategoryProperty,
                SharpProofDiagnostics.ImpurityRuleProperty,
                SharpProofDiagnostics.ImpurityOperationKindProperty),
            ImmutableArray.Create(
                SharpProofDiagnostics.ExceptionTypesProperty,
                SharpProofDiagnostics.ExceptionCategoriesProperty,
                SharpProofDiagnostics.ExceptionSourcesProperty),
            ImmutableArray<string>.Empty);
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
                 using SharpProof.Attributes;

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
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Select(line => line.Length == 0 ? line : padding + line));
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
        var nonActionableUnobservedOperationKinds = ImmutableHashSet.Create(
            OperationKind.Invalid,
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
            OperationKind.InterpolatedStringAppendInvalid,
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

internal sealed record AnalyzerRunResult(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<string> Exceptions);

internal sealed class FixedAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _emptyOptions =
        new FixedAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

    public FixedAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
    {
        GlobalOptions = new FixedAnalyzerConfigOptions(globalOptions);
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return _emptyOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return _emptyOptions;
    }
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
