using static SharpProof.Symbolic.SymbolicStateFactBuilder;

namespace SharpProof.Symbolic;

internal static class SymbolicStatementStateTransfer {
    internal static void AddMethodEntryNullableFlowStateFacts(
        ref SymbolicState state,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken) is IMethodSymbol {
            IsStatic: false,
            ContainingType.IsReferenceType: true
        } method) {
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
        foreach (var parameter in GetDefinitelyNotNullEntryParameters(site, semanticModel, cancellationToken)) {
            if (!TryCreateSymbolTerm(parameter, out var parameterTerm) ||
                parameterTerm.Kind != SmtValueKind.Reference)
                continue;

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(SymbolicRelationOperator.NotEqual, parameterTerm, new SymbolicNullTerm()),
                site,
                "ir.path.method-entry.nullability-contract",
                parameter);
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }
    }
    private static IEnumerable<IParameterSymbol> GetDefinitelyNotNullEntryParameters(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken) is not IMethodSymbol method)
            yield break;

        foreach (var parameter in method.Parameters)
            if (NullableFlowFacts.GetParameterInputState(parameter) == NullableFlowFactState.NotNull &&
                NullableFlowFacts.HasExplicitNotNullInputContract(parameter))
                yield return (IParameterSymbol)parameter.OriginalDefinition;
    }
    internal static void AddPriorStatementStateFacts(
        ref SymbolicState state,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (statement is LocalDeclarationStatementSyntax or ExpressionStatementSyntax &&
            SymbolicCfgProgramPointStateCollector.TryApplyPriorStatementCompletion(ref state, statement, semanticModel,
                cancellationToken)) {
            return;
        }
        if (statement is BlockSyntax completedBlock) {
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
            SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(statement, state, semanticModel,
                cancellationToken) is { IsExact: true, Value: { } completedControlFlowState }) {
            state = completedControlFlowState;
            return;
        }
        SymbolicStateInvalidator.InvalidateNestedMutations(ref state, statement, semanticModel, cancellationToken);
        if (statement is UsingStatementSyntax completedUsingStatement) {
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
