namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility {
    private static bool TryGetEvaluationBranch(
        SyntaxNode ancestor,
        int position,
        out ExpressionSyntax condition,
        out bool branchWhenTrue,
        out SyntaxNode branchBody) {
        switch (ancestor) {
            case IfStatementSyntax ifStatement when ifStatement.Statement.Span.Contains(position):
                condition = ifStatement.Condition;
                branchWhenTrue = true;
                branchBody = ifStatement.Statement;
                return true;
            case IfStatementSyntax ifStatement when ifStatement.Else?.Statement.Span.Contains(position) == true:
                condition = ifStatement.Condition;
                branchWhenTrue = false;
                branchBody = ifStatement.Else!.Statement;
                return true;
            case ConditionalExpressionSyntax conditional when conditional.WhenTrue.Span.Contains(position):
                condition = conditional.Condition;
                branchWhenTrue = true;
                branchBody = conditional.WhenTrue;
                return true;
            case ConditionalExpressionSyntax conditional when conditional.WhenFalse.Span.Contains(position):
                condition = conditional.Condition;
                branchWhenTrue = false;
                branchBody = conditional.WhenFalse;
                return true;
            case BinaryExpressionSyntax binary when binary.Right.Span.Contains(position) &&
                                                    binary.IsKind(SyntaxKind.LogicalAndExpression):
                condition = binary.Left;
                branchWhenTrue = true;
                branchBody = binary.Right;
                return true;
            case BinaryExpressionSyntax binary when binary.Right.Span.Contains(position) &&
                                                    binary.IsKind(SyntaxKind.LogicalOrExpression):
                condition = binary.Left;
                branchWhenTrue = false;
                branchBody = binary.Right;
                return true;
            case WhileStatementSyntax whileStatement when whileStatement.Statement.Span.Contains(position):
                condition = whileStatement.Condition;
                branchWhenTrue = true;
                branchBody = whileStatement.Statement;
                return true;
            case ForStatementSyntax { Condition: { } forCondition } forStatement
                when forStatement.Statement.Span.Contains(position):
                condition = forCondition;
                branchWhenTrue = true;
                branchBody = forStatement.Statement;
                return true;
            default:
                condition = null!;
                branchWhenTrue = false;
                branchBody = null!;
                return false;
        }
    }
}
