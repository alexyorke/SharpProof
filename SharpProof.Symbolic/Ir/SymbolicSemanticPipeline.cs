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
                pattern,
                pattern,
                context,
                out var condition))
            return Exact(condition, pattern, "pattern");

        return Unsupported<SymbolicCondition>(pattern, "pattern");
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
