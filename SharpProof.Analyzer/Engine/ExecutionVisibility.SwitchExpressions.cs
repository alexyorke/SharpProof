namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    private static bool IsInUnreachableSwitchExpressionArm(
        SyntaxNode syntaxNode,
        SwitchExpressionSyntax switchExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        var arm = switchExpression.Arms.FirstOrDefault(candidate =>
            candidate.Expression.Span.Contains(syntaxNode.SpanStart));
        if (arm == null ||
            !SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                switchExpression.GoverningExpression,
                arm,
                semanticModel,
                cancellationToken,
                out var armCondition))
            return false;

        return HasSymbolicConditionStatusAt(
            armCondition,
            SymbolicProofStatus.ProvenFalse,
            switchExpression,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }
}
