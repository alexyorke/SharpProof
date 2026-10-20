using System.Numerics;

namespace SharpProof.Compiler.Semantics;

internal sealed partial class ResourceBoundsAnalysis
{
    private Cost? CreditLoop(IOperation body, ResourceLoopRank rank, Bound count, int depth)
    {
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        rank = ArgumentNullGuard.NotNull(rank, nameof(rank));
        count = ArgumentNullGuard.NotNull(count, nameof(count));
        if (body is not IBlockOperation { Operations.Length: >= 2 } block ||
            block.Operations[0] is not IExpressionStatementOperation deposit)
        { return null; }
        ILocalSymbol? credit = null;
        BigInteger amount = 0;
        bool checkedDeposit = false;
        if (deposit.Operation is IIncrementOrDecrementOperation { Kind: OperationKind.Increment, OperatorMethod: null, Target: ILocalReferenceOperation local } increment)
        {
            credit = local.Local;
            amount = 1;
            checkedDeposit = increment.IsChecked;
        }
        else if (deposit.Operation is ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null, Target: ILocalReferenceOperation target } add &&
            !add.InConversion.IsUserDefined && !add.OutConversion.IsUserDefined && IntegerConstant(add.Value) is { } value && value > 0)
        {
            credit = target.Local;
            amount = value;
            checkedDeposit = add.IsChecked;
        }
        if (credit is null || credit.RefKind != RefKind.None || credit.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64) ||
            _counters.ContainsKey(credit) || !_localInitialConstants.TryGetValue(credit, out var initial) || initial < 0)
        { return null; }
        IOperation last = block.Operations[block.Operations.Length - 1];
        IOperation? condition = null;
        if (last is IConditionalOperation { WhenFalse: null } optional)
        {
            condition = optional.Condition;
            last = optional.WhenTrue;
            if (last is IBlockOperation { Operations.Length: 1 } arm)
            { last = arm.Operations[0]; }
        }
        if (last is not IWhileLoopOperation { ConditionIsTop: true, ConditionIsUntil: false } drain ||
            drain.Condition is null || !SymbolEqualityComparer.Default.Equals(PositiveCounter(drain.Condition), credit))
        { return null; }
        var operations = drain.Body is IBlockOperation drainBlock ? drainBlock.Operations : ImmutableArray.Create(drain.Body);
        if (operations.IsEmpty || operations[operations.Length - 1] is not IExpressionStatementOperation spend)
        { return null; }
        var unit = spend.Operation is IIncrementOrDecrementOperation { Kind: OperationKind.Decrement, OperatorMethod: null, Target: ILocalReferenceOperation decrement } &&
            SymbolEqualityComparer.Default.Equals(decrement.Local, credit) ||
            spend.Operation is ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Subtract, OperatorMethod: null, Target: ILocalReferenceOperation subtract } subtraction &&
            !subtraction.InConversion.IsUserDefined && !subtraction.OutConversion.IsUserDefined &&
            IntegerConstant(subtraction.Value) == BigInteger.One && SymbolEqualityComparer.Default.Equals(subtract.Local, credit);
        if (!unit)
        { return null; }
        var width = credit.Type.SpecialType == SpecialType.System_Int32 ? 32 : 64;
        var maximum = (BigInteger.One << (width - 1)) - 1;
        var budget = Bound.Add(Bound.Number(initial), Bound.Multiply(count, Bound.Number(amount)));
        if (initial > maximum || amount > maximum || !checkedDeposit && (budget.Maximum is not { } bound || bound > maximum))
        { return Unknown("ResourceCreditMayOverflow"); }
        _localProgress.Remove(credit);
        _localInitialConstants.Remove(credit);
        _counters.Add(credit, new Bound(budget.Expression, ResourceExpression.Zero));
        try
        {
            var prefix = Sequence(block.Operations.Skip(1).Take(block.Operations.Length - 2), depth + 1);
            if (condition != null)
            { prefix = prefix.Add(Visit(condition, depth + 1)); }
            var spending = Sequence(operations.Take(operations.Length - 1), depth + 1);
            return new Cost(Bound.Add(Bound.Multiply(count, Bound.Add(Bound.One, prefix.LoopIterations)),
                    Bound.Multiply(budget, Bound.Add(Bound.One, spending.LoopIterations))),
                Bound.Add(Bound.Multiply(count, prefix.Arrays), Bound.Multiply(budget, spending.Arrays)),
                Bound.Add(Bound.Multiply(count, prefix.Elements), Bound.Multiply(budget, spending.Elements)),
                ResourceProgram.CreditLoop(rank, new ResourceCreditShape(initial, amount, width, checkedDeposit), prefix.Program, spending.Program));
        }
        finally
        { _counters.Remove(credit); }
    }
}
