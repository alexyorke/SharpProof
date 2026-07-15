using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicControlFlowCompletionStateTransfer
{
    internal static void AddCompletedLoopStatementStateFacts(
        ref SymbolicState state,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (TryGetConditionalLoop(statement, out var loopBody, out var condition))
        {
            if (SymbolicControlFlowFacts.StatementDefinitelyExits(statement, semanticModel, cancellationToken))
                state = MarkContradictory(state);
            else
                AddCompletedConditionalLoopStateFacts(
                    ref state,
                    statement,
                    loopBody,
                    condition,
                    semanticModel,
                    cancellationToken);
            return;
        }

        switch (statement)
        {
            case ForEachStatementSyntax forEachStatement:
                AddCompletedForeachStatementStateFacts(
                    ref state,
                    forEachStatement.Expression,
                    forEachStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
            case ForEachVariableStatementSyntax forEachVariableStatement:
                AddCompletedForeachStatementStateFacts(
                    ref state,
                    forEachVariableStatement.Expression,
                    forEachVariableStatement.Statement,
                    semanticModel,
                    cancellationToken);
                break;
            case LockStatementSyntax lockStatement:
                AddCompletedLockStatementStateFacts(
                    ref state,
                    lockStatement,
                    semanticModel,
                    cancellationToken);
                break;
        }
    }

    private static void AddCompletedConditionalLoopStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        ExpressionSyntax? condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (condition != null && CanAssumeLoopConditionFalseAfterNormalExit(loopStatement, loopBody))
            SymbolicProgramPointFacts.AddReachabilityCondition(
                ref state,
                condition,
                false,
                semanticModel,
                cancellationToken);
        else if (TryCreateGuardedBreakLoopExitSymbolicCondition(
                     loopStatement,
                     loopBody,
                     condition,
                     semanticModel,
                     cancellationToken,
                     out var exitCondition))
            state = SymbolicOperationTransferKernel.TransitionLoopEdge(
                state,
                SymbolicLoopEdgeKind.Exit,
                exitCondition,
                loopStatement.Span,
                "ir.path.loop-exit").State;
        else
            return;
        AddLoopBodyInvariantStateFacts(ref state, loopStatement, semanticModel, cancellationToken);
    }

    private static bool TryGetConditionalLoop(
        StatementSyntax statement,
        out StatementSyntax body,
        out ExpressionSyntax? condition)
    {
        (body, condition) = statement switch
        {
            WhileStatementSyntax loop => (loop.Statement, loop.Condition),
            ForStatementSyntax loop => (loop.Statement, loop.Condition),
            DoStatementSyntax loop => (loop.Statement, loop.Condition),
            _ => (null!, null)
        };
        return body != null;
    }

    private static void AddLoopBodyInvariantStateFacts(
        ref SymbolicState state,
        StatementSyntax loopStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (loopStatement)
        {
            case ForStatementSyntax forStatement:
                SymbolicLoopStateTransfer.AddForLoopBodyInvariantStateFacts(ref state, forStatement, semanticModel, cancellationToken);
                break;
            case WhileStatementSyntax whileStatement:
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatement,
                    whileStatement.Statement,
                    "ir.path.while-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
            case DoStatementSyntax doStatement:
                SymbolicLoopStateTransfer.AddPreLoopBodyInvariantStateFacts(
                    ref state,
                    doStatement,
                    doStatement.Statement,
                    "ir.path.do-loop-invariant",
                    semanticModel,
                    cancellationToken);
                break;
        }
    }

    private static void AddCompletedForeachStatementStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax foreachBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (SymbolicLoopStateTransfer.ReferenceIdentityFactIsInvalidatedInStatement(
                expression,
                foreachBody,
                semanticModel,
                cancellationToken))
            return;

        SymbolicProgramPointFacts.AddReferenceNullCondition(
            ref state,
            expression,
            false,
            semanticModel,
            cancellationToken,
            "ir.path.foreach-completion.not-null");
    }

    private static bool TryCreateGuardedBreakLoopExitSymbolicCondition(
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        ExpressionSyntax? condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition exitCondition)
    {
        exitCondition = null!;
        if (LoopBodyContainsGoto(loopBody) ||
            !TryCreateTopLevelGuardedBreakSymbolicCondition(
                loopStatement,
                loopBody,
                semanticModel,
                cancellationToken,
                out var breakCondition))
            return false;

        if (condition == null)
        {
            exitCondition = breakCondition;
            return true;
        }

        if (!SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
                condition,
                false,
                semanticModel,
                cancellationToken,
                out var normalExitCondition))
            return false;

        exitCondition = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            normalExitCondition,
            breakCondition);
        return true;
    }

    private static bool TryCreateTopLevelGuardedBreakSymbolicCondition(
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition breakCondition)
    {
        breakCondition = null!;
        var loopBreaks = loopBody
            .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .OfType<BreakStatementSyntax>()
            .Where(breakStatement => BreakTargetsLoop(breakStatement, loopStatement))
            .ToArray();
        if (loopBreaks.Length == 0) return false;

        SymbolicCondition? combinedCondition = null;
        foreach (var breakStatement in loopBreaks)
        {
            if (!TryCreateGuardedBreakSymbolicCondition(
                    breakStatement,
                    loopStatement,
                    loopBody,
                    semanticModel,
                    cancellationToken,
                    out var guardedBreakCondition))
                return false;

            combinedCondition = combinedCondition == null
                ? guardedBreakCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    combinedCondition,
                    guardedBreakCondition);
        }

        breakCondition = combinedCondition!;
        return true;
    }

    private static bool TryCreateGuardedBreakSymbolicCondition(
        BreakStatementSyntax breakStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition breakCondition)
    {
        breakCondition = null!;
        var guards = new List<(IfStatementSyntax IfStatement, bool BranchWhenTrue)>();
        StatementSyntax currentStatement = breakStatement;
        while (TryGetOnlyParentIfBranch(currentStatement, out var ifStatement, out var branchWhenTrue))
        {
            guards.Add((ifStatement, branchWhenTrue));
            currentStatement = ifStatement;
        }

        if (!IsTopLevelLoopBodyStatement(currentStatement, loopBody)) return false;

        SymbolicCondition? combinedCondition = null;
        if (guards.Count > 0 &&
            !TryCreateCombinedNestedGuardCondition(
                guards,
                loopBody,
                invalidationSpanStart: null,
                semanticModel,
                cancellationToken,
                out combinedCondition))
            return false;

        if (TryCreateGuardedContinueFallThroughBeforeStatementSymbolicCondition(
                loopStatement,
                loopBody,
                currentStatement,
                semanticModel,
                cancellationToken,
                out var fallThroughCondition))
            combinedCondition = combinedCondition == null
                ? fallThroughCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    fallThroughCondition,
                    combinedCondition);

        breakCondition = combinedCondition!;
        return combinedCondition != null;
    }

    private static bool TryCreateGuardedContinueFallThroughBeforeStatementSymbolicCondition(
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        StatementSyntax targetStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition fallThroughCondition)
    {
        fallThroughCondition = null!;
        if (loopBody is not BlockSyntax block) return false;

        var targetIndex = -1;
        for (var index = 0; index < block.Statements.Count; index++)
            if (ReferenceEquals(block.Statements[index], targetStatement))
            {
                targetIndex = index;
                break;
            }

        if (targetIndex <= 0) return false;

        SymbolicCondition? combinedCondition = null;
        for (var index = targetIndex - 1; index >= 0; index--)
        {
            if (block.Statements[index] is not IfStatementSyntax ifStatement ||
                !TryCreateGuardedContinueFallThroughSymbolicCondition(
                    ifStatement,
                    loopStatement,
                    loopBody,
                    targetStatement,
                    semanticModel,
                    cancellationToken,
                    out var guardFallThroughCondition))
                break;

            combinedCondition = combinedCondition == null
                ? guardFallThroughCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    guardFallThroughCondition,
                    combinedCondition);
        }

        if (combinedCondition == null) return false;

        fallThroughCondition = combinedCondition;
        return true;
    }

    private static bool TryCreateGuardedContinueFallThroughSymbolicCondition(
        IfStatementSyntax ifStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        StatementSyntax targetStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition fallThroughCondition)
    {
        fallThroughCondition = null!;
        if (TryGetDirectContinueBranch(ifStatement, loopStatement, out var continueBranchWhenTrue))
        {
            if (AnyConditionSymbolInvalidatedBeforeStatement(
                    ifStatement.Condition,
                    loopBody,
                    targetStatement.SpanStart,
                    semanticModel,
                    cancellationToken) ||
                !SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
                    ifStatement.Condition,
                    !continueBranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    out fallThroughCondition))
            {
                fallThroughCondition = null!;
                return false;
            }

            return true;
        }

        if (!TryCreateNestedGuardedContinueSymbolicCondition(
                ifStatement,
                loopStatement,
                loopBody,
                targetStatement,
                semanticModel,
                cancellationToken,
                out var continueCondition))
            return false;

        fallThroughCondition = new SymbolicNotCondition(continueCondition);
        return true;
    }

    private static bool TryCreateNestedGuardedContinueSymbolicCondition(
        IfStatementSyntax ifStatement,
        StatementSyntax loopStatement,
        StatementSyntax loopBody,
        StatementSyntax targetStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition continueCondition)
    {
        continueCondition = null!;
        var continueStatements = ifStatement
            .DescendantNodes(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .OfType<ContinueStatementSyntax>()
            .Where(continueStatement => ContinueTargetsLoop(continueStatement, loopStatement))
            .ToArray();
        if (continueStatements.Length != 1) return false;

        var guards = new List<(IfStatementSyntax IfStatement, bool BranchWhenTrue)>();
        StatementSyntax currentStatement = continueStatements[0];
        while (TryGetOnlyParentIfBranch(currentStatement, out var parentIf, out var branchWhenTrue))
        {
            guards.Add((parentIf, branchWhenTrue));
            currentStatement = parentIf;
        }

        if (guards.Count <= 1 || !ReferenceEquals(currentStatement, ifStatement)) return false;

        return TryCreateCombinedNestedGuardCondition(
            guards,
            loopBody,
            targetStatement.SpanStart,
            semanticModel,
            cancellationToken,
            out continueCondition);
    }

    private static bool TryCreateCombinedNestedGuardCondition(
        IReadOnlyList<(IfStatementSyntax IfStatement, bool BranchWhenTrue)> guards,
        StatementSyntax loopBody,
        int? invalidationSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicCondition combinedCondition)
    {
        combinedCondition = null!;
        SymbolicCondition? aggregate = null;
        for (var index = guards.Count - 1; index >= 0; index--)
        {
            var guard = guards[index];
            if (AnyConditionSymbolInvalidatedBeforeStatement(
                    guard.IfStatement.Condition,
                    loopBody,
                    invalidationSpanStart ?? guard.IfStatement.SpanStart,
                    semanticModel,
                    cancellationToken) ||
                !SymbolicBranchCompletionStateTransfer.TryCreateBranchSymbolicCondition(
                    guard.IfStatement.Condition,
                    guard.BranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    out var guardCondition))
                return false;

            aggregate = aggregate == null
                ? guardCondition
                : new SymbolicBinaryCondition(SymbolicConditionOperator.And, aggregate, guardCondition);
        }

        if (aggregate == null) return false;

        combinedCondition = aggregate;
        return true;
    }

    private static void AddCompletedLockStatementStateFacts(
        ref SymbolicState state,
        LockStatementSyntax lockStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!SymbolicLoopStateTransfer.IsLocalOrParameterReference(lockStatement.Expression, semanticModel, cancellationToken) ||
            SymbolicLoopStateTransfer.ReferenceIdentityFactIsInvalidatedInStatement(
                lockStatement.Expression,
                lockStatement.Statement,
                semanticModel,
                cancellationToken))
            return;

        SymbolicProgramPointFacts.AddReferenceNullCondition(
            ref state,
            lockStatement.Expression,
            false,
            semanticModel,
            cancellationToken,
            "ir.path.lock-completion.not-null");
    }

    private static bool CanAssumeLoopConditionFalseAfterNormalExit(
        StatementSyntax loopStatement,
        StatementSyntax loopBody)
    {
        foreach (var node in loopBody.DescendantNodesAndSelf(candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
            switch (node)
            {
                case GotoStatementSyntax:
                    return false;
                case BreakStatementSyntax breakStatement when BreakTargetsLoop(breakStatement, loopStatement):
                    return false;
            }

        return true;
    }

    private static bool IsTopLevelLoopBodyStatement(
        StatementSyntax statement,
        StatementSyntax loopBody)
    {
        return ReferenceEquals(statement, loopBody) ||
               (loopBody is BlockSyntax block &&
                ReferenceEquals(statement.Parent, block));
    }

    private static bool TryGetOnlyParentIfBranch(
        StatementSyntax statement,
        out IfStatementSyntax ifStatement,
        out bool branchWhenTrue)
    {
        ifStatement = null!;
        branchWhenTrue = false;

        var branchStatement = statement;
        if (branchStatement.Parent is BlockSyntax block)
        {
            if (block.Statements.Count != 1 ||
                !ReferenceEquals(block.Statements[0], branchStatement))
                return false;

            branchStatement = block;
        }

        if (branchStatement.Parent is not IfStatementSyntax parentIf) return false;

        if (ReferenceEquals(parentIf.Statement, branchStatement))
        {
            ifStatement = parentIf;
            branchWhenTrue = true;
            return true;
        }

        if (parentIf.Else?.Statement is { } elseStatement &&
            ReferenceEquals(elseStatement, branchStatement))
        {
            ifStatement = parentIf;
            branchWhenTrue = false;
            return true;
        }

        return false;
    }

    private static bool TryGetDirectContinueBranch(
        IfStatementSyntax ifStatement,
        StatementSyntax loopStatement,
        out bool branchWhenTrue)
    {
        if (StatementDirectlyContainsOnlyContinue(ifStatement.Statement, loopStatement))
        {
            branchWhenTrue = true;
            return true;
        }

        if (ifStatement.Else?.Statement is { } elseStatement &&
            StatementDirectlyContainsOnlyContinue(elseStatement, loopStatement))
        {
            branchWhenTrue = false;
            return true;
        }

        branchWhenTrue = false;
        return false;
    }

    private static bool StatementDirectlyContainsOnlyContinue(
        StatementSyntax statement,
        StatementSyntax loopStatement)
    {
        statement = SymbolicControlFlowFacts.UnwrapSingleStatementBlock(statement);
        return statement is ContinueStatementSyntax continueStatement &&
               ContinueTargetsLoop(continueStatement, loopStatement);
    }

    private static bool AnyConditionSymbolInvalidatedBeforeStatement(
        ExpressionSyntax condition,
        StatementSyntax root,
        int beforeSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = SymbolicLoopStateTransfer.GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
        if (conditionSymbols.Count == 0) return false;

        foreach (var statement in EnumerateStatementsBefore(root, beforeSpanStart))
            if (conditionSymbols.Any(symbol =>
                    SymbolicProgramPointFacts.StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken)))
                return true;

        return false;
    }

    private static IEnumerable<StatementSyntax> EnumerateStatementsBefore(
        StatementSyntax root,
        int beforeSpanStart)
    {
        if (root is BlockSyntax block)
        {
            foreach (var statement in block.Statements)
            {
                if (statement.SpanStart >= beforeSpanStart) yield break;

                yield return statement;
            }

            yield break;
        }

        if (root.SpanStart < beforeSpanStart) yield return root;
    }

    private static bool LoopBodyContainsGoto(StatementSyntax loopBody)
    {
        return loopBody
            .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
            .Any(static node => node is GotoStatementSyntax);
    }

    private static bool BreakTargetsLoop(
        BreakStatementSyntax breakStatement,
        StatementSyntax loopStatement)
    {
        for (var ancestor = breakStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, loopStatement)) return true;

            if (ancestor is SwitchStatementSyntax ||
                IsLoopStatement(ancestor))
                return false;
        }

        return false;
    }

    private static bool ContinueTargetsLoop(
        ContinueStatementSyntax continueStatement,
        StatementSyntax loopStatement)
    {
        for (var ancestor = continueStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, loopStatement)) return true;

            if (IsLoopStatement(ancestor)) return false;
        }

        return false;
    }

    internal static bool IsLoopStatement(SyntaxNode node)
    {
        return node is WhileStatementSyntax or
            ForStatementSyntax or
            ForEachStatementSyntax or
            DoStatementSyntax;
    }
}
