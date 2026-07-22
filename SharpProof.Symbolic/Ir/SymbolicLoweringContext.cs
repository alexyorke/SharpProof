namespace SharpProof.Symbolic.Ir;

internal delegate bool SymbolicInvocationTermLowerer(
    InvocationExpressionSyntax invocation,
    SymbolicLoweringContext context,
    out SymbolicTerm term);

internal delegate ITypeSymbol? SymbolicInvocationTermTypeResolver(InvocationExpressionSyntax invocation);

internal sealed class SymbolicLoweringContext(
    SemanticModel semanticModel,
    CancellationToken cancellationToken,
    Func<ISymbol, int>? getSymbolVersion = null,
    SmtAnalysisService? smtAnalysis = null,
    SymbolicInvocationTermLowerer? invocationTermLowerer = null,
    SymbolicTerm? implicitThis = null,
    int inlineDepth = 0,
    IReadOnlyDictionary<ISymbol, SymbolicTerm>? symbolSubstitutions = null,
    SymbolicInvocationTermTypeResolver? invocationTermTypeResolver = null) {
    internal const int MaxSourcePredicateInlineDepth = 8;

    public SemanticModel SemanticModel { get; } = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));

    public Compilation Compilation { get; } = semanticModel.Compilation;

    public CancellationToken CancellationToken { get; } = cancellationToken;

    public Func<ISymbol, int>? GetSymbolVersion { get; } = getSymbolVersion;

    public SmtAnalysisService? SmtAnalysis { get; } = smtAnalysis;

    public SymbolicInvocationTermLowerer? InvocationTermLowerer { get; } = invocationTermLowerer;

    public SymbolicTerm ImplicitThis { get; } = implicitThis ?? new SymbolicVariableTerm(
        SymbolicStateValueFacts.ImplicitThisVariableName,
        SmtValueKind.Reference);

    public int InlineDepth { get; } = inlineDepth;

    public IReadOnlyDictionary<ISymbol, SymbolicTerm>? SymbolSubstitutions { get; } = symbolSubstitutions;

    public SymbolicInvocationTermTypeResolver? InvocationTermTypeResolver { get; } = invocationTermTypeResolver;

    public bool TryGetSubstitution(ISymbol symbol, out SymbolicTerm term) {
        if (SymbolSubstitutions != null &&
            SymbolSubstitutions.TryGetValue(symbol.OriginalDefinition, out term!))
            return true;

        term = null!;
        return false;
    }
    public string GetVariableName(ISymbol symbol) {
        var name = SymbolicFactFactory.GetSmtVariableName(symbol);
        var version = GetSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
        return version > 0
            ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
            : name;
    }
}
