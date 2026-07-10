using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Smt;

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
            !SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                switchExpression.GoverningExpression,
                arm,
                semanticModel,
                cancellationToken,
                out var armCondition))
            return false;

        return IsFormulaAlwaysFalseAt(
            armCondition,
            switchExpression,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }
}