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
        state = SymbolicSourceCompletionLowerer.ApplyThrowGuard(
            state,
            expression,
            statement,
            semanticModel,
            cancellationToken,
            "ir.path.using-entry.throw-guarded-not-null").State;
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
            state = SymbolicSourceCompletionLowerer.ApplyThrowGuard(
                state,
                initializer,
                usingBody,
                semanticModel,
                cancellationToken,
                "ir.path.using-entry.throw-guarded-not-null").State;
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
        if (statement is LocalDeclarationStatementSyntax or ExpressionStatementSyntax &&
            SymbolicCfgProgramPointStateCollector.TryApplyPriorStatementCompletion(
                ref state,
                statement,
                semanticModel,
                cancellationToken))
        {
            return;
        }

        if (statement is BlockSyntax completedBlock)
        {
            state = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
                completedBlock,
                state,
                semanticModel,
                cancellationToken).Value!;
            return;
        }

        if (statement is TryStatementSyntax or IfStatementSyntax or SwitchStatementSyntax or
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
                state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
                    state,
                    completedUsingStatement.Expression,
                    completedUsingStatement,
                    true,
                    semanticModel,
                    cancellationToken).State;

            if (completedUsingStatement.Declaration != null)
                foreach (var declarator in completedUsingStatement.Declaration.Variables)
                    if (declarator.Initializer != null)
                        state = SymbolicSourceCompletionLowerer.ApplyNormalCompletion(
                            state,
                            declarator.Initializer.Value,
                            completedUsingStatement,
                            true,
                            semanticModel,
                            cancellationToken).State;

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

}
