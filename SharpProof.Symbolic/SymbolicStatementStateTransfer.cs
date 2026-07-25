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
        if (semanticModel.GetEnclosingSymbol(site.SpanStart, cancellationToken) is not IMethodSymbol containingMethod)
            return;
        foreach (var parameter in containingMethod.Parameters) {
            if (!TryCreateSymbolTerm(parameter.OriginalDefinition, out var parameterTerm) ||
                parameterTerm.Kind != SmtValueKind.Int ||
                !SymbolicTypeFacts.TryGetIntegralShape(
                    parameter.Type.SpecialType,
                    out var signed,
                    out _) ||
                signed ||
                !SymbolicTypeFacts.TryGetBoundedIntegralRange(
                    parameter.Type,
                    out var minimum,
                    out var maximum))
                continue;
            state = state.AddPathCondition(new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    parameterTerm,
                    new SymbolicIntegerConstantTerm(minimum)),
                site,
                "ir.path.method-entry.integral-lower-bound",
                parameter)));
            state = state.AddPathCondition(new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.LessThanOrEqual,
                    parameterTerm,
                    new SymbolicIntegerConstantTerm(maximum)),
                site,
                "ir.path.method-entry.integral-upper-bound",
                parameter)));
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
}
