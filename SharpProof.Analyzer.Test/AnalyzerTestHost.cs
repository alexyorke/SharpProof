using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Analyzer.Test;

internal static class AnalyzerTestHost
{
    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Preview);
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences);

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        string? mode,
        IEnumerable<string> enabledIds,
        DiagnosticAnalyzer? analyzer = null,
        IEnumerable<MetadataReference>? additionalReferences = null,
        string? profile = null,
        string? features = null,
        string filePath = "input.cs",
        bool allowCompilationErrors = false)
    {
        var compilation = CreateCompilation(
            source,
            enabledIds,
            additionalReferences,
            filePath);
        return await AnalyzeAsync(
                compilation,
                mode,
                analyzer,
                profile,
                features,
                allowCompilationErrors)
            .ConfigureAwait(false);
    }

    internal static CSharpCompilation CreateCompilation(
        string source,
        IEnumerable<string> enabledIds,
        IEnumerable<MetadataReference>? additionalReferences = null,
        string filePath = "input.cs")
    {
        var enabled = enabledIds.ToImmutableHashSet(StringComparer.Ordinal);
        var tree = CSharpSyntaxTree.ParseText(
            source,
            ParseOptions,
            filePath);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (!enabled.IsEmpty)
        {
            options = options.WithSpecificDiagnosticOptions(
                new SharpProofAnalyzer().SupportedDiagnostics.ToImmutableDictionary(
                    static descriptor => descriptor.Id,
                    descriptor => enabled.Contains(descriptor.Id)
                        ? ReportDiagnostic.Warn
                        : ReportDiagnostic.Suppress,
                    StringComparer.Ordinal));
        }

        return CSharpCompilation.Create(
            "AnalyzerFixture",
            [tree],
            additionalReferences == null
                ? References.Value
                : References.Value.AddRange(additionalReferences),
            options);
    }

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        CSharpCompilation compilation,
        string? mode,
        DiagnosticAnalyzer? analyzer = null,
        string? profile = null,
        string? features = null,
        bool allowCompilationErrors = false,
        CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (mode != null)
        {
            switch (mode.ToUpperInvariant())
            {
                case "OFF":
                    profile ??= "off";
                    break;
                case "EFFECTS":
                case "CONTRACTS":
                    features ??= mode;
                    break;
                case "ALL-EXPERIMENTAL":
                    features ??= "all";
                    break;
                default:
                    values.Add("sharpproof_mode", mode);
                    break;
            }
        }

        if (profile != null)
        {
            values.Add("build_property.SharpProofProfile", profile);
        }

        if (features != null)
        {
            values.Add("build_property.SharpProofFeatures", features);
        }

        return await AnalyzeAsync(
                compilation,
                values,
                analyzer: analyzer,
                allowCompilationErrors: allowCompilationErrors,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, string> values,
        ImmutableArray<AdditionalText> additionalFiles = default,
        DiagnosticAnalyzer? analyzer = null,
        bool allowCompilationErrors = false,
        CancellationToken cancellationToken = default)
    {
        if (!allowCompilationErrors)
        {
            EnsureCompilationHasNoErrors(compilation);
        }
        var analyzerOptions = new AnalyzerOptions(
            additionalFiles.IsDefault ? [] : additionalFiles,
            new TestOptionsProvider(values));
        var withAnalyzers = compilation.WithAnalyzers(
            [analyzer ?? new SharpProofAnalyzer()],
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));
        return [.. (await withAnalyzers.GetAnalyzerDiagnosticsAsync(
                cancellationToken))
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)];
    }

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        CSharpCompilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider,
        DiagnosticAnalyzer? analyzer = null,
        bool allowCompilationErrors = false)
    {
        if (!allowCompilationErrors)
        {
            EnsureCompilationHasNoErrors(compilation);
        }
        var analyzerOptions = new AnalyzerOptions([], optionsProvider);
        var withAnalyzers = compilation.WithAnalyzers(
            [analyzer ?? new SharpProofAnalyzer()],
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));
        return [.. (await withAnalyzers.GetAnalyzerDiagnosticsAsync())
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)];
    }

    private static void EnsureCompilationHasNoErrors(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (errors.IsEmpty)
        {
            return;
        }

        throw new InvalidOperationException(
            "Analyzer fixture compilation failed:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
    }

    internal static byte[] EmitImage(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
        }

        return stream.ToArray();
    }

    internal static MetadataReference EmitReference(
        string source,
        string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            ParseOptions,
            assemblyName + ".cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    internal static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Could not find the repository root.");
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .Append(
                MetadataReference.CreateFromFile(
                    typeof(Contract).Assembly.Location))];
    }

    private sealed class TestOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues)
        : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Empty =
            new TestOptions(new Dictionary<string, string>());
        private readonly AnalyzerConfigOptions _global =
            new TestOptions(globalValues);

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return Empty;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return Empty;
        }
    }

    private sealed class TestOptions(
        IReadOnlyDictionary<string, string> values)
        : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
