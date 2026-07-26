namespace SharpProof.Analyzer;

internal interface IAnalyzerSessionFactory {
    AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken);
}

internal sealed class DefaultAnalyzerSessionFactory : IAnalyzerSessionFactory {
    internal static DefaultAnalyzerSessionFactory Instance { get; } = new();

    private DefaultAnalyzerSessionFactory() {
    }

    public AnalyzerSession Create(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken) =>
        new(compilation, configuration, cancellationToken);
}

internal sealed class AnalyzerSession {
    private readonly EffectAnalysisSession? _effects;
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly Action<IMethodSymbol, AnalyzerSemanticOutcome>? _outcomeObserver;
    private readonly ConcurrentDictionary<AttributeSourceKey, byte> _validatedAttributes = new();

    internal AnalyzerSession(
        Compilation compilation,
        AnalyzerConfiguration configuration,
        CancellationToken cancellationToken,
        Action<IMethodSymbol, AnalyzerSemanticOutcome>? outcomeObserver = null) {
        cancellationToken.ThrowIfCancellationRequested();
        Compilation = compilation ??
            throw new ArgumentNullException(nameof(compilation));
        Configuration = configuration ??
            throw new ArgumentNullException(nameof(configuration));
        _outcomeObserver = outcomeObserver;
        Attributes = new AnalyzerAttributeSymbols(compilation);
        _apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        if (configuration.Mode is SharpProofMode.Effects or SharpProofMode.AllExperimental)
            _effects = new EffectAnalysisSession(compilation, _apiSpecs);
    }

    internal Compilation Compilation { get; }
    internal AnalyzerConfiguration Configuration { get; }
    internal AnalyzerAttributeSymbols Attributes { get; }
    internal IrFactory IrFactory { get; } = new();
    internal ResolvedApiSpecTable ApiSpecs => _apiSpecs;
    internal ResolvedApiSpecTable? EffectApiSpecs => _effects?.ApiSpecs;

    internal EffectMethodResult AnalyzeEffects(
        IMethodSymbol method,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return (_effects ??
                throw new InvalidOperationException(
                    "Effect analysis was not enabled for this compilation."))
            .Analyze(method, cancellationToken);
    }

    internal bool HasResolvedApiSpec(IMethodSymbol method) =>
        _apiSpecs.TryGet(method, out _);

    internal bool IsKnownPure(IMethodSymbol method) =>
        _apiSpecs.IsPureAndAllocationFree(method);

    internal void RecordSemanticOutcome(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome) =>
        _outcomeObserver?.Invoke(method, outcome);

    internal bool TryMarkControlAttributeValidated(AttributeData attribute) {
        var reference = attribute.ApplicationSyntaxReference;
        return reference == null ||
               _validatedAttributes.TryAdd(
                   new AttributeSourceKey(
                       reference.SyntaxTree,
                       reference.Span),
                   0);
    }

    private readonly struct AttributeSourceKey : IEquatable<AttributeSourceKey> {
        internal AttributeSourceKey(SyntaxTree tree, TextSpan span) {
            Tree = tree;
            Span = span;
        }

        private SyntaxTree Tree { get; }
        private TextSpan Span { get; }

        public bool Equals(AttributeSourceKey other) =>
            ReferenceEquals(Tree, other.Tree) &&
            Span.Equals(other.Span);

        public override bool Equals(object? obj) =>
            obj is AttributeSourceKey other && Equals(other);

        public override int GetHashCode() {
            unchecked {
                return (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Tree) * 397) ^
                       Span.GetHashCode();
            }
        }
    }
}
