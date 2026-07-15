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
        if (TryAddContainingBlockEntryInlineAssignmentStateFacts(
                ref state,
                block,
                semanticModel,
                cancellationToken))
            return;

        RemoveStateFactsInvalidatedByContainingBlockEntry(ref state, block, semanticModel, cancellationToken);
        AddContainingBlockEntryStateFacts(ref state, block, semanticModel, cancellationToken);
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

    private static void RemoveStateFactsInvalidatedByContainingBlockEntry(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in GetContainingBlockEntryAssignedSymbols(block, semanticModel, cancellationToken))
            state = SymbolicStateValueFacts.RemoveReferences(state, symbol);
    }

    private static bool TryAddContainingBlockEntryInlineAssignmentStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (block.Parent)
        {
            case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                return SymbolicProgramPointFacts.TryAddInlineAssignmentReachabilityState(
                    ref state,
                    ifStatement.Condition,
                    true,
                    semanticModel,
                    cancellationToken);
            case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                when ReferenceEquals(statement, block):
                return SymbolicProgramPointFacts.TryAddInlineAssignmentReachabilityState(
                    ref state,
                    ifStatement.Condition,
                    false,
                    semanticModel,
                    cancellationToken);
            case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                if (!SymbolicProgramPointFacts.TryAddInlineAssignmentReachabilityState(
                        ref state,
                        whileStatement.Condition,
                        true,
                        semanticModel,
                        cancellationToken))
                    return false;

                SymbolicLoopStateTransfer.ApplyLoopBodyInvariantStateFacts(
                    ref state,
                    whileStatement,
                    SymbolicLoopEdgeKind.Entry,
                    semanticModel,
                    cancellationToken);
                return true;
            case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                if (forStatement.Condition == null ||
                    !SymbolicProgramPointFacts.TryAddInlineAssignmentReachabilityState(
                        ref state,
                        forStatement.Condition,
                        true,
                        semanticModel,
                        cancellationToken))
                    return false;

                SymbolicLoopStateTransfer.ApplyLoopBodyInvariantStateFacts(
                    ref state,
                    forStatement,
                    SymbolicLoopEdgeKind.Entry,
                    semanticModel,
                    cancellationToken);
                return true;
            default:
                return false;
        }
    }

    private static IEnumerable<ISymbol> GetContainingBlockEntryAssignedSymbols(
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax? condition = null;
        switch (block.Parent)
        {
            case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                condition = ifStatement.Condition;
                break;
            case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                when ReferenceEquals(statement, block):
                condition = ifStatement.Condition;
                break;
            case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                condition = whileStatement.Condition;
                break;
            case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                condition = forStatement.Condition;
                break;
        }

        if (condition == null) yield break;

        foreach (var assignment in condition
                     .DescendantNodesAndSelf(candidate => !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;

            var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            if (assignedSymbol is ILocalSymbol or IParameterSymbol) yield return assignedSymbol.OriginalDefinition;
        }
    }

    private static void AddContainingBlockEntryStateFacts(
        ref SymbolicState state,
        BlockSyntax block,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (SymbolicLoopStateTransfer.TryApplyLoopBodyEntryStateFacts(
                ref state,
                block.Parent!,
                siteSpanStart: null,
                semanticModel,
                cancellationToken))
            return;

        switch (block.Parent)
        {
            case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                SymbolicProgramPointFacts.AddReachabilityCondition(ref state, ifStatement.Condition, true, semanticModel, cancellationToken);
                break;
            case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                when ReferenceEquals(statement, block):
                SymbolicProgramPointFacts.AddReachabilityCondition(ref state, ifStatement.Condition, false, semanticModel, cancellationToken);
                break;
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

        if (statement is ExpressionStatementSyntax expressionStatement &&
            expressionStatement.Expression is AssignmentExpressionSyntax assignment)
        {
            SymbolicExpressionStateTransfer.AddAssignmentExpressionStateFacts(
                ref state,
                assignment,
                expressionStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is ExpressionStatementSyntax unaryExpressionStatement &&
            SymbolMutationFacts.TryGetIncrementedOrDecrementedSymbol(
                unaryExpressionStatement.Expression,
                semanticModel,
                cancellationToken,
                out var mutatedSymbol,
                out var delta))
        {
            if (SymbolicStateValueFacts.TryGetCurrentValue(state, mutatedSymbol, out var previousValueTerm) &&
                SymbolicAssignmentValueUpdater.TryCreateIncrementOrDecrement(
                    previousValueTerm,
                    delta,
                    unaryExpressionStatement.Expression,
                    semanticModel,
                    cancellationToken,
                    mutatedSymbol,
                    out var updatedValueTerm,
                    out var isChecked))
            {
                var transition = SymbolicOperationTransferAdapter.ApplyComputedUpdate(
                    state,
                    mutatedSymbol,
                    updatedValueTerm,
                    unaryExpressionStatement.Expression,
                    semanticModel,
                    cancellationToken,
                    delta >= 0
                        ? SymbolicComputedUpdateKind.Increment
                        : SymbolicComputedUpdateKind.Decrement,
                    isChecked,
                    delta >= 0
                        ? "ir.path.prior-statement.increment"
                        : "ir.path.prior-statement.decrement");
                if (transition.IsExact)
                {
                    state = transition.State;
                    return;
                }
            }

            state = SymbolicStateValueFacts.RemoveReferences(state, mutatedSymbol);
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

        var stateBeforeStatement = state;
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

        if (statement is IfStatementSyntax completedIfStatement)
        {
            SymbolicBranchCompletionStateTransfer.AddCompletedIfStatementStateFacts(
                ref state,
                completedIfStatement,
                stateBeforeStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is SwitchStatementSyntax completedSwitchStatement)
        {
            SymbolicBranchCompletionStateTransfer.AddCompletedSwitchStatementStateFacts(
                ref state,
                completedSwitchStatement,
                stateBeforeStatement,
                semanticModel,
                cancellationToken);
            return;
        }

        if (statement is ExpressionStatementSyntax completedExpressionStatement)
            SymbolicNormalCompletionStateTransfer.AddNormalCompletionStateFacts(
                ref state,
                completedExpressionStatement.Expression,
                completedExpressionStatement,
                true,
                semanticModel,
                cancellationToken);
        else
            SymbolicControlFlowCompletionStateTransfer.AddCompletedLoopStatementStateFacts(
                ref state,
                statement,
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

        if (!SymbolicControlFlowFacts.StatementDefinitelyExits(tryStatement.Block, semanticModel, cancellationToken))
        {
            var tryState = entryState;
            AddCompletedBlockStateFacts(
                ref tryState,
                tryStatement.Block,
                semanticModel,
                cancellationToken);
            if (!tryState.IsContradictory) completionStates.Add(tryState);
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

            if (!CatchClauseCanHandleKnownThrow(tryStatement, catchClause, semanticModel, cancellationToken) ||
                SymbolicControlFlowFacts.StatementDefinitelyExits(catchClause.Block, semanticModel, cancellationToken))
                continue;

            var catchState = entryState;
            SymbolicStateInvalidator.InvalidateNestedMutations(
                ref catchState,
                tryStatement.Block,
                semanticModel,
                cancellationToken);
            AddCompletedBlockStateFacts(
                ref catchState,
                catchClause.Block,
                semanticModel,
                cancellationToken);
            if (!catchState.IsContradictory) completionStates.Add(catchState);
        }

        if (completionStates.Count == 0)
        {
            state = MarkContradictory(entryState);
            return;
        }

        state = MergeCompletedAlternativeStates(completionStates, entryState, tryStatement);
        if (tryStatement.Finally?.Block is { } finallyBlock)
        {
            AddCompletedBlockStateFacts(
                ref state,
                finallyBlock,
                semanticModel,
                cancellationToken);
            if (SymbolicControlFlowFacts.StatementDefinitelyExits(finallyBlock, semanticModel, cancellationToken))
                state = MarkContradictory(state);
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

    private static SymbolicState MergeCompletedAlternativeStates(
        IReadOnlyList<SymbolicState> states,
        SymbolicState entryState,
        TryStatementSyntax tryStatement)
    {
        if (states.Count == 1) return states[0];

        var commonFactKeys = new HashSet<string>(
            states[0].Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        for (var index = 1; index < states.Count; index++)
            commonFactKeys.IntersectWith(states[index].Facts.Select(SymbolicState.CreateProofFactKey));

        var commonFacts = states[0].Facts
            .Where(fact => commonFactKeys.Contains(SymbolicState.CreateProofFactKey(fact)))
            .ToArray();
        var commonConditions = SymbolicStateMerger.MergePathConditionsAcrossAll(states);
        var entryFactKeys = new HashSet<string>(
            entryState.Facts.Select(SymbolicState.CreateProofFactKey),
            StringComparer.Ordinal);
        var entryConditionKeys = new HashSet<string>(
            entryState.PathConditions.Select(SymbolicState.CreateProofConditionKey),
            StringComparer.Ordinal);
        var retainedFacts = entryState.Facts.ToList();
        var retainedConditions = entryState.PathConditions.ToList();
        var retainedFactKeys = new HashSet<string>(entryFactKeys, StringComparer.Ordinal);
        var retainedConditionKeys = new HashSet<string>(entryConditionKeys, StringComparer.Ordinal);
        var addedCount = 0;
        var mergeLimit = SymbolicAnalysisLimitContext.Limits.MaxMergedTryFacts;

        foreach (var fact in commonFacts)
        {
            var key = SymbolicState.CreateProofFactKey(fact);
            if (!retainedFactKeys.Add(key)) continue;

            if (addedCount >= mergeLimit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryFactMerge,
                    mergeLimit,
                    addedCount + 1,
                    tryStatement,
                    "program_point.try_fact_merge");
                break;
            }

            retainedFacts.Add(fact);
            addedCount++;
        }

        foreach (var condition in commonConditions)
        {
            var key = SymbolicState.CreateProofConditionKey(condition);
            if (!retainedConditionKeys.Add(key)) continue;

            if (addedCount >= mergeLimit)
            {
                SymbolicAnalysisLimitContext.Record(
                    SymbolicAnalysisLimitKind.TryFactMerge,
                    mergeLimit,
                    addedCount + 1,
                    tryStatement,
                    "program_point.try_fact_merge");
                break;
            }

            retainedConditions.Add(condition);
            addedCount++;
        }

        var commonVersions = states[0].SymbolVersions
            .Where(pair => states.Skip(1).All(state =>
                state.SymbolVersions.TryGetValue(pair.Key, out var version) && version == pair.Value))
            .ToArray();

        return new SymbolicState(
            retainedFacts,
            retainedConditions,
            commonVersions,
            states.All(static candidate => candidate.IsContradictory)).Normalize();
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
