using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using SharpProof.Testing;

namespace SharpProof.Analyzer.Test;

internal static class AnalyzerTestHost
{
    // SharpProofAnalyzer keeps all compilation state in the analyzer session
    // created by the engine, so the Roslyn analyzer object itself is safe to
    // share between independent fixture compilations. Reusing it avoids an
    // allocation on every test-host invocation without sharing mutable
    // compilation state.
    private static readonly SharpProofAnalyzer DefaultAnalyzer = new();
    private static readonly ImmutableDictionary<string, DiagnosticDescriptor>
        SupportedDiagnosticMap = DefaultAnalyzer.SupportedDiagnostics
            .ToImmutableDictionary(
                static descriptor => descriptor.Id,
                StringComparer.Ordinal);
    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Preview);
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences);

    internal static void AssertIds(
        IEnumerable<Diagnostic> diagnostics,
        params string[] expected)
    {
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(expected));
    }

    internal static void AssertIds(
        IEnumerable<Diagnostic> diagnostics,
        string expected,
        int count)
    {
        AssertIds(diagnostics, [.. Enumerable.Repeat(expected, count)]);
    }

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
                SupportedDiagnosticMap.ToImmutableDictionary(
                    static pair => pair.Key,
                    pair => enabled.Contains(pair.Key)
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

    internal static CSharpCompilation WithEnabledDiagnostics(
        CSharpCompilation compilation,
        params string[] enabledIds)
    {
        var enabled = enabledIds.ToImmutableHashSet(StringComparer.Ordinal);
        var options = compilation.Options.WithSpecificDiagnosticOptions(
            SupportedDiagnosticMap.ToImmutableDictionary(
                static pair => pair.Key,
                pair => enabled.Contains(pair.Key)
                    ? ReportDiagnostic.Warn
                    : ReportDiagnostic.Suppress,
                StringComparer.Ordinal));
        return compilation.WithOptions(options);
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
        var analyzerOptions = new AnalyzerOptions(
            additionalFiles.IsDefault ? [] : additionalFiles,
            new DictionaryAnalyzerConfigOptionsProvider(values));
        return await AnalyzeAsync(
                compilation,
                analyzerOptions,
                analyzer,
                allowCompilationErrors,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        CSharpCompilation compilation,
        AnalyzerOptions analyzerOptions,
        DiagnosticAnalyzer? analyzer,
        bool allowCompilationErrors,
        CancellationToken cancellationToken)
    {
        if (!allowCompilationErrors)
        {
            EnsureCompilationHasNoErrors(compilation);
        }
        var withAnalyzers = compilation.WithAnalyzers(
            [analyzer ?? DefaultAnalyzer],
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
        var analyzerOptions = new AnalyzerOptions([], optionsProvider);
        return await AnalyzeAsync(
                compilation,
                analyzerOptions,
                analyzer,
                allowCompilationErrors,
                CancellationToken.None)
            .ConfigureAwait(false);
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

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(
                MetadataReference.CreateFromFile(
                    typeof(Contract).Assembly.Location))];
    }

}
