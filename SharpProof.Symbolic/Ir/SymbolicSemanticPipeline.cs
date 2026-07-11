using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicSemanticPipeline
{
    internal static SymbolicLoweringResult<SymbolicTerm> LowerTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term))
            return Exact(term, expression, "term");

        return Unsupported<SymbolicTerm>(expression, "term");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerCondition(expression, context, out var condition))
            return Exact(condition, expression, "condition");

        return Unsupported<SymbolicCondition>(expression, "condition");
    }

    internal static SymbolicLoweringResult<SymbolicState> LowerBranchFacts(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SymbolicLoweringContext context)
    {
        var lowered = LowerCondition(expression, context);
        if (!lowered.IsExact || lowered.Value == null)
            return Unsupported<SymbolicState>(expression, "branch-facts");

        var condition = branchWhenTrue
            ? lowered.Value
            : new SymbolicNotCondition(lowered.Value);
        return Exact(new SymbolicState(pathConditions: new[] { condition }), expression, "branch-facts");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPattern(
        ExpressionSyntax valueExpression,
        PatternSyntax pattern,
        SymbolicLoweringContext context)
    {
        var value = LowerTerm(valueExpression, context);
        if (value.IsExact &&
            value.Value != null &&
            SymbolicIrLowerer.TryLowerPatternCondition(
                value.Value,
                context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type,
                pattern,
                pattern,
                context,
                out var condition))
            return Exact(condition, pattern, "pattern");

        return Unsupported<SymbolicCondition>(pattern, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerPatternCondition(
                value,
                valueType,
                pattern,
                source,
                context,
                out var condition))
            return Exact(condition, source, "pattern");

        return Unsupported<SymbolicCondition>(source, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerMemberOrIndexAccess(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (expression is not MemberAccessExpressionSyntax and not ElementAccessExpressionSyntax)
            return Unsupported<SymbolicTerm>(expression, "member-or-index");

        if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term))
            return Exact(term, expression, "member-or-index");

        return Unsupported<SymbolicTerm>(expression, "member-or-index");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerConversion(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (expression is not CastExpressionSyntax and not CheckedExpressionSyntax)
            return Unsupported<SymbolicTerm>(expression, "conversion");

        if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term))
            return Exact(term, expression, "conversion");

        return Unsupported<SymbolicTerm>(expression, "conversion");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerReferenceTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerReferenceTerm(expression, context, out var term))
            return Exact(term, expression, "reference-term");

        return Unsupported<SymbolicTerm>(expression, "reference-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerStringTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerStringTerm(expression, context, out var term))
            return Exact(term, expression, "string-term");

        return Unsupported<SymbolicTerm>(expression, "string-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerBooleanValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerBooleanValueTerm(expression, context, out var term))
            return Exact(term, expression, "boolean-term");

        return Unsupported<SymbolicTerm>(expression, "boolean-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerBuiltInLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerBuiltInLengthTerm(expression, context, out var term))
            return Exact(term, expression, "built-in-length");

        return Unsupported<SymbolicTerm>(expression, "built-in-length");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerArrayDimensionLengthTerm(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerArrayDimensionLengthTerm(expression, dimension, context, out var term))
            return Exact(term, expression, "array-dimension-length");

        return Unsupported<SymbolicTerm>(expression, "array-dimension-length");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerNullableHasValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerNullableHasValueTerm(expression, context, out var term))
            return Exact(term, expression, "nullable-has-value");

        return Unsupported<SymbolicTerm>(expression, "nullable-has-value");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerNullableValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerNullableValueTerm(expression, context, out var term))
            return Exact(term, expression, "nullable-value");

        return Unsupported<SymbolicTerm>(expression, "nullable-value");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerStringNonNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.TryLowerStringNonNullCondition(expression, context, out var condition))
            return Exact(condition, expression, "string-non-null");

        return Unsupported<SymbolicCondition>(expression, "string-non-null");
    }

    internal static SymbolicLoweringResult<SymbolicFact> LowerRuntimeHazardTrigger(
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        SymbolicCondition trigger,
        SyntaxNode source,
        string detail)
    {
        if (trigger == null) throw new ArgumentNullException(nameof(trigger));

        var provenance = "ir.runtime-hazard." + detail;
        return Exact(
            SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(kind, subject, trigger),
                source,
                provenance,
                evidenceKey: provenance),
            source,
            "runtime-hazard");
    }

    private static SymbolicLoweringResult<T> Exact<T>(T value, SyntaxNode source, string stage)
        where T : class
    {
        return SymbolicLoweringResult<T>.Exact(value, CreateProvenance(source, stage, "exact"));
    }

    private static SymbolicLoweringResult<T> Unsupported<T>(SyntaxNode source, string stage)
        where T : class
    {
        return SymbolicLoweringResult<T>.Unsupported(CreateProvenance(source, stage, "unsupported"));
    }

    private static SymbolicLoweringProvenance CreateProvenance(
        SyntaxNode source,
        string stage,
        string detail)
    {
        return new SymbolicLoweringProvenance("roslyn-to-ir." + stage, source.Span, detail);
    }
}
