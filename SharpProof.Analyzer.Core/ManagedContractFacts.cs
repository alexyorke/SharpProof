using SharpProof.Dataflow;

namespace SharpProof.Analyzer;

internal static class ManagedContractFacts
{
    internal static ManagedFlowState ApplyRequires(
        ManagedFlowState state, BoundMethodContracts? contracts)
    {
        if (contracts == null)
        {
            return state;
        }

        if (state.IsBottom)
        {
            return state;
        }

        Dictionary<IrVarId, ISymbol>? variables = null;
        foreach (var clause in contracts.Clauses)
        {
            if (clause.Kind != BoundContractKind.Requires)
            {
                continue;
            }

            variables ??= contracts.Variables
                .Where(static variable =>
                    variable.Symbol != null &&
                    variable.Role is BoundContractVariableRole.Receiver or BoundContractVariableRole.Parameter)
                .ToDictionary(static variable => variable.Variable, static variable => variable.Symbol!);
            state = Assume(state, clause.Condition, true, variables);
            if (state.IsBottom)
            {
                return state;
            }
        }

        return state;
    }

    internal static ManagedAbstractValue Evaluate(
        IrTerm term, IReadOnlyDictionary<IrVarId, ManagedAbstractValue> variables)
    {
        return Evaluate(term, variables, [], default);
    }

    internal static bool ContainsPotentiallyFailingCast(IrTerm term)
    {
        return IrTraversal.Any(term, static current => current is IrCastTerm);
    }

    internal static ManagedAbstractValue Evaluate(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, ManagedAbstractValue> variables,
        IReadOnlyCollection<IrVarId> definitelyStrings,
        IrTypeId stringType)
    {
        return term switch
        {
            IrBooleanTerm boolean => ManagedAbstractValue.Boolean(boolean.Value),
            IrIntegerTerm integer => ManagedAbstractValue.Integer(IntervalValue.Constant(integer.Value)),
            IrStringTerm => ManagedAbstractValue.Reference(NullnessValue.NonNull),
            IrNullTerm => ManagedAbstractValue.Reference(NullnessValue.Null),
            IrVariableTerm variable => variables.TryGetValue(variable.Variable, out var value)
                ? value
                : ManagedAbstractValue.Unknown,
            IrUnaryTerm { Operator: IrUnaryOperator.Not } unary =>
                ManagedAbstractValue.NegateBoolean(
                    Evaluate(
                        unary.Operand,
                        variables,
                        definitelyStrings,
                        stringType)),
            IrBinaryTerm binary => ManagedAbstractValue.BinaryOverIrScalars(
                CSharpScalarSemantics.MapBinaryToRoslyn(binary.Operator),
                Evaluate(
                    binary.Left,
                    variables,
                    definitelyStrings,
                    stringType),
                Evaluate(
                    binary.Right,
                    variables,
                    definitelyStrings,
                    stringType)),
            IrConditionalTerm conditional => Evaluate(
                    conditional.Condition,
                    variables,
                    definitelyStrings,
                    stringType).TryGetBoolean(out var condition)
                ? Evaluate(
                    condition ? conditional.WhenTrue : conditional.WhenFalse,
                    variables,
                    definitelyStrings,
                    stringType)
                : ManagedAbstractValue.Join(
                    Evaluate(
                        conditional.WhenTrue,
                        variables,
                        definitelyStrings,
                        stringType),
                    Evaluate(
                        conditional.WhenFalse,
                        variables,
                        definitelyStrings,
                        stringType)),
            IrCastTerm cast => EvaluateCast(
                cast,
                variables,
                definitelyStrings,
                stringType),
            _ => ManagedAbstractValue.Unknown
        };
    }

    private static ManagedAbstractValue EvaluateCast(
        IrCastTerm cast,
        IReadOnlyDictionary<IrVarId, ManagedAbstractValue> variables,
        IReadOnlyCollection<IrVarId> definitelyStrings,
        IrTypeId stringType)
    {
        var operand = Evaluate(
            cast.Operand,
            variables,
            definitelyStrings,
            stringType);
        if (operand.IsDefinitelyNull ||
            cast.Type == stringType &&
            (cast.Operand is IrVariableTerm variable &&
             definitelyStrings.Contains(variable.Variable) ||
             cast.Operand is IrStringTerm))
        {
            return operand;
        }

        return ManagedAbstractValue.Unknown;
    }

    private static ManagedFlowState Assume(
        ManagedFlowState state, IrTerm condition, bool expected,
        IReadOnlyDictionary<IrVarId, ISymbol> variables)
    {
        var values = variables.ToDictionary(static pair => pair.Key, pair => state.Get(pair.Value));
        if (Evaluate(condition, values).TryGetBoolean(out var constant))
        {
            return constant == expected ? state : ManagedFlowState.Bottom;
        }

        return condition switch
        {
            IrUnaryTerm { Operator: IrUnaryOperator.Not } unary =>
                Assume(state, unary.Operand, !expected, variables),
            IrBinaryTerm { Operator: IrBinaryOperator.AndAlso } binary when expected =>
                Assume(Assume(state, binary.Left, true, variables), binary.Right, true, variables),
            IrBinaryTerm { Operator: IrBinaryOperator.OrElse } binary when !expected =>
                Assume(Assume(state, binary.Left, false, variables), binary.Right, false, variables),
            IrVariableTerm variable when variables.TryGetValue(variable.Variable, out var symbol) =>
                state.Set(symbol, ManagedAbstractValue.Boolean(expected)),
            // The opposite operand is evaluated against the live state, not an
            // empty one, so a clause such as Requires(a < b) refines against
            // what is already known about b rather than only against literals.
            IrBinaryTerm { Left: IrVariableTerm left } binary
                when variables.TryGetValue(left.Variable, out var leftSymbol) =>
                ManagedAbstractFlow.Refine(
                    state,
                    leftSymbol,
                    CSharpScalarSemantics.MapBinaryToRoslyn(binary.Operator),
                    Evaluate(binary.Right, values), expected),
            IrBinaryTerm { Right: IrVariableTerm right } binary
                when variables.TryGetValue(right.Variable, out var rightSymbol) =>
                ManagedAbstractFlow.Refine(state, rightSymbol,
                    CSharpScalarSemantics.ReverseBinary(
                        CSharpScalarSemantics.MapBinaryToRoslyn(
                            binary.Operator)),
                    Evaluate(binary.Left, values), expected),
            _ => state
        };
    }
}
