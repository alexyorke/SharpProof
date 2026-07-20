namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility {
    private static SymbolicState AddEvaluationPathState(
        SymbolicState pathState,
        SyntaxNode syntaxNode,
        SyntaxNode ancestor,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion) {
        if (TryGetEvaluationBranch(
                ancestor,
                syntaxNode.SpanStart,
                out var condition,
                out var branchWhenTrue,
                out var branchBody) &&
            !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                condition,
                branchBody,
                syntaxNode.SpanStart,
                semanticModel,
                cancellationToken) &&
            SymbolicReachabilityLowerer.ApplyCondition(
                pathState,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                getSymbolVersion) is { IsExact: true } transition)
            return transition.State;

        if (ancestor is BinaryExpressionSyntax binaryExpression &&
            binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
            binaryExpression.IsKind(SyntaxKind.CoalesceExpression))
            return SymbolicStateFactBuilder.AddReferenceNullCondition(
                pathState,
                binaryExpression.Left,
                true,
                semanticModel,
                cancellationToken,
                "analyzer.evaluation.null",
                getSymbolVersion);

        if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
            conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
            return SymbolicStateFactBuilder.AddReferenceNullCondition(
                pathState,
                conditionalAccessExpression.Expression,
                false,
                semanticModel,
                cancellationToken,
                "analyzer.evaluation.non-null",
                getSymbolVersion);

        if (ancestor is SwitchStatementSyntax switchStatement) {
            var section = switchStatement.Sections.FirstOrDefault(candidate =>
                candidate.Statements.Any(statement => statement.Span.Contains(syntaxNode.SpanStart)));
            if (section != null &&
                !IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel, cancellationToken) &&
                SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                    switchStatement.Expression,
                    section,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition,
                    getSymbolVersion))
                return AddSwitchEvaluationState(
                    pathState,
                    switchStatement.Expression,
                    sectionCondition,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion);
        }

        if (ancestor is SwitchExpressionSyntax switchExpression) {
            var arm = switchExpression.Arms.FirstOrDefault(candidate =>
                candidate.Expression.Span.Contains(syntaxNode.SpanStart));
            if (arm != null &&
                SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition,
                    getSymbolVersion))
                return AddSwitchEvaluationState(
                    pathState,
                    switchExpression.GoverningExpression,
                    armCondition,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion);
        }

        return pathState;
    }

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

    private static SymbolicState AddSwitchEvaluationState(
        SymbolicState pathState,
        ExpressionSyntax governingExpression,
        SymbolicCondition selectionCondition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion) {
        if (SymbolicSemanticPipeline.LowerArrayLengthCountAliasCondition(
                governingExpression,
                new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion)) is { IsExact: true, Value: { } aliasCondition })
            pathState = SymbolicOperationTransferKernel.Assume(
                pathState,
                aliasCondition,
                assumeTrue: true,
                governingExpression.Span,
                "analyzer.execution-visibility.switch-alias").State;

        return SymbolicOperationTransferKernel.Assume(
            pathState,
            selectionCondition,
            assumeTrue: true,
            governingExpression.Span,
            "analyzer.execution-visibility.switch-selection").State;
    }

}
