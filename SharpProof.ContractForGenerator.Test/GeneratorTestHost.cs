using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;

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

    internal static GeneratorRun Run(
        CSharpCompilation compilation,
        GeneratorDriver? previousDriver = null,
        IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        var driver = previousDriver ?? CreateDriver(globalOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var driverDiagnostics);
        var runResult = driver.GetRunResult();
        var diagnostics = runResult.Diagnostics
            .Concat(driverDiagnostics)
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
        return new GeneratorRun(
            compilation,
            (CSharpCompilation)outputCompilation,
            driver,
            runResult,
            diagnostics);
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
        return CSharpGeneratorDriver.Create(
            generators: [
                new ContractForValidatorGenerator().AsSourceGenerator()
            ],
            parseOptions: ParseOptions,
            optionsProvider: globalOptions == null
                ? null
                : new TestAnalyzerConfigOptionsProvider(globalOptions),
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

internal sealed class TestAnalyzerConfigOptionsProvider(
    IReadOnlyDictionary<string, string> globalValues) :
    AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _globalOptions =
        new TestAnalyzerConfigOptions(globalValues);

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return TestAnalyzerConfigOptions.Empty;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return TestAnalyzerConfigOptions.Empty;
    }
}

internal sealed class TestAnalyzerConfigOptions(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    internal static TestAnalyzerConfigOptions Empty { get; } =
        new(ImmutableDictionary<string, string>.Empty);

    public override bool TryGetValue(string key, out string value)
    {
        return values.TryGetValue(key, out value!);
    }
}

internal sealed class GeneratorRun(
    CSharpCompilation inputCompilation,
    CSharpCompilation outputCompilation,
    GeneratorDriver driver,
    GeneratorDriverRunResult runResult,
    ImmutableArray<Diagnostic> diagnostics)
{
    internal CSharpCompilation InputCompilation { get; } = inputCompilation;
    internal CSharpCompilation OutputCompilation { get; } = outputCompilation;
    internal GeneratorDriver Driver { get; } = driver;
    internal GeneratorDriverRunResult RunResult { get; } = runResult;
    internal ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;
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
