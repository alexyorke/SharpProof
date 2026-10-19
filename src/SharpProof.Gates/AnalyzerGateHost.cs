using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer;
using SharpProof.Compiler.Semantics;
using SharpProof.Attributes;
using SharpProof.Testing;

namespace SharpProof.Gates;

internal sealed record AnalyzerMethodSemanticOutcome(
    Accessibility Accessibility,
    AnalyzerSemanticOutcome Outcome);

internal sealed record AnalyzerGateAnalysis(
    ImmutableArray<Diagnostic> Diagnostics,
    CompilationOptions CompilationOptions,
    ImmutableArray<AnalyzerMethodSemanticOutcome> SemanticOutcomes)
{
    public CompilationOptions CompilationOptions { get; init; } = CompilationOptions ?? throw new System.ArgumentNullException(nameof(CompilationOptions));
    public ImmutableArray<Diagnostic> Diagnostics { get; init; } = Diagnostics.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Diagnostics)) : Diagnostics;
    public ImmutableArray<AnalyzerMethodSemanticOutcome> SemanticOutcomes { get; init; } = SemanticOutcomes.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(SemanticOutcomes)) : SemanticOutcomes;
}

internal abstract class AnalyzerSessionFactoryBase<TKey>(
    IEqualityComparer<TKey> comparer)
    : IAnalyzerSessionFactory
    where TKey : notnull
{
    protected ImmutableDictionary<TKey, AnalyzerSemanticOutcome>
        Outcomes = ImmutableDictionary.Create<TKey, AnalyzerSemanticOutcome>(comparer ?? throw new System.ArgumentNullException(nameof(comparer)));

    public AnalyzerSession Create(
        Compilation compilation,
        CompilerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(configuration);
        return new(compilation, configuration, cancellationToken, Record);
    }

    protected abstract void Record(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome);

    protected void RecordOutcome(
        TKey key,
        AnalyzerSemanticOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(key);

        ImmutableInterlocked.AddOrUpdate(
            ref Outcomes,
            key,
            outcome,
            (_, current) => AnalyzerSemanticOutcomes.Combine(current, outcome));
    }
}

internal readonly record struct MethodOutcomeKey(
    IMethodSymbol Method,
    SyntaxTree? SourceTree,
    string SourceFilePath,
    int SourceTreeOrdinal,
    string MethodName,
    Accessibility Accessibility,
    int SourceStart)
{
    public IMethodSymbol Method { get; init; } = Method ?? throw new System.ArgumentNullException(nameof(Method));
    public string SourceFilePath { get; init; } = SourceFilePath ?? throw new System.ArgumentNullException(nameof(SourceFilePath));
    public string MethodName { get; init; } = MethodName ?? throw new System.ArgumentNullException(nameof(MethodName));
}

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
        key = RepositoryParameterGuard.ValidateInitialized(key, key.Method is null || key.SourceFilePath is null || key.MethodName is null, nameof(key), "Value must be initialized.");

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
            .Select(static descriptor => descriptor is null ? throw new System.ArgumentNullException(nameof(descriptor)) : (descriptor.Id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (id), StringComparer.Ordinal)];

    private static readonly ImmutableDictionary<string, ReportDiagnostic>
        DiagnosticOptions = DiagnosticIds.ToImmutableDictionary(
            static id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (id),
            static _ => ReportDiagnostic.Warn,
            StringComparer.Ordinal);
    private static readonly AnalyzerOptions EmptyAnalyzerOptions =
        new(
            [],
            new DictionaryAnalyzerConfigOptionsProvider(
                DictionaryAnalyzerConfigOptions.Empty,
                globalForFiles: false));

    private static readonly Lazy<ImmutableArray<MetadataReference>> FileReferences =
        new(CreateFileReferences);

    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences);

    internal static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "SharpProofGate",
        bool includeExternalEffectsFixture = true)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(source);
        var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release)
            .WithSpecificDiagnosticOptions(DiagnosticOptions);
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, ParseOptions, "input.cs")],
            includeExternalEffectsFixture ? References.Value : FileReferences.Value,
            options);
    }

    internal static CSharpCompilation CreateValidatedCompilation(
        string source,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = CreateCompilation(source);
        ThrowIfCompilationHasErrors(
            compilation,
            int.MaxValue,
            static errors => errors is null ? throw new System.ArgumentNullException(nameof(errors)) : (new InvalidOperationException(
                "Corpus source did not compile:" + Environment.NewLine + errors)),
            cancellationToken);
        return compilation;
    }

    internal static async Task<AnalyzerGateAnalysis>
        AnalyzeWithSemanticOutcomesAsync(
            string source,
            string? mode,
            CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = CreateValidatedCompilation(source, cancellationToken);
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
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(analyzer);
        AnalyzerOptions options;
        if (mode == null)
        {
            options = EmptyAnalyzerOptions;
        }
        else
        {
            var values = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase).Add("sharpproof_features", mode);
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
            .OrderBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Location.SourceSpan.Start))
            .ThenBy(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id), StringComparer.Ordinal)];
    }

    internal static void ThrowIfCompilationHasErrors(
        Compilation compilation,
        int limit,
        Func<string, Exception> createException,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(createException);
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .Take(limit)
            .Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))
            .ToImmutableArray();
        if (errors.Length != 0)
        {
            throw createException(string.Join(Environment.NewLine, errors));
        }
    }

    private static ImmutableArray<MetadataReference> CreateFileReferences()
    {
        var trustedPlatformAssemblies = TrustedPlatformAssemblyPaths.Get();
        ImmutableArray<MetadataReference> references = [.. trustedPlatformAssemblies
            .Select(static path => path is null ? throw new System.ArgumentNullException(nameof(path)) : (MetadataReference.CreateFromFile(path)))
            .Append(
                MetadataReference.CreateFromFile(
                    typeof(Contract).Assembly.Location))
        ];
        return references;
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        return CreateReferences(FileReferences.Value);
    }

    // Build the fixed corpus library against the caller's reference set.
    // The normal cached host uses the current runtime and contract assemblies.
    internal static ImmutableArray<MetadataReference> CreateReferences(
        ImmutableArray<MetadataReference> references)
    {

        RepositoryParameterGuard.RequireInitialized(references.IsDefault, nameof(references), "Array must be initialized.");
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
                        diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        }

        // The production artifact capture requires a file-backed reference.
        // Keep the generated image alive for this process's compilation cache.
        var directory = Directory.CreateTempSubdirectory("sharpproof-gates-reference-");
        var path = Path.Combine(directory.FullName, "SharpProof.Gates.ExternalEffects.dll");
        File.WriteAllBytes(path, stream.ToArray());
        return references.Add(MetadataReference.CreateFromFile(path));
    }

    private sealed class RecordingAnalyzerSessionFactory(
        Compilation compilation)
        : AnalyzerSessionFactoryBase<MethodOutcomeKey>(
            MethodOutcomeKeyComparer.Instance)
    {
        private readonly ImmutableDictionary<SyntaxTree, int> _treeOrdinals =
            (compilation is null ? throw new System.ArgumentNullException(nameof(compilation)) : compilation.SyntaxTrees
                .Select(static (tree, ordinal) => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((tree, ordinal)))
                .ToImmutableDictionary(
                    static item => item.tree,
                    static item => item.ordinal,
                    (IEqualityComparer<SyntaxTree>)
                        ReferenceEqualityComparer.Instance));
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
            System.ArgumentNullException.ThrowIfNull(method);
            var sourceLocation = method.Locations
                .FirstOrDefault(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource));
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
