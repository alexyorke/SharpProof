using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic.Ir;

internal delegate bool SymbolicInvocationTermLowerer(
    InvocationExpressionSyntax invocation,
    SymbolicLoweringContext context,
    out SymbolicTerm term);

internal delegate ITypeSymbol? SymbolicInvocationTermTypeResolver(InvocationExpressionSyntax invocation);

internal sealed class SymbolicLoweringContext
{
    public SymbolicLoweringContext(
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion = null,
        SmtAnalysisService? smtAnalysis = null,
        SymbolicInvocationTermLowerer? invocationTermLowerer = null,
        SymbolicTerm? implicitThis = null,
        int inlineDepth = 0,
        IReadOnlyDictionary<ISymbol, SymbolicTerm>? symbolSubstitutions = null,
        SymbolicInvocationTermTypeResolver? invocationTermTypeResolver = null)
    {
        SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        Compilation = semanticModel.Compilation;
        CancellationToken = cancellationToken;
        GetSymbolVersion = getSymbolVersion;
        SmtAnalysis = smtAnalysis;
        InvocationTermLowerer = invocationTermLowerer;
        ImplicitThis = implicitThis ?? new SymbolicVariableTerm("this", SmtValueKind.Reference);
        InlineDepth = inlineDepth;
        SymbolSubstitutions = symbolSubstitutions;
        InvocationTermTypeResolver = invocationTermTypeResolver;
    }

    public SemanticModel SemanticModel { get; }

    public Compilation Compilation { get; }

    public CancellationToken CancellationToken { get; }

    public Func<ISymbol, int>? GetSymbolVersion { get; }

    public SmtAnalysisService? SmtAnalysis { get; }

    public SymbolicInvocationTermLowerer? InvocationTermLowerer { get; }

    public SymbolicTerm ImplicitThis { get; }

    public int InlineDepth { get; }

    public IReadOnlyDictionary<ISymbol, SymbolicTerm>? SymbolSubstitutions { get; }

    public SymbolicInvocationTermTypeResolver? InvocationTermTypeResolver { get; }

    public bool TryGetSubstitution(ISymbol symbol, out SymbolicTerm term)
    {
        if (SymbolSubstitutions != null &&
            SymbolSubstitutions.TryGetValue(symbol.OriginalDefinition, out term!))
            return true;

        term = null!;
        return false;
    }

    public string GetVariableName(ISymbol symbol)
    {
        var name = SymbolicFactFactory.GetSmtVariableName(symbol);
        var version = GetSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
        return version > 0
            ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
            : name;
    }
}
