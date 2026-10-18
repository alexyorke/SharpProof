using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer;
using SharpProof.Testing;

namespace SharpProof.ContractForGenerator.Test;

internal static class GeneratorTestHost
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
    private static readonly ImmutableArray<MetadataReference> References =
        [.. TestMetadataReferences.SortedPlatform,
            MetadataReference.CreateFromFile(
                typeof(ContractForAttribute).Assembly.Location)];

    internal static CSharpCompilation CreateCompilation(
        params (string Path, string Source)[] sources)
    {
        System.ArgumentNullException.ThrowIfNull(sources, nameof(sources));
        return CreateCompilation(References, sources);
    }

    internal static CSharpCompilation CreateCompilationWithoutAttributes(
        params (string Path, string Source)[] sources)
    {
        System.ArgumentNullException.ThrowIfNull(sources, nameof(sources));
        return CreateCompilation(TestMetadataReferences.SortedPlatform, sources);
    }

    internal static CSharpCompilation CreateCompilationWithReference(
        string referenceSource,
        params (string Path, string Source)[] sources)
    {
        System.ArgumentNullException.ThrowIfNull(sources, nameof(sources));
        System.ArgumentNullException.ThrowIfNull(referenceSource);
        var reference = CreateCompilation(
            References,
            ("ReferencedContracts.cs", referenceSource));
        return CreateCompilation(
            References.Add(reference.ToMetadataReference()),
            sources);
    }

    private static CSharpCompilation CreateCompilation(
        ImmutableArray<MetadataReference> references,
        params (string Path, string Source)[] sources)
    {

        System.ArgumentNullException.ThrowIfNull(sources, nameof(sources));
        if (references.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(references));
        }
        var trees = sources.Select(source =>
            CSharpSyntaxTree.ParseText(
                source.Source,
                ParseOptions,
                source.Path));
        var compilation = CSharpCompilation.Create(
            "ContractForGeneratorTests",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true,
                allowUnsafe: true));
        RequireNoErrors(compilation);
        return compilation;
    }

    internal static AnalyzerRun RunAnalyzer(
        CSharpCompilation compilation,
        Dictionary<string, string>? globalOptions = null)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return new AnalyzerRun(CollectDiagnostics(
            compilation,
            globalOptions,
            analyzer: null,
            ImmutableArray<Diagnostic>.Empty));
    }

    internal static AnalyzerRun RunWithAnalyzer(
        CSharpCompilation compilation,
        DiagnosticAnalyzer analyzer,
        Dictionary<string, string>? globalOptions = null)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(analyzer);
        return new AnalyzerRun(CollectDiagnostics(
            compilation,
            globalOptions,
            analyzer,
            ImmutableArray<Diagnostic>.Empty));
    }

    internal static GeneratorRun RunWithDefaultGenerator(
        CSharpCompilation compilation,
        GeneratorDriver? previousDriver = null,
        Dictionary<string, string>? globalOptions = null)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var driver = previousDriver ?? CreateDriver(globalOptions);
        return RunCore(compilation, driver, globalOptions);
    }

    internal static GeneratorRun RunWithGenerator(
        CSharpCompilation compilation,
        IIncrementalGenerator generator,
        Dictionary<string, string>? globalOptions = null)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(generator);
        return RunCore(
            compilation,
            CreateDriver(generator, globalOptions),
            globalOptions);
    }

    private static GeneratorRun RunCore(
        CSharpCompilation compilation,
        GeneratorDriver driver,
        Dictionary<string, string>? globalOptions)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(driver);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var driverDiagnostics);
        var runResult = driver.GetRunResult();
        var diagnostics = CollectDiagnostics(
            (CSharpCompilation)outputCompilation,
            globalOptions,
            analyzer: null,
            runResult.Diagnostics.Concat(driverDiagnostics));
        return new GeneratorRun(
            driver,
            runResult,
            diagnostics);
    }

    private static ImmutableArray<Diagnostic> CollectDiagnostics(
        CSharpCompilation compilation,
        Dictionary<string, string>? globalOptions,
        DiagnosticAnalyzer? analyzer,
        IEnumerable<Diagnostic> existing)
    {
        System.ArgumentNullException.ThrowIfNull(existing);
        RequireNoErrors(compilation);
        return existing
            .Concat(AnalyzeFinalCompilation(
                compilation,
                globalOptions,
                analyzer))
            .Distinct(DiagnosticIdentityComparer.Instance)
            .OrderBy(
                static diagnostic =>
                    diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Location.SourceTree?.FilePath),
                StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Location.SourceSpan.Start))
            .ThenBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id), StringComparer.Ordinal)
            .ThenBy(
                static diagnostic =>
                    diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> AnalyzeFinalCompilation(
        CSharpCompilation compilation,
        Dictionary<string, string>? globalOptions,
        DiagnosticAnalyzer? analyzer)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var options = new AnalyzerOptions(
            [],
            new DictionaryAnalyzerConfigOptionsProvider(
                globalOptions ??
                new Dictionary<string, string>(StringComparer.Ordinal)));
        var withAnalyzers = compilation.WithAnalyzers(
            [analyzer ?? new SharpProofAnalyzer()],
            new CompilationWithAnalyzersOptions(
                options,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));
        return [.. withAnalyzers.GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult()
            .Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id.StartsWith("SPCF", StringComparison.Ordinal) ||
                diagnostic.Id == "AD0001"))];
    }

    internal static ImmutableArray<string> DiagnosticKeys(
        GeneratorRun run)
    {
        System.ArgumentNullException.ThrowIfNull(run);
        return [.. run.Diagnostics.Select(static diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id + "|" +
            diagnostic.Location.SourceTree?.FilePath + "|" +
            diagnostic.Location.SourceSpan.Start + "|" +
            diagnostic.GetMessage(CultureInfo.InvariantCulture)))];
    }

    private static CSharpGeneratorDriver CreateDriver(
        Dictionary<string, string>? globalOptions)
    {
        return CreateDriver(
            new ContractForValidatorGenerator(),
            globalOptions);
    }

    private static CSharpGeneratorDriver CreateDriver(
        IIncrementalGenerator generator,
        Dictionary<string, string>? globalOptions)
    {
        System.ArgumentNullException.ThrowIfNull(generator);
        return CSharpGeneratorDriver.Create(
            generators: [
                generator.AsSourceGenerator()
            ],
            parseOptions: ParseOptions,
            optionsProvider: globalOptions == null
                ? null
                : new DictionaryAnalyzerConfigOptionsProvider(globalOptions),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }

    private static void RequireNoErrors(Compilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .ToImmutableArray();
        if (!errors.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        }
    }
}

internal sealed class GeneratorRun(
    GeneratorDriver driver,
    GeneratorDriverRunResult runResult,
    ImmutableArray<Diagnostic> diagnostics)
{
    internal GeneratorDriver Driver { get; } = (driver ?? throw new System.ArgumentNullException(nameof(driver)));
    internal GeneratorDriverRunResult RunResult { get; } = (runResult ?? throw new System.ArgumentNullException(nameof(runResult)));
    public ImmutableArray<Diagnostic> Diagnostics { get; } = (diagnostics.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(diagnostics)) : diagnostics);
}

internal sealed class AnalyzerRun(ImmutableArray<Diagnostic> diagnostics)
{
    public ImmutableArray<Diagnostic> Diagnostics { get; } = (diagnostics.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(diagnostics)) : diagnostics);
}

internal sealed class DiagnosticIdentityComparer :
    IEqualityComparer<Diagnostic>
{
    internal static DiagnosticIdentityComparer Instance { get; } = new();

    private DiagnosticIdentityComparer()
    {
    }

    public bool Equals(Diagnostic? left, Diagnostic? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null)
        {
            return false;
        }

        return string.Equals(left.Id, right.Id, StringComparison.Ordinal) &&
               Equals(left.Location.SourceTree, right.Location.SourceTree) &&
               left.Location.SourceSpan == right.Location.SourceSpan &&
               string.Equals(
                   left.GetMessage(CultureInfo.InvariantCulture),
                   right.GetMessage(CultureInfo.InvariantCulture),
                   StringComparison.Ordinal);
    }

    public int GetHashCode(Diagnostic diagnostic)
    {
        System.ArgumentNullException.ThrowIfNull(diagnostic);
        unchecked
        {
            var hash = StringComparer.Ordinal.GetHashCode(diagnostic.Id);
            hash = hash * 31 + diagnostic.Location.SourceSpan.GetHashCode();
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                diagnostic.GetMessage(CultureInfo.InvariantCulture));
            return hash;
        }
    }
}
