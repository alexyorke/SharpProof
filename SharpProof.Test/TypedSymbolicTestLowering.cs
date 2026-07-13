using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

internal static class TypedSymbolicTestLowering
{
    internal static bool TryLowerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        var lowering = SymbolicSemanticPipeline.LowerCondition(expression, context);
        condition = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryLowerTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        term = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryLowerBuiltInLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.LowerBuiltInLengthTerm(expression, context);
        term = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryLowerArrayDimensionLengthTerm(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.LowerArrayDimensionLengthTerm(expression, dimension, context);
        term = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryLowerNullableHasValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(expression, context);
        term = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryLowerStringNonNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        var lowering = SymbolicSemanticPipeline.LowerStringNonNullCondition(expression, context);
        condition = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryLowerStringTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.LowerStringTerm(expression, context);
        term = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryCreateStringContentReferenceTerm(
        SymbolicTerm reference,
        out SymbolicTerm term)
    {
        var source = SyntaxFactory.IdentifierName("string-content");
        var lowering = SymbolicSemanticPipeline.ProjectStringContentTerm(reference, source);
        term = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

    internal static bool TryCreateBuiltInElementAccessInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax indexExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        _ = provenance;
        var lowering = SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(
            receiverExpression,
            indexExpression,
            source,
            context);
        condition = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }

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
