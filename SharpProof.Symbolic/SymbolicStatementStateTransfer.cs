using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicStatementStateTransfer
{
    internal static void InvalidateStateForTryRegionEntry(
        ref SymbolicState state,
        SyntaxNode site,
        StatementSyntax containingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (containingStatement is not TryStatementSyntax tryStatement ||
            tryStatement.Block.Span.Contains(site.SpanStart))
            return;

        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            tryStatement.Block,
            semanticModel,
            cancellationToken);
        if (tryStatement.Finally?.Block.Span.Contains(site.SpanStart) != true) return;

        foreach (var catchClause in tryStatement.Catches)
            SymbolicStateInvalidator.InvalidateNestedMutations(
                ref state,
                catchClause.Block,
                semanticModel,
                cancellationToken);
    }

    internal static bool SupportsCurrentStatementCompletionFacts(StatementSyntax statement)
    {
        return statement is LocalDeclarationStatementSyntax or
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax };
    }

    internal static void ApplyContainingBlockEntryStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        RemoveStateFactsInvalidatedByForLoopEntry(ref state, block, semanticModel, cancellationToken);

        ExpressionSyntax? condition = null;
        var branchWhenTrue = true;
        StatementSyntax? loopStatement = null;
        switch (block.Parent)
        {
            case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                condition = ifStatement.Condition;
                break;
            case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                when ReferenceEquals(statement, block):
                condition = ifStatement.Condition;
                branchWhenTrue = false;
                break;
            case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                condition = whileStatement.Condition;
                loopStatement = whileStatement;
                break;
            case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                condition = forStatement.Condition;
                loopStatement = forStatement;
                break;
        }

        if (condition != null &&
            SymbolicProgramPointFacts.TryAddInlineAssignmentReachabilityState(
                ref state,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken))
        {
            if (loopStatement != null)
                SymbolicLoopStateTransfer.ApplyLoopBodyInvariantStateFacts(
                    ref state,
                    loopStatement,
                    SymbolicLoopEdgeKind.Entry,
                    semanticModel,
                    cancellationToken);
            return;
        }

        if (condition != null)
            RemoveConditionAssignmentTargetFacts(
                ref state,
                condition,
                semanticModel,
                cancellationToken);

        if (SymbolicLoopStateTransfer.TryApplyLoopBodyEntryStateFacts(
                ref state,
                block.Parent!,
                siteSpanStart: null,
                semanticModel,
                cancellationToken))
            return;

        if (condition != null)
            SymbolicProgramPointFacts.AddReachabilityCondition(
                ref state,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken);
    }

    private static void RemoveStateFactsInvalidatedByForLoopEntry(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (block.Parent is not ForStatementSyntax forStatement ||
            !ReferenceEquals(forStatement.Statement, block))
            return;

        SymbolicLoopStateTransfer.InvalidateForLoopInitializerTargets(
            ref state,
            forStatement,
            semanticModel,
            cancellationToken);
    }

    internal static void AddCatchBodyEntryStateFacts(
        ref SymbolicState state,
        CatchClauseSyntax catchClause,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (catchClause.Declaration != null &&
            semanticModel.GetDeclaredSymbol(catchClause.Declaration, cancellationToken) is ILocalSymbol localSymbol &&
            !SymbolicLoopStateTransfer.IsSymbolAssignedBetween(
                catchClause.Block,
                catchClause.Block.SpanStart - 1,
                useSpanStart,
                localSymbol.OriginalDefinition,
                semanticModel,
                cancellationToken))
            AddSymbolReferenceNullCondition(
                ref state,
                localSymbol.OriginalDefinition,
                catchClause.Declaration,
                false,
                "ir.path.catch-entry.exception-not-null");

        if (catchClause.Filter?.FilterExpression is { } filterExpression &&
            !SymbolicLoopStateTransfer.AnyReferencedSymbolAssignedBeforeUse(
                filterExpression,
                catchClause.Block,
                useSpanStart,
                semanticModel,
                cancellationToken))
            SymbolicProgramPointFacts.AddReachabilityCondition(ref state, filterExpression, true, semanticModel, cancellationToken);
    }

    internal static void AddUsingStatementExpressionStateFacts(
        ref SymbolicState state,
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SymbolicLoopStateTransfer.AddThrowGuardedExpressionStateFacts(
            ref state,
            expression,
            statement,
            semanticModel,
            cancellationToken,
            "ir.path.using-entry.throw-guarded-not-null");
    }

    internal static void AddUsingStatementDeclarationStateFacts(
        ref SymbolicState state,
        UsingStatementSyntax usingStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (usingStatement.Declaration == null) return;

        foreach (var declarator in usingStatement.Declaration.Variables)
        {
            if (declarator.Initializer == null ||
                semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol)
                continue;

            AddUsingDeclarationInitializerStateFacts(
                ref state,
                localSymbol,
                declarator.Initializer.Value,
                usingStatement.Statement,
                semanticModel,
                cancellationToken);
        }
    }

    private static void AddUsingDeclarationInitializerStateFacts(
        ref SymbolicState state,
        ILocalSymbol localSymbol,
        ExpressionSyntax initializer,
        StatementSyntax usingBody,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var throwGuardedValue = SymbolicAssignmentStateTransfer.GetThrowGuardedValue(initializer);
        var effectiveInitializer = throwGuardedValue.EffectiveValueExpression;
        if (throwGuardedValue.HasGuard)
        {
            SymbolicLoopStateTransfer.AddThrowGuardedExpressionStateFacts(
                ref state,
                initializer,
                usingBody,
                semanticModel,
                cancellationToken,
                "ir.path.using-entry.throw-guarded-not-null");
        }

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerTerm(effectiveInitializer, context);
        if (!TryCreateSymbolTerm(localSymbol, out var target) ||
            lowering is not { IsExact: true, Value: { } value } ||
            !CanCompareIrTerms(target, value))
            return;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                target,
                value),
            initializer,
            "ir.path.using-entry.declaration-alias");
        state = state.AddPathCondition(new SymbolicFactCondition(fact));
    }

    internal static void AddMethodEntryNullableFlowStateFacts(
        ref SymbolicState state,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken) is IMethodSymbol
            {
                IsStatic: false,
                ContainingType.IsReferenceType: true
            } method)
        {
            var thisFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.NotEqual,
                    new SymbolicVariableTerm(SymbolicStateValueFacts.ImplicitThisVariableName, SmtValueKind.Reference),
                    new SymbolicNullTerm()),
                site,
                "ir.path.method-entry.this-non-null",
                method);
            state = state.AddPathCondition(new SymbolicFactCondition(thisFact));
        }

        foreach (var parameter in GetDefinitelyNotNullEntryParameters(
                     site,
                     semanticModel,
                     cancellationToken))
        {
            if (!TryCreateSymbolTerm(parameter, out var parameterTerm) ||
                parameterTerm.Kind != SmtValueKind.Reference)
                continue;

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.NotEqual,
                    parameterTerm,
                    new SymbolicNullTerm()),
                site,
                "ir.path.method-entry.nullability-contract",
                parameter);
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }
    }

    private static IEnumerable<IParameterSymbol> GetDefinitelyNotNullEntryParameters(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken) is not IMethodSymbol method)
            yield break;

        foreach (var parameter in method.Parameters)
            if (NullableFlowFacts.GetParameterInputState(parameter) == NullableFlowFactState.NotNull &&
                NullableFlowFacts.HasExplicitNotNullInputContract(parameter))
                yield return (IParameterSymbol)parameter.OriginalDefinition;
    }

    private static void RemoveConditionAssignmentTargetFacts(
        ref SymbolicState state,
        ExpressionSyntax condition,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in condition
                     .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;

            var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                state = SymbolicStateValueFacts.RemoveReferences(state, assignedSymbol.OriginalDefinition);
        }
    }

    internal static void AddPriorStatementStateFacts(
        ref SymbolicState state,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            SymbolicAssignmentStateTransfer.AddVariableDeclarationInitializerStateFacts(
                ref state,
                localDeclaration.Declaration,
                localDeclaration,
                semanticModel,
                cancellationToken,
                "ir.path.prior-statement");

            return;
        }

        if (statement is ExpressionStatementSyntax expressionStatement)
        {
            AddCompletedExpressionStatementStateFacts(
                ref state,
                expressionStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is BlockSyntax completedBlock)
        {
            AddCompletedBlockStateFacts(
                ref state,
                completedBlock,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is TryStatementSyntax completedTryStatement)
        {
            AddCompletedTryStatementStateFacts(
                ref state,
                completedTryStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is IfStatementSyntax or SwitchStatementSyntax or
            WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or
            ForEachStatementSyntax or ForEachVariableStatementSyntax or LockStatementSyntax &&
            SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
                statement,
                state,
                semanticModel,
                cancellationToken) is { IsExact: true, Value: { } completedControlFlowState })
        {
            state = completedControlFlowState;
            return;
        }

        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            statement,
            semanticModel,
            cancellationToken);
        if (statement is UsingStatementSyntax completedUsingStatement)
        {
            if (completedUsingStatement.Expression != null)
                SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                    ref state,
                    completedUsingStatement.Expression,
                    completedUsingStatement,
                    true,
                    semanticModel,
                    cancellationToken);

            if (completedUsingStatement.Declaration != null)
                foreach (var declarator in completedUsingStatement.Declaration.Variables)
                    if (declarator.Initializer != null)
                        SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                            ref state,
                            declarator.Initializer.Value,
                            completedUsingStatement,
                            true,
                            semanticModel,
                            cancellationToken);

            return;
        }

        if (statement is IfStatementSyntax completedIf &&
            SymbolicLoopStateTransfer.AnyConditionSymbolInvalidatedInStatement(
                completedIf.Condition,
                completedIf,
                semanticModel,
                cancellationToken))
            foreach (var symbol in SymbolicLoopStateTransfer.GetConditionDependencySymbols(
                         completedIf.Condition,
                         semanticModel,
                         cancellationToken))
                SymbolicStateInvalidator.InvalidateSymbol(ref state, symbol, completedIf);
    }

    internal static void AddCompletedExpressionStatementStateFacts(
        ref SymbolicState state,
        ExpressionStatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (statement.Expression is AssignmentExpressionSyntax assignment)
        {
            SymbolicExpressionStateTransfer.AddAssignmentExpressionStateFacts(
                ref state,
                assignment,
                statement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (SymbolMutationFacts.TryGetIncrementedOrDecrementedSymbol(
                statement.Expression,
                semanticModel,
                cancellationToken,
                out var mutatedSymbol,
                out _))
        {
            if (!SymbolicAssignmentValueUpdater.TryApplyComputedUpdate(
                    ref state,
                    mutatedSymbol,
                    statement.Expression,
                    semanticModel,
                    cancellationToken))
                state = SymbolicStateValueFacts.RemoveReferences(state, mutatedSymbol);
            return;
        }

        SymbolicStateInvalidator.InvalidateNestedMutations(
            ref state,
            statement,
            semanticModel,
            cancellationToken);
        SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
            ref state,
            statement.Expression,
            statement,
            true,
            semanticModel,
            cancellationToken);
    }

    private static void AddCompletedTryStatementStateFacts(
        ref SymbolicState state,
        TryStatementSyntax tryStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var entryState = state;
        var completionStates = new List<SymbolicState>();
        AddCompletion(tryStatement.Block, invalidateTryMutations: false);

        void AddCompletion(BlockSyntax block, bool invalidateTryMutations)
        {
            if (SymbolicControlFlowFacts.StatementDefinitelyExits(block, semanticModel, cancellationToken)) return;
            var branchState = entryState;
            if (invalidateTryMutations)
                SymbolicStateInvalidator.InvalidateNestedMutations(
                    ref branchState, tryStatement.Block, semanticModel, cancellationToken);
            AddCompletedBlockStateFacts(ref branchState, block, semanticModel, cancellationToken);
            if (!branchState.IsContradictory) completionStates.Add(branchState);
        }

        foreach (var catchClause in tryStatement.Catches)
        {
            var branchLimit = SymbolicAnalysisLimitContext.Limits.MaxTryCompletionBranches;
            if (completionStates.Count >= branchLimit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryCompletionBranches,
                    branchLimit,
                    completionStates.Count + 1,
                    tryStatement,
                    "program_point.try_completion_branches");
                break;
            }

            if (CatchClauseCanHandleKnownThrow(tryStatement, catchClause, semanticModel, cancellationToken))
                AddCompletion(catchClause.Block, invalidateTryMutations: true);
        }

        if (completionStates.Count == 0)
        {
            state = SymbolicOperationTransferKernel.Complete(entryState, tryStatement.Span).State;
            return;
        }

        state = SymbolicOperationTransferKernel.Merge(
            entryState,
            completionStates.ToImmutableArray(),
            tryStatement).State;
        if (tryStatement.Finally?.Block is { } finallyBlock)
        {
            AddCompletedBlockStateFacts(
                ref state,
                finallyBlock,
                semanticModel,
                cancellationToken);
            if (SymbolicControlFlowFacts.StatementDefinitelyExits(finallyBlock, semanticModel, cancellationToken))
                state = SymbolicOperationTransferKernel.Complete(state, finallyBlock.Span).State;
        }

        foreach (var hiddenSymbol in SymbolicBranchCompletionStateTransfer.GetLocalsDeclaredInside(
                     tryStatement,
                     semanticModel,
                     cancellationToken))
            state = SymbolicStateValueFacts.RemoveReferences(state, hiddenSymbol);
    }

    private static bool CatchClauseCanHandleKnownThrow(
        TryStatementSyntax tryStatement,
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (catchClause.Filter?.FilterExpression is { } filterExpression)
        {
            var filterValue = semanticModel.GetConstantValue(filterExpression, cancellationToken);
            if (filterValue is { HasValue: true, Value: false }) return false;
        }

        if (catchClause.Declaration?.Type is not { } caughtTypeSyntax ||
            tryStatement.Block.Statements.Count != 1 ||
            tryStatement.Block.Statements[0] is not ThrowStatementSyntax { Expression: { } thrownExpression })
            return true;

        var thrownType = semanticModel.GetTypeInfo(thrownExpression, cancellationToken).Type;
        var caughtType = semanticModel.GetTypeInfo(caughtTypeSyntax, cancellationToken).Type;
        if (thrownType == null || caughtType == null) return true;

        return semanticModel.Compilation.ClassifyConversion(thrownType, caughtType).IsImplicit;
    }

    internal static void AddCompletedBlockStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var processedStatementCount = 0;
        foreach (var statement in block.Statements)
        {
            var limit = SymbolicAnalysisLimitContext.Limits.MaxScopedBlockCompletionStatements;
            if (processedStatementCount >= limit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.ScopedBlockCompletionStatements,
                    limit,
                    block.Statements.Count,
                    block,
                    "program_point.completed_block_state");
                return;
            }

            processedStatementCount++;
            AddPriorStatementStateFacts(
                ref state,
                statement,
                semanticModel,
                cancellationToken);
            if (SymbolicControlFlowFacts.StatementDefinitelyExits(statement, semanticModel, cancellationToken)) return;
        }
    }
}
