using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicStateFactBuilder
{
    internal static bool TryCreateSymbolTerm(ISymbol symbol, out SymbolicTerm term)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
            !TryGetValueKind(type, out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), kind);
        return true;
    }

    internal static bool CanCompareIrTerms(SymbolicTerm left, SymbolicTerm right)
    {
        return left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
    }

    internal static void AddRelationPathFact(
        ref SymbolicState state,
        SymbolicRelationOperator op,
        SymbolicTerm left,
        SymbolicTerm right,
        SyntaxNode source,
        string provenance)
    {
        if (!CanCompareIrTerms(left, right)) return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(op, left, right),
            source,
            provenance);
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
    }

    internal static void AddSymbolReferenceNullCondition(
        ref SymbolicState state,
        ISymbol symbol,
        SyntaxNode source,
        bool isNull,
        string provenance)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
            !TryGetValueKind(type, out var kind) ||
            kind != SmtValueKind.Reference)
            return;

        AddRelationPathFact(
            ref state,
            isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
            new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), SmtValueKind.Reference),
            new SymbolicNullTerm(),
            source,
            provenance);
    }

    internal static bool TryCreateReferenceNullCondition(
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out SymbolicCondition condition,
        Func<ISymbol, int>? getSymbolVersion = null)
    {
        var lowering = SymbolicSemanticPipeline.LowerReferenceTerm(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion));
        if (lowering is not { IsExact: true, Value: { } reference })
        {
            condition = null!;
            return false;
        }

        condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                reference,
                new SymbolicNullTerm()),
            expression,
            provenance));
        return true;
    }

    internal static SymbolicState AddReferenceNullCondition(
        SymbolicState state,
        ExpressionSyntax expression,
        bool isNull,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        Func<ISymbol, int>? getSymbolVersion = null)
    {
        return TryCreateReferenceNullCondition(
            expression,
            isNull,
            semanticModel,
            cancellationToken,
            provenance,
            out var condition,
            getSymbolVersion)
            ? state.AddPathCondition(condition)
            : state;
    }

    internal static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
    {
        return SymbolicFactFactory.TryGetValueKind(
            type,
            SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
            IsProgramPointReferenceLikeType,
            out kind);
    }

    private static bool IsProgramPointReferenceLikeType(ITypeSymbol type)
    {
        return SymbolicTypeFacts.IsSymbolicReferenceLikeType(type);
    }
}
