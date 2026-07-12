using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

internal static class TypedSymbolicTestLowering
{
    internal static bool TryCreateBuiltInElementAccessInRangeCondition(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula formula)
    {
        var lowering = SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(
            elementAccess,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is { IsExact: true, Value: { } condition } &&
            SymbolicIrFormulaEncoder.TryEncode(condition, out formula))
            return true;

        formula = null!;
        return false;
    }

    internal static bool TryTranslateConditionFormula(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula)
    {
        var lowering = SymbolicSemanticPipeline.LowerCondition(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is { IsExact: true, Value: { } condition } &&
            SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded))
        {
            formula = encoded;
            return true;
        }

        formula = null;
        return false;
    }

    internal static bool TryTranslateValueWithPathFacts(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IEnumerable<SmtFormula> pathConditions,
        out SmtFormula? formula)
    {
        _ = pathConditions;
        return TryTranslateValue(expression, semanticModel, cancellationToken, out formula, null);
    }

    internal static bool TryTranslateValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion = null)
    {
        var lowering = SymbolicSemanticPipeline.LowerTerm(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion));
        if (lowering is { IsExact: true, Value: { } term } &&
            SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var encoded))
        {
            formula = encoded;
            return true;
        }

        formula = null;
        return false;
    }

    internal static bool TryCollectBranchAssumptions(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> formulas,
        Func<ISymbol, int>? getSymbolVersion = null)
    {
        var lowering = SymbolicSemanticPipeline.LowerBranchFacts(
            expression,
            branchWhenTrue,
            new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion));
        if (lowering is not { IsExact: true, Value: { } state }) return false;

        var originalCount = formulas.Count;
        foreach (var condition in state.PathConditions)
            if (SymbolicIrFormulaEncoder.TryEncode(condition, out var formula))
                formulas.Add(formula);

        return formulas.Count > originalCount;
    }

    internal static bool TryAddBranchConditionFacts(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> formulas)
    {
        return TryCollectBranchAssumptions(
            expression,
            branchWhenTrue,
            semanticModel,
            cancellationToken,
            formulas);
    }
}
