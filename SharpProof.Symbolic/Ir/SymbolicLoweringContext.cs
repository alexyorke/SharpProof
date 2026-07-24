namespace SharpProof.Symbolic.Ir;
internal delegate bool SymbolicInvocationTermLowerer(
    InvocationExpressionSyntax invocation,
    SymbolicLoweringContext context,
    [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SymbolicTerm? term);
internal delegate ITypeSymbol? SymbolicInvocationTermTypeResolver(InvocationExpressionSyntax invocation);
internal sealed class BoundNode {
    private IOperation? operation;
    private ISymbol? symbol;
    private TypeInfo typeInfo;
    private Optional<object?> constant;
    private bool hasOperation, hasSymbol, hasTypeInfo, hasConstant;
    private BoundNode(ExpressionSyntax syntax, SymbolicLoweringContext context) =>
        (Syntax, Context) = (syntax, context);
    internal ExpressionSyntax Syntax { get; }
    internal SymbolicLoweringContext Context { get; }
    internal SyntaxKind Kind => Syntax.Kind();
    internal IOperation? Operation => hasOperation ? operation : BindOperation();
    internal ISymbol? Symbol => hasSymbol ? symbol : BindSymbol();
    internal TypeInfo TypeInfo => hasTypeInfo ? typeInfo : BindTypeInfo();
    internal ITypeSymbol? Type => TypeInfo.ConvertedType ?? TypeInfo.Type;
    internal Optional<object?> Constant => hasConstant ? constant : BindConstant();
    internal static BoundNode Bind(ExpressionSyntax syntax, SymbolicLoweringContext context) {
        context.CancellationToken.ThrowIfCancellationRequested();
        return new(SymbolicLoweringValueFacts.UnwrapExpression(syntax), context);
    }
    private IOperation? BindOperation() {
        operation = Context.SemanticModel.GetOperation(Syntax, Context.CancellationToken);
        hasOperation = true;
        return operation;
    }
    private ISymbol? BindSymbol() {
        symbol = Context.SemanticModel.GetSymbolInfo(Syntax, Context.CancellationToken).Symbol;
        hasSymbol = true;
        return symbol;
    }
    private TypeInfo BindTypeInfo() {
        typeInfo = Context.SemanticModel.GetTypeInfo(Syntax, Context.CancellationToken);
        hasTypeInfo = true;
        return typeInfo;
    }
    private Optional<object?> BindConstant() {
        constant = Context.SemanticModel.GetConstantValue(Syntax, Context.CancellationToken);
        hasConstant = true;
        return constant;
    }
}
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
