namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    private static SymbolicState AddEvaluationPathState(
        SymbolicState pathState,
        SyntaxNode syntaxNode,
        SyntaxNode ancestor,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (TryGetEvaluationBranch(ancestor, syntaxNode.SpanStart, out var condition, out var branchWhenTrue) &&
            !IsGuardConditionInvalidatedBeforeUse(
                ancestor,
                condition,
                syntaxNode,
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

        if (ancestor is SwitchStatementSyntax switchStatement)
        {
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

        if (ancestor is SwitchExpressionSyntax switchExpression)
        {
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
        out bool branchWhenTrue)
    {
        switch (ancestor)
        {
            case IfStatementSyntax ifStatement when ifStatement.Statement.Span.Contains(position):
                condition = ifStatement.Condition;
                branchWhenTrue = true;
                return true;
            case IfStatementSyntax ifStatement when ifStatement.Else?.Statement.Span.Contains(position) == true:
                condition = ifStatement.Condition;
                branchWhenTrue = false;
                return true;
            case ConditionalExpressionSyntax conditional when conditional.WhenTrue.Span.Contains(position):
                condition = conditional.Condition;
                branchWhenTrue = true;
                return true;
            case ConditionalExpressionSyntax conditional when conditional.WhenFalse.Span.Contains(position):
                condition = conditional.Condition;
                branchWhenTrue = false;
                return true;
            case BinaryExpressionSyntax binary when binary.Right.Span.Contains(position) &&
                                                    binary.IsKind(SyntaxKind.LogicalAndExpression):
                condition = binary.Left;
                branchWhenTrue = true;
                return true;
            case BinaryExpressionSyntax binary when binary.Right.Span.Contains(position) &&
                                                    binary.IsKind(SyntaxKind.LogicalOrExpression):
                condition = binary.Left;
                branchWhenTrue = false;
                return true;
            case WhileStatementSyntax whileStatement when whileStatement.Statement.Span.Contains(position):
                condition = whileStatement.Condition;
                branchWhenTrue = true;
                return true;
            case ForStatementSyntax { Condition: { } forCondition } forStatement
                when forStatement.Statement.Span.Contains(position):
                condition = forCondition;
                branchWhenTrue = true;
                return true;
            default:
                condition = null!;
                branchWhenTrue = false;
                return false;
        }
    }

    // A guard condition (from an if/conditional/logical-and-or/loop ancestor) must not be
    // assumed at the use site if any symbol it references is reassigned between the guard's
    // branch entry and the use. Otherwise a stale guard fact (e.g. x > 0) is applied at the
    // reassigned symbol's current version, contradicting the new value (x = -1) and pruning a
    // reachable path. This mirrors the reassignment guard already applied in
    // SymbolicProgramPointFacts.CollectAncestorReachabilityState.
    private static bool IsGuardConditionInvalidatedBeforeUse(
        SyntaxNode ancestor,
        ExpressionSyntax condition,
        SyntaxNode use,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var body = GetGuardBranchBody(ancestor, use.SpanStart);
        return body != null &&
               SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                   condition,
                   body,
                   use.SpanStart,
                   semanticModel,
                   cancellationToken);
    }

    private static SyntaxNode? GetGuardBranchBody(SyntaxNode ancestor, int position)
    {
        switch (ancestor)
        {
            case IfStatementSyntax ifStatement when ifStatement.Statement.Span.Contains(position):
                return ifStatement.Statement;
            case IfStatementSyntax { Else.Statement: { } elseStatement }
                when elseStatement.Span.Contains(position):
                return elseStatement;
            case ConditionalExpressionSyntax conditional when conditional.WhenTrue.Span.Contains(position):
                return conditional.WhenTrue;
            case ConditionalExpressionSyntax conditional when conditional.WhenFalse.Span.Contains(position):
                return conditional.WhenFalse;
            case BinaryExpressionSyntax binary when binary.Right.Span.Contains(position) &&
                                                    (binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                                                     binary.IsKind(SyntaxKind.LogicalOrExpression)):
                return binary.Right;
            case WhileStatementSyntax whileStatement when whileStatement.Statement.Span.Contains(position):
                return whileStatement.Statement;
            case ForStatementSyntax forStatement when forStatement.Statement.Span.Contains(position):
                return forStatement.Statement;
            default:
                return null;
        }
    }

    private static SymbolicState AddSwitchEvaluationState(
        SymbolicState pathState,
        ExpressionSyntax governingExpression,
        SymbolicCondition selectionCondition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion)
    {
        if (SymbolicSemanticPipeline.LowerArrayLengthCountAliasCondition(
                governingExpression,
                new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion)) is
            { IsExact: true, Value: { } aliasCondition })
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
