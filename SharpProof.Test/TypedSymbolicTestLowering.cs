using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

internal static class TypedSymbolicTestLowering {
    internal static bool TryLowerCondition(ExpressionSyntax expression, SymbolicLoweringContext context,
        out SymbolicCondition condition)
        => TryGetExact(SymbolicSemanticPipeline.LowerCondition(expression, context), out condition);
    internal static bool TryLowerTerm(ExpressionSyntax expression, SymbolicLoweringContext context, out SymbolicTerm term)
        => TryGetExact(SymbolicSemanticPipeline.LowerTerm(expression, context), out term);
    internal static bool TryLowerBuiltInLengthTerm(ExpressionSyntax expression, SymbolicLoweringContext context,
        out SymbolicTerm term) => TryGetExact(SymbolicSemanticPipeline.LowerBuiltInLengthTerm(expression, context), out term);
    internal static bool TryLowerArrayDimensionLengthTerm(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context,
        out SymbolicTerm term) => TryGetExact(SymbolicSemanticPipeline.LowerArrayDimensionLengthTerm(expression, dimension,
            context), out term);
    internal static bool TryLowerStringNonNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
            => TryGetExact(SymbolicSemanticPipeline.LowerStringNonNullCondition(expression, context), out condition);
    internal static bool TryLowerStringTerm(ExpressionSyntax expression, SymbolicLoweringContext context, out SymbolicTerm term)
        => TryGetExact(SymbolicSemanticPipeline.LowerStringTerm(expression, context), out term);
    internal static bool TryCreateStringContentReferenceTerm(SymbolicTerm reference, out SymbolicTerm term) {
        var source = SyntaxFactory.IdentifierName("string-content");
        return TryGetExact(SymbolicSemanticPipeline.ProjectStringContentTerm(reference, source), out term);
    }
    internal static bool TryCreateBuiltInElementAccessInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax indexExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        _ = provenance;
        return TryGetExact(
            SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(receiverExpression, indexExpression, source, context),
            out condition);
    }
    internal static bool TryCreateBuiltInElementAccessInRangeCondition(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula formula) {
        var lowering = SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(
            elementAccess,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is { IsExact: true, Value: { } condition } &&
            SymbolicIrFormulaEncoder.TryEncode(condition, out formula))
            return true;

        formula = null!;
        return false;
    }
    private static bool TryGetExact<T>(SymbolicLoweringResult<T> lowering, out T value)
        where T : class {
        value = lowering.Value!;
        return lowering is { IsExact: true, Value: not null };
    }
}
