namespace SharpProof.Symbolic;

internal static class SymbolicControlFlowFacts {
    internal static bool StatementDefinitelyExits(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (statement is ReturnStatementSyntax or
            ThrowStatementSyntax or
            BreakStatementSyntax or
            ContinueStatementSyntax)
            return true;
        if (statement is YieldStatementSyntax yieldStatement)
            return yieldStatement.IsKind(SyntaxKind.YieldBreakStatement);

        statement = UnwrapSingleStatementBlock(statement);
        if (statement is ExpressionStatementSyntax expressionStatement &&
            ExpressionStatementDefinitelyExits(expressionStatement, semanticModel, cancellationToken))
            return true;

        try {
            var controlFlow = semanticModel.AnalyzeControlFlow(statement);
            return controlFlow is { Succeeded: true } && !controlFlow.EndPointIsReachable;
        }
        catch (ArgumentException) {
            return false;
        }
    }
    internal static bool ExpressionStatementDefinitelyExits(
        ExpressionStatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(statement.Expression);
        return expression is InvocationExpressionSyntax invocation &&
               semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
               NullableFlowFacts.HasDoesNotReturn(invocationOperation.TargetMethod);
    }
    internal static StatementSyntax UnwrapSingleStatementBlock(StatementSyntax statement) {
        while (statement is BlockSyntax { Statements.Count: 1 } block) statement = block.Statements[0];

        return statement;
    }
}
