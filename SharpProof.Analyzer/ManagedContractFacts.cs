using SharpProof.Dataflow;

namespace SharpProof.Analyzer;

internal static class ManagedContractFacts
{
    private static readonly ImmutableDictionary<IrBinaryOperator, BinaryOperatorKind> Operators =
        Enum.GetValues(typeof(BinaryOperatorKind))
            .Cast<BinaryOperatorKind>()
            .Select(static kind => (Kind: kind, Ir: CSharpScalarSemantics.MapBinary(kind, SpecialType.None)))
            .Where(static item => item.Ir.HasValue)
            .ToImmutableDictionary(static item => item.Ir!.Value, static item => item.Kind);

    internal static ManagedFlowState ApplyRequires(
        ManagedFlowState state, BoundMethodContracts? contracts)
    {
        if (contracts == null)
        {
            return state;
        }

        var variables = contracts.Variables
            .Where(static variable =>
                variable.Symbol != null &&
                variable.Role is BoundContractVariableRole.Receiver or BoundContractVariableRole.Parameter)
            .ToDictionary(static variable => variable.Variable, static variable => variable.Symbol!);
        return contracts.Clauses
            .Where(static clause => clause.Kind == BoundContractKind.Requires)
            .Aggregate(state, (current, clause) =>
                current.IsBottom ? current : Assume(current, clause.Condition, true, variables));
    }

    internal static ManagedAbstractValue Evaluate(
        IrTerm term, IReadOnlyDictionary<IrVarId, ManagedAbstractValue> variables)
    {
        return Evaluate(term, variables, []);
    }

    internal static bool ContainsPotentiallyFailingCast(IrTerm term)
    {
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(term);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current.Id))
            {
                continue;
            }

            if (current is IrCastTerm)
            {
                return true;
            }

            foreach (var child in IrTraversal.GetChildren(current))
            {
                pending.Push(child);
            }
        }

        return false;
    }

    internal static ManagedAbstractValue Evaluate(
        IrTerm term,
        IReadOnlyDictionary<IrVarId, ManagedAbstractValue> variables,
        IReadOnlyCollection<IrVarId> definitelyStrings)
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
                    Evaluate(unary.Operand, variables, definitelyStrings)),
            IrBinaryTerm binary => ManagedAbstractValue.Binary(
                Map(binary.Operator),
                Evaluate(binary.Left, variables, definitelyStrings),
                Evaluate(binary.Right, variables, definitelyStrings)),
            IrConditionalTerm conditional => Evaluate(
                    conditional.Condition,
                    variables,
                    definitelyStrings).TryGetBoolean(out var condition)
                ? Evaluate(
                    condition ? conditional.WhenTrue : conditional.WhenFalse,
                    variables,
                    definitelyStrings)
                : ManagedAbstractValue.Join(
                    Evaluate(
                        conditional.WhenTrue,
                        variables,
                        definitelyStrings),
                    Evaluate(
                        conditional.WhenFalse,
                        variables,
                        definitelyStrings)),
            IrCastTerm cast => EvaluateCast(
                cast,
                variables,
                definitelyStrings),
            _ => ManagedAbstractValue.Unknown
        };
    }

    private static ManagedAbstractValue EvaluateCast(
        IrCastTerm cast,
        IReadOnlyDictionary<IrVarId, ManagedAbstractValue> variables,
        IReadOnlyCollection<IrVarId> definitelyStrings)
    {
        var operand = Evaluate(cast.Operand, variables, definitelyStrings);
        if (operand.IsDefinitelyNull ||
            cast.Operand is IrVariableTerm variable &&
            definitelyStrings.Contains(variable.Variable) ||
            cast.Operand is IrStringTerm)
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
            IrBinaryTerm { Left: IrVariableTerm left } binary
                when variables.TryGetValue(left.Variable, out var leftSymbol) =>
                ManagedAbstractFlow.Refine(state, leftSymbol, Map(binary.Operator),
                    Evaluate(binary.Right, EmptyValues), expected),
            IrBinaryTerm { Right: IrVariableTerm right } binary
                when variables.TryGetValue(right.Variable, out var rightSymbol) =>
                ManagedAbstractFlow.Refine(state, rightSymbol,
                    ManagedAbstractValue.ReverseComparison(Map(binary.Operator)),
                    Evaluate(binary.Left, EmptyValues), expected),
            _ => state
        };
    }

    private static BinaryOperatorKind Map(IrBinaryOperator @operator)
    {
        return Operators.TryGetValue(@operator, out var kind) ? kind : BinaryOperatorKind.None;
    }

    private static IReadOnlyDictionary<IrVarId, ManagedAbstractValue> EmptyValues
    {
        get;
    } =
        new Dictionary<IrVarId, ManagedAbstractValue>();
}
