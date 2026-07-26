using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Attributes;

namespace SharpProof.Gates;

internal sealed record AnalyzerMethodSemanticOutcome(
    string MethodName,
    Accessibility Accessibility,
    int SourceStart,
    AnalyzerSemanticOutcome Outcome);

internal sealed record AnalyzerGateAnalysis(
    ImmutableArray<Diagnostic> Diagnostics,
    CompilationOptions CompilationOptions,
    ImmutableArray<AnalyzerMethodSemanticOutcome> SemanticOutcomes);

internal static class AnalyzerGateHost {
    internal static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Preview);
    internal static readonly ImmutableArray<string> DiagnosticIds = [
        "SP0002",
        "SP0013",
        "SP0015",
        "SP0016",
        "SP0024",
        "SP0025",
        "SP0027",
        "SP0030",
        "SP0045",
        "SP0046",
        "SP0047"
    ];

    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences);

    internal static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "SharpProofGate") {
        var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release)
            .WithSpecificDiagnosticOptions(
                DiagnosticIds.ToImmutableDictionary(
                    static id => id,
                    static _ => ReportDiagnostic.Warn,
                    StringComparer.Ordinal));
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "input.cs")],
            References.Value,
            options);
    }

    internal static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        string? mode,
        CancellationToken cancellationToken = default) {
        var compilation = CreateCompilation(source);
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (!errors.IsDefaultOrEmpty)
            throw new InvalidOperationException(
                "Corpus source did not compile:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors.Select(static diagnostic => diagnostic.ToString())));
        return AnalyzeAsync(
            compilation,
            new SharpProofAnalyzer(),
            mode,
            concurrentAnalysis: true,
            cancellationToken);
    }

    internal static async Task<AnalyzerGateAnalysis>
        AnalyzeWithSemanticOutcomesAsync(
            string source,
            string? mode,
            CancellationToken cancellationToken = default) {
        var compilation = CreateCompilation(source);
        ThrowIfCompilationHasErrors(compilation, cancellationToken);
        return await AnalyzeWithSemanticOutcomesAsync(
                compilation,
                mode,
                concurrentAnalysis: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<AnalyzerGateAnalysis>
        AnalyzeWithSemanticOutcomesAsync(
            Compilation compilation,
            string? mode,
            bool concurrentAnalysis,
            CancellationToken cancellationToken = default) {
        var factory = new RecordingAnalyzerSessionFactory();
        var diagnostics = await AnalyzeAsync(
                compilation,
                new SharpProofAnalyzer(factory),
                mode,
                concurrentAnalysis,
                cancellationToken)
            .ConfigureAwait(false);
        return new AnalyzerGateAnalysis(
            diagnostics,
            compilation.Options,
            factory.GetOutcomes());
    }

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        Compilation compilation,
        DiagnosticAnalyzer analyzer,
        string? mode,
        bool concurrentAnalysis,
        CancellationToken cancellationToken = default) {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (mode != null)
            values.Add("sharpproof_mode", mode);
        var options = new AnalyzerOptions(
            [],
            new GateOptionsProvider(values));
        var withAnalyzers = compilation.WithAnalyzers(
            [analyzer],
            new CompilationWithAnalyzersOptions(
                options,
                onAnalyzerException: null,
                concurrentAnalysis,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));
        return [.. (await withAnalyzers
                .GetAnalyzerDiagnosticsAsync(cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)];
    }

    internal static AnalyzerOptions CreateOptions(string? mode) {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (mode != null)
            values.Add("sharpproof_mode", mode);
        return new AnalyzerOptions([], new GateOptionsProvider(values));
    }

    private static void ThrowIfCompilationHasErrors(
        Compilation compilation,
        CancellationToken cancellationToken) {
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (!errors.IsDefaultOrEmpty)
            throw new InvalidOperationException(
                "Corpus source did not compile:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors.Select(static diagnostic => diagnostic.ToString())));
    }

    private static ImmutableArray<MetadataReference> CreateReferences() {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        ImmutableArray<MetadataReference> references = [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .Append(
                MetadataReference.CreateFromFile(
                    typeof(Contract).Assembly.Location))
        ];
        var externalTree = CSharpSyntaxTree.ParseText(
            """
            using SharpProof.Attributes;

            public static class ExternalCorpusEffects {
                [SharpProofTrusted("Reviewed corpus effect contract.")]
                [EffectContract(
                    SharpProofEffect.Synchronizes,
                    Capabilities = SharpProofCapability.Synchronization,
                    Complete = true)]
                public static void Synchronize() {
                }
            }
            """,
            ParseOptions,
            "ExternalCorpusEffects.cs");
        var externalCompilation = CSharpCompilation.Create(
            "SharpProof.Gates.ExternalEffects",
            [externalTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = externalCompilation.Emit(stream);
        if (!emit.Success)
            throw new InvalidOperationException(
                "Could not create the corpus effect-contract reference: " +
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
        return references.Add(MetadataReference.CreateFromImage(stream.ToArray()));
    }

    private sealed class GateOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues)
        : AnalyzerConfigOptionsProvider {
        private static readonly AnalyzerConfigOptions Empty =
            new GateOptions(new Dictionary<string, string>());
        private readonly AnalyzerConfigOptions _global =
            new GateOptions(globalValues);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
            Empty;

        public override AnalyzerConfigOptions GetOptions(
            AdditionalText textFile) =>
            Empty;
    }

    private sealed class GateOptions(
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

    private sealed class RecordingAnalyzerSessionFactory
        : IAnalyzerSessionFactory {
        private readonly ConcurrentDictionary<
            MethodOutcomeKey,
            AnalyzerSemanticOutcome> _outcomes = new();

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken) =>
            new(
                compilation,
                configuration,
                cancellationToken,
                Record);

        internal ImmutableArray<AnalyzerMethodSemanticOutcome> GetOutcomes() =>
            [.. _outcomes
                .OrderBy(static pair => pair.Key.SourceStart)
                .ThenBy(static pair => pair.Key.MethodName, StringComparer.Ordinal)
                .Select(static pair => new AnalyzerMethodSemanticOutcome(
                    pair.Key.MethodName,
                    pair.Key.Accessibility,
                    pair.Key.SourceStart,
                    pair.Value))];

        private void Record(
            IMethodSymbol method,
            AnalyzerSemanticOutcome outcome) {
            var sourceStart = method.Locations
                .FirstOrDefault(static location => location.IsInSource)
                ?.SourceSpan.Start ?? -1;
            var key = new MethodOutcomeKey(
                method.MetadataName,
                method.DeclaredAccessibility,
                sourceStart);
            _outcomes.AddOrUpdate(
                key,
                outcome,
                (_, current) =>
                    AnalyzerSemanticOutcomes.Combine(current, outcome));
        }

        private readonly record struct MethodOutcomeKey(
            string MethodName,
            Accessibility Accessibility,
            int SourceStart);
    }
}
