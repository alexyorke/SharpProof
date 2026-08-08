using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
namespace SharpProof.Test;
internal static class AnalyzerTestHost {
    private static readonly CSharpParseOptions PreviewParseOptions = new(LanguageVersion.Preview);
    private static readonly CSharpCompilationOptions DefaultCompilationOptions =
        new(OutputKind.DynamicallyLinkedLibrary);
    private static readonly ImmutableArray<DiagnosticAnalyzer> AnalyzerInstances =
        [new SharpProofAnalyzer()];
    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedPlatformReferences =
        new(CreateTrustedPlatformReferences);
    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedPlatformReferencesWithEnforcePure =
        new(CreateTrustedPlatformReferencesWithEnforcePure);
    private static readonly Lazy<MetadataReference> EnforcePureAttributeReference =
        new(() => MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));
    public static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source) =>
        GetDiagnosticsAsync(
            source,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["sharpproof_features"] = "all"
            },
            [.. AnalyzerInstances.SelectMany(static analyzer => analyzer.SupportedDiagnostics)
                .Select(static descriptor => descriptor.Id)]);
    internal static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        IReadOnlyDictionary<string, string> globalOptions,
        params string[] enabledDiagnosticIds) {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, PreviewParseOptions, string.Empty);
        var specificDiagnosticOptions = enabledDiagnosticIds
            .Distinct(StringComparer.Ordinal)
            .ToImmutableDictionary(
                static id => id,
                static _ => ReportDiagnostic.Info,
                StringComparer.Ordinal);
        var compilation = CreateCompilation(
            "AnalyzerTestHost",
            TrustedPlatformReferencesWithEnforcePure.Value,
            DefaultCompilationOptions
                .WithSpecificDiagnosticOptions(specificDiagnosticOptions)
                .WithSyntaxTreeOptionsProvider(
                    new TestSyntaxTreeOptionsProvider(enabledDiagnosticIds)),
            syntaxTree);
        var analyzerOptions = new AnalyzerOptions(
            [],
            new TestAnalyzerConfigOptionsProvider(globalOptions, enabledDiagnosticIds));
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            AnalyzerInstances,
            new CompilationWithAnalyzersOptions(analyzerOptions, null, false, false, false));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
    internal static string GetRepositoryRoot() {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
    internal static SharpProof.Analyzer.Configuration.AnalyzerConfiguration GetConfiguration(
        IReadOnlyDictionary<string, string> globalOptions) =>
        SharpProof.Analyzer.Configuration.AnalyzerConfiguration.FromOptions(
            new AnalyzerOptions(
                [],
                new TestAnalyzerConfigOptionsProvider(globalOptions, [])));
    private static ImmutableArray<MetadataReference> CreateTrustedPlatformReferences() {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            return
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            ];
        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()];
    }
    private static ImmutableArray<MetadataReference> CreateTrustedPlatformReferencesWithEnforcePure() {
        var references = TrustedPlatformReferences.Value;
        if (references.IsDefault) references = [];
        return references.Add(EnforcePureAttributeReference.Value);
    }
    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        ImmutableArray<MetadataReference> references,
        CSharpCompilationOptions options,
        SyntaxTree syntaxTree) => CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            options);
    private sealed class TestAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues,
        IEnumerable<string> enabledDiagnosticIds)
        : AnalyzerConfigOptionsProvider {
        private static readonly AnalyzerConfigOptions Empty = new TestAnalyzerConfigOptions(
            new Dictionary<string, string>());
        private readonly AnalyzerConfigOptions _global = new TestAnalyzerConfigOptions(globalValues);
        private readonly AnalyzerConfigOptions _tree = new TestAnalyzerConfigOptions(
            enabledDiagnosticIds
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    static id => "dotnet_diagnostic." + id + ".severity",
                    static _ => "warning",
                    StringComparer.OrdinalIgnoreCase));
        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _tree;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
    }
    private sealed class TestAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string> values)
        : AnalyzerConfigOptions {
        public override bool TryGetValue(string key, out string value) {
            if (values.TryGetValue(key, out var found)) {
                value = found;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
    private sealed class TestSyntaxTreeOptionsProvider(
        IEnumerable<string> enabledDiagnosticIds)
        : SyntaxTreeOptionsProvider {
        private readonly ImmutableHashSet<string> _enabledDiagnosticIds =
            enabledDiagnosticIds.ToImmutableHashSet(StringComparer.Ordinal);
        public override GeneratedKind IsGenerated(
            SyntaxTree tree,
            CancellationToken cancellationToken) =>
            GeneratedKind.NotGenerated;
        public override bool TryGetDiagnosticValue(
            SyntaxTree tree,
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity) =>
            TryGetValue(diagnosticId, out severity);
        public override bool TryGetGlobalDiagnosticValue(
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity) =>
            TryGetValue(diagnosticId, out severity);
        private bool TryGetValue(string diagnosticId, out ReportDiagnostic severity) {
            severity = _enabledDiagnosticIds.Contains(diagnosticId)
                ? ReportDiagnostic.Warn
                : ReportDiagnostic.Default;
            return severity != ReportDiagnostic.Default;
        }
    }
}
