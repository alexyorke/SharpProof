using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Attributes;
using SharpProof.Testing;

namespace SharpProof.Gates;

internal sealed record AnalyzerMethodSemanticOutcome(
    Accessibility Accessibility,
    AnalyzerSemanticOutcome Outcome);

internal sealed record AnalyzerGateAnalysis(
    ImmutableArray<Diagnostic> Diagnostics,
    CompilationOptions CompilationOptions,
    ImmutableArray<AnalyzerMethodSemanticOutcome> SemanticOutcomes);

internal abstract class AnalyzerSessionFactoryBase<TKey>(
    IEqualityComparer<TKey> comparer)
    : IAnalyzerSessionFactory
    where TKey : notnull
{
    protected readonly ConcurrentDictionary<TKey, AnalyzerSemanticOutcome>
        Outcomes = new(comparer);

    public AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return new(compilation, configuration, cancellationToken, Record);
    }

    protected abstract void Record(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome);

    protected void RecordOutcome(
        TKey key,
        AnalyzerSemanticOutcome outcome)
    {
        Outcomes.AddOrUpdate(
            key,
            static (_, incoming) => incoming,
            static (_, current, incoming) =>
                AnalyzerSemanticOutcomes.Combine(current, incoming),
            outcome);
    }
}

internal readonly record struct MethodOutcomeKey(
    IMethodSymbol Method,
    SyntaxTree? SourceTree,
    string SourceFilePath,
    int SourceTreeOrdinal,
    string MethodName,
    Accessibility Accessibility,
    int SourceStart);

internal sealed class MethodOutcomeKeyComparer
    : IEqualityComparer<MethodOutcomeKey>
{
    internal static MethodOutcomeKeyComparer Instance { get; } = new();

    public bool Equals(MethodOutcomeKey left, MethodOutcomeKey right)
    {
        return ReferenceEquals(left.SourceTree, right.SourceTree) &&
            left.SourceStart == right.SourceStart &&
            SymbolEqualityComparer.Default.Equals(left.Method, right.Method);
    }

    public int GetHashCode(MethodOutcomeKey key)
    {
        var hash = new HashCode();
        hash.Add(
            key.SourceTree == null
                ? 0
                : RuntimeHelpers.GetHashCode(key.SourceTree));
        hash.Add(key.SourceStart);
        hash.Add(SymbolEqualityComparer.Default.GetHashCode(key.Method));
        return hash.ToHashCode();
    }
}

internal static class AnalyzerGateHost
{
    internal static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Preview);
    /// <summary>
    /// Every diagnostic the corpus gate normalizes to a warning before
    /// snapshotting. Derived from the analyzer's own supported set so a new
    /// descriptor cannot be silently omitted from the gate.
    /// </summary>
    internal static readonly ImmutableArray<string> DiagnosticIds =
        [.. GeneratedDiagnosticDescriptors.SupportedDiagnostics
            .Select(static descriptor => descriptor.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)];

    private static readonly ImmutableDictionary<string, ReportDiagnostic>
        DiagnosticOptions = DiagnosticIds.ToImmutableDictionary(
            static id => id,
            static _ => ReportDiagnostic.Warn,
            StringComparer.Ordinal);
    private static readonly AnalyzerOptions EmptyAnalyzerOptions =
        new(
            [],
            new DictionaryAnalyzerConfigOptionsProvider(
                DictionaryAnalyzerConfigOptions.Empty,
                globalForFiles: false));

    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences);

    internal static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "SharpProofGate")
    {
        var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release)
            .WithSpecificDiagnosticOptions(DiagnosticOptions);
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "input.cs")],
            References.Value,
            options);
    }

    internal static async Task<AnalyzerGateAnalysis>
        AnalyzeWithSemanticOutcomesAsync(
            string source,
            string? mode,
            CancellationToken cancellationToken = default)
    {
        var compilation = CreateCompilation(source);
        ThrowIfCompilationHasErrors(
            compilation,
            int.MaxValue,
            static errors => new InvalidOperationException(
                "Corpus source did not compile:" + Environment.NewLine + errors),
            cancellationToken);
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
            CancellationToken cancellationToken = default)
    {
        var factory = new RecordingAnalyzerSessionFactory(compilation);
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
        CancellationToken cancellationToken = default)
    {
        AnalyzerOptions options;
        if (mode == null)
        {
            options = EmptyAnalyzerOptions;
        }
        else
        {
            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["sharpproof_features"] = mode
            };
            options = new AnalyzerOptions(
                [],
                new DictionaryAnalyzerConfigOptionsProvider(values));
        }
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

    internal static void ThrowIfCompilationHasErrors(
        Compilation compilation,
        int limit,
        Func<string, Exception> createException,
        CancellationToken cancellationToken)
    {
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(limit)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        if (errors.Length != 0)
        {
            throw createException(string.Join(Environment.NewLine, errors));
        }
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedPlatformAssemblies = TrustedPlatformAssemblyPaths.Get();
        ImmutableArray<MetadataReference> references = [.. trustedPlatformAssemblies
            .Select(static path => MetadataReference.CreateFromFile(path))
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
                    IsDeterministic = true,
                    PreconditionFree = true,
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
        {
            throw new InvalidOperationException(
                "Could not create the corpus effect-contract reference: " +
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
        }

        // A file reference, so the worker's compiler artifact can record it.
        var image = stream.ToArray();
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-gates-" + Convert.ToHexString(SHA256.HashData(image)));
        var path = Path.Combine(directory, "SharpProof.Gates.ExternalEffects.dll");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(directory);
            var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(staging, image);
            try
            {
                File.Move(staging, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(staging);
            }
        }

        return references.Add(MetadataReference.CreateFromFile(path));
    }

    private sealed class RecordingAnalyzerSessionFactory(
        Compilation compilation)
        : AnalyzerSessionFactoryBase<MethodOutcomeKey>(
            MethodOutcomeKeyComparer.Instance)
    {
        private readonly ImmutableDictionary<SyntaxTree, int> _treeOrdinals =
            compilation.SyntaxTrees
                .Select(static (tree, ordinal) => (tree, ordinal))
                .ToImmutableDictionary(
                    static item => item.tree,
                    static item => item.ordinal,
                    (IEqualityComparer<SyntaxTree>)
                        ReferenceEqualityComparer.Instance);
        internal ImmutableArray<AnalyzerMethodSemanticOutcome> GetOutcomes()
        {
            return [.. Outcomes
                .OrderBy(
                    static pair => pair.Key.SourceFilePath,
                    StringComparer.Ordinal)
                .ThenBy(static pair => pair.Key.SourceTreeOrdinal)
                .ThenBy(static pair => pair.Key.SourceStart)
                .ThenBy(
                    static pair => pair.Key.MethodName,
                    StringComparer.Ordinal)
                .Select(static pair => new AnalyzerMethodSemanticOutcome(
                    pair.Key.Accessibility,
                    pair.Value))];
        }

        protected override void Record(
            IMethodSymbol method,
            AnalyzerSemanticOutcome outcome)
        {
            var sourceLocation = method.Locations
                .FirstOrDefault(static location => location.IsInSource);
            var sourceTree = sourceLocation?.SourceTree;
            var sourceFilePath = sourceTree?.FilePath ?? string.Empty;
            var sourceTreeOrdinal = sourceTree != null &&
                _treeOrdinals.TryGetValue(sourceTree, out var ordinal)
                    ? ordinal
                    : int.MaxValue;
            var key = new MethodOutcomeKey(
                method,
                sourceTree,
                sourceFilePath,
                sourceTreeOrdinal,
                method.MetadataName,
                method.DeclaredAccessibility,
                sourceLocation?.SourceSpan.Start ?? -1);
            RecordOutcome(key, outcome);
        }

    }
}
