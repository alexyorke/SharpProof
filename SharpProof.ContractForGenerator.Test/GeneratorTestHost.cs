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
        CreateReferences();

    internal static CSharpCompilation CreateCompilation(
        params (string Path, string Source)[] sources)
    {
        return CreateCompilation(References, sources);
    }

    internal static CSharpCompilation CreateCompilationWithoutAttributes(
        params (string Path, string Source)[] sources)
    {
        return CreateCompilation(CreateReferences(includeAttributes: false), sources);
    }

    internal static CSharpCompilation CreateCompilationWithReference(
        string referenceSource,
        params (string Path, string Source)[] sources)
    {
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
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        return new AnalyzerRun(CollectDiagnostics(
            compilation,
            globalOptions,
            analyzer: null,
            ImmutableArray<Diagnostic>.Empty));
    }

    internal static AnalyzerRun RunWithAnalyzer(
        CSharpCompilation compilation,
        DiagnosticAnalyzer analyzer,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
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
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var driver = previousDriver ?? CreateDriver(globalOptions);
        return RunCore(compilation, driver, globalOptions);
    }

    internal static GeneratorRun RunWithGenerator(
        CSharpCompilation compilation,
        IIncrementalGenerator generator,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return RunCore(
            compilation,
            CreateDriver(generator, globalOptions),
            globalOptions);
    }

    private static GeneratorRun RunCore(
        CSharpCompilation compilation,
        GeneratorDriver driver,
        IReadOnlyDictionary<string, string>? globalOptions,
        DiagnosticAnalyzer? analyzer = null)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var driverDiagnostics);
        var runResult = driver.GetRunResult();
        var diagnostics = CollectDiagnostics(
            (CSharpCompilation)outputCompilation,
            globalOptions,
            analyzer,
            runResult.Diagnostics.Concat(driverDiagnostics));
        return new GeneratorRun(
            driver,
            runResult,
            diagnostics);
    }

    private static ImmutableArray<Diagnostic> CollectDiagnostics(
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, string>? globalOptions,
        DiagnosticAnalyzer? analyzer,
        IEnumerable<Diagnostic> existing)
    {
        RequireNoErrors(compilation);
        return existing
            .Concat(AnalyzeFinalCompilation(
                compilation,
                globalOptions,
                analyzer))
            .Distinct(DiagnosticIdentityComparer.Instance)
            .OrderBy(
                static diagnostic =>
                    diagnostic.Location.SourceTree?.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(
                static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<Diagnostic> AnalyzeFinalCompilation(
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, string>? globalOptions,
        DiagnosticAnalyzer? analyzer)
    {
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
                diagnostic.Id.StartsWith("SPCF", StringComparison.Ordinal) ||
                diagnostic.Id == "AD0001")];
    }

    internal static ImmutableArray<string> DiagnosticKeys(
        GeneratorRun run)
    {
        return [.. run.Diagnostics.Select(static diagnostic =>
            diagnostic.Id + "|" +
            diagnostic.Location.SourceTree?.FilePath + "|" +
            diagnostic.Location.SourceSpan.Start + "|" +
            diagnostic.GetMessage(CultureInfo.InvariantCulture))];
    }

    private static CSharpGeneratorDriver CreateDriver(
        IReadOnlyDictionary<string, string>? globalOptions)
    {
        return CreateDriver(
            new ContractForValidatorGenerator(),
            globalOptions);
    }

    private static CSharpGeneratorDriver CreateDriver(
        IIncrementalGenerator generator,
        IReadOnlyDictionary<string, string>? globalOptions)
    {
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

    private static ImmutableArray<MetadataReference> CreateReferences(
        bool includeAttributes = true)
    {
        var trustedAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidOperationException(
                "The runtime did not expose trusted platform assemblies.");
        var references = trustedAssemblies
            .Split(Path.PathSeparator)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path));
        if (includeAttributes)
        {
            references = references.Append(MetadataReference.CreateFromFile(
                typeof(ContractForAttribute).Assembly.Location));
        }

        return [.. references];
    }

    private static void RequireNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (!errors.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic.ToString())));
        }
    }
}

internal interface IDiagnosticRun
{
    ImmutableArray<Diagnostic> Diagnostics { get; }
}

internal sealed class GeneratorRun(
    GeneratorDriver driver,
    GeneratorDriverRunResult runResult,
    ImmutableArray<Diagnostic> diagnostics) : IDiagnosticRun
{
    internal GeneratorDriver Driver { get; } = driver;
    internal GeneratorDriverRunResult RunResult { get; } = runResult;
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
}

internal sealed class AnalyzerRun(ImmutableArray<Diagnostic> diagnostics) : IDiagnosticRun
{
    public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
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
