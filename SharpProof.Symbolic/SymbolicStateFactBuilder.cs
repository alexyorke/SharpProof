using Microsoft.CodeAnalysis;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicStateFactBuilder
{
    internal static SymbolicState MarkContradictory(SymbolicState state)
    {
        return new SymbolicState(
            state.Facts,
            state.PathConditions,
            state.SymbolVersions,
            true);
    }

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
        return type.TypeKind == TypeKind.Dynamic ||
               type.IsReferenceType ||
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type) ||
               SymbolicTypeFacts.IsSupportedTupleCarrierType(type);
    }
}
