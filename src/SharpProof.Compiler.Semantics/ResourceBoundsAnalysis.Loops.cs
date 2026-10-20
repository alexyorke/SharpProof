using System.Numerics;

namespace SharpProof.Compiler.Semantics;

internal sealed partial class ResourceBoundsAnalysis
{
    private Cost While(IWhileLoopOperation loop, int depth)
    {
        loop = ArgumentNullGuard.NotNull(loop, nameof(loop));
        if (loop.ConditionIsTop && !loop.ConditionIsUntil &&
            loop.Condition is IBinaryOperation { OperatorMethod: null, OperatorKind: BinaryOperatorKind.ConditionalOr } phases)
        { return PhasedWhile(loop, phases, depth); }
        if (!loop.ConditionIsTop || loop.ConditionIsUntil ||
            loop.Condition is not IBinaryOperation { OperatorMethod: null } condition ||
            condition.OperatorKind is not (BinaryOperatorKind.LessThan or BinaryOperatorKind.GreaterThan) ||
            condition.LeftOperand is not ILocalReferenceOperation counter)
        { return Unknown("UnsupportedLoop"); }
        if (!_localProgress.TryGetValue(counter.Local, out var initial))
        { return Unknown("UnprovedLoopProgress"); }
        var increasing = condition.OperatorKind == BinaryOperatorKind.LessThan;
        var comparison = Size(condition.RightOperand);
        var lower = IntegerConstant(condition.RightOperand);
        if (comparison is null || !increasing && (lower is null || lower.Value < 0))
        { return Unknown("UnsupportedLoopBound"); }
        var operations = loop.Body is IBlockOperation block ? block.Operations : ImmutableArray.Create(loop.Body);
        if (operations.IsEmpty || operations[operations.Length - 1] is not IExpressionStatementOperation step)
        { return Unknown("UnprovedLoopProgress"); }
        IOperation? target;
        BigInteger amount;
        BinaryOperatorKind kind;
        bool checkedStep;
        if (step.Operation is IIncrementOrDecrementOperation { OperatorMethod: null } increment)
        {
            target = increment.Target;
            amount = 1;
            kind = increment.Kind == OperationKind.Increment ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract;
            checkedStep = increment.IsChecked;
        }
        else if (step.Operation is ICompoundAssignmentOperation { OperatorMethod: null } compound &&
            !compound.InConversion.IsUserDefined && !compound.OutConversion.IsUserDefined &&
            compound.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Multiply or BinaryOperatorKind.Subtract or BinaryOperatorKind.Divide &&
            IntegerConstant(compound.Value) is { } constant && constant > 0)
        {
            target = compound.Target;
            amount = constant;
            kind = compound.OperatorKind;
            checkedStep = compound.IsChecked;
        }
        else
        { return Unknown("UnprovedLoopProgress"); }
        if (target is not ILocalReferenceOperation local ||
            !SymbolEqualityComparer.Default.Equals(local.Local, counter.Local) ||
            counter.Local.RefKind != RefKind.None ||
            counter.Local.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64))
        { return Unknown("UnprovedLoopProgress"); }
        var maximum = counter.Local.Type.SpecialType == SpecialType.System_Int32 ? new BigInteger(int.MaxValue) : new BigInteger(long.MaxValue);
        if (amount > maximum)
        { return Unknown("UnprovedLoopProgress"); }
        Bound count;
        Bound envelope;
        ResourceLoopKind rankKind;
        if (increasing && kind is BinaryOperatorKind.Add or BinaryOperatorKind.Multiply)
        {
            // A nonnegative size envelope is insufficient for an increasing
            // rank: clamping a negative initializer would lose real iterations.
            if (!_localInitialConstants.TryGetValue(counter.Local, out var start) || start < 0)
            { return Unknown("UnsupportedLoopStart"); }
            if (!checkedStep && (comparison.Maximum is not { } limit ||
                (kind == BinaryOperatorKind.Add ? limit - 1 + amount > maximum : (limit - 1) * amount > maximum)))
            { return Unknown("LoopCounterMayOverflow"); }
            if (kind == BinaryOperatorKind.Multiply && (start != 1 || amount <= 1))
            { return Unknown("UnprovedLoopProgress"); }
            count = kind == BinaryOperatorKind.Add ? DivideCeiling(Difference(comparison, start), amount)
                : GeometricCount(comparison, amount, descending: false);
            envelope = comparison;
            rankKind = kind == BinaryOperatorKind.Add ? ResourceLoopKind.IncreasingAdd : ResourceLoopKind.IncreasingMultiply;
        }
        else if (!increasing && kind is BinaryOperatorKind.Subtract or BinaryOperatorKind.Divide)
        {
            if (kind == BinaryOperatorKind.Divide && (lower != 0 || amount <= 1))
            { return Unknown("UnprovedLoopProgress"); }
            count = kind == BinaryOperatorKind.Subtract ? DivideCeiling(Difference(initial, lower!.Value), amount)
                : GeometricCount(initial, amount, descending: true);
            envelope = initial;
            rankKind = kind == BinaryOperatorKind.Subtract ? ResourceLoopKind.DecreasingSubtract : ResourceLoopKind.DecreasingDivide;
        }
        else
        { return Unknown("UnprovedLoopProgress"); }
        // Every continuing path must reach this tail update. The closed body
        // visitor rejects other counter writes, aliases, jumps and continues.
        _localProgress.Remove(counter.Local);
        _localInitialConstants.Remove(counter.Local);
        _counters.Add(counter.Local, new Bound(envelope.Expression, ResourceExpression.Zero));
        var body = Sequence(operations.Take(operations.Length - 1), depth + 1);
        _counters.Remove(counter.Local);
        _localProgress.Remove(counter.Local);
        _localInitialConstants.Remove(counter.Local);
        var rank = new ResourceLoopRank(rankKind, initial.Expression, comparison.Expression, amount,
            counter.Local.Type.SpecialType == SpecialType.System_Int32 ? 32 : 64, checkedStep, initial.Lower, comparison.Lower);
        return body.Scale(count, rank, ResourceRepeatKind.Loop);
    }

    private Cost PhasedWhile(IWhileLoopOperation loop, IBinaryOperation condition, int depth)
    {
        loop = ArgumentNullGuard.NotNull(loop, nameof(loop));
        condition = ArgumentNullGuard.NotNull(condition, nameof(condition));
        var left = PositiveCounter(condition.LeftOperand);
        var right = PositiveCounter(condition.RightOperand);
        if (left is null || right is null || SymbolEqualityComparer.Default.Equals(left, right))
        { return Unknown("UnsupportedPhaseGuard"); }
        if (!_localProgress.TryGetValue(left, out var leftInitial) || !_localProgress.TryGetValue(right, out var rightInitial))
        { return Unknown("UnprovedLoopProgress"); }
        var operations = loop.Body is IBlockOperation block ? block.Operations : ImmutableArray.Create(loop.Body);
        if (operations.Length != 1 || operations[0] is not IConditionalOperation { WhenFalse: { } otherwise } select ||
            !SymbolEqualityComparer.Default.Equals(PositiveCounter(select.Condition), left))
        { return Unknown("UnsupportedPhaseSelection"); }
        var leftPhase = ReadCounterPhase(select.WhenTrue, left, leftInitial);
        var rightPhase = ReadCounterPhase(otherwise, right, rightInitial);
        if (leftPhase is null || rightPhase is null)
        { return Unknown("UnprovedLoopProgress"); }
        // Each continuing branch changes only its selected positive counter.
        // The other phase's initial value therefore remains a valid envelope.
        _localProgress.Remove(left);
        _localProgress.Remove(right);
        _localInitialConstants.Remove(left);
        _localInitialConstants.Remove(right);
        _counters.Add(left, new Bound(leftInitial.Expression, ResourceExpression.Zero));
        _counters.Add(right, new Bound(rightInitial.Expression, ResourceExpression.Zero));
        try
        {
            var leftCost = Sequence(leftPhase.Value.Prefix, depth + 1);
            if (_unknown is not null)
            { return Cost.Zero; }
            var rightCost = Sequence(rightPhase.Value.Prefix, depth + 1);
            if (_unknown is not null)
            { return Cost.Zero; }
            return leftCost.Scale(leftPhase.Value.Count, leftPhase.Value.Rank, ResourceRepeatKind.Loop)
                .Add(rightCost.Scale(rightPhase.Value.Count, rightPhase.Value.Rank, ResourceRepeatKind.Loop));
        }
        finally
        {
            _counters.Remove(left);
            _counters.Remove(right);
            _localProgress.Remove(left);
            _localProgress.Remove(right);
            _localInitialConstants.Remove(left);
            _localInitialConstants.Remove(right);
        }
    }

    private static ILocalSymbol? PositiveCounter(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        return operation is IBinaryOperation
        {
            OperatorMethod: null, OperatorKind: BinaryOperatorKind.GreaterThan,
            LeftOperand: ILocalReferenceOperation counter
        } comparison && IntegerConstant(comparison.RightOperand) == 0 &&
            counter.Local.RefKind == RefKind.None &&
            counter.Local.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64
            ? counter.Local : null;
    }

    private (ImmutableArray<IOperation> Prefix, ResourceLoopRank Rank, Bound Count)? ReadCounterPhase(
        IOperation body, ILocalSymbol counter, Bound initial)
    {
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        counter = ArgumentNullGuard.NotNull(counter, nameof(counter));
        initial = ArgumentNullGuard.NotNull(initial, nameof(initial));
        var operations = body is IBlockOperation block ? block.Operations : ImmutableArray.Create(body);
        if (operations.IsEmpty || operations[operations.Length - 1] is not IExpressionStatementOperation step)
        { return null; }
        IOperation target;
        BigInteger amount;
        bool divide;
        bool checkedStep;
        if (step.Operation is IIncrementOrDecrementOperation { OperatorMethod: null, Kind: OperationKind.Decrement } decrement)
        {
            target = decrement.Target;
            amount = 1;
            divide = false;
            checkedStep = decrement.IsChecked;
        }
        else if (step.Operation is ICompoundAssignmentOperation { OperatorMethod: null } compound &&
            !compound.InConversion.IsUserDefined && !compound.OutConversion.IsUserDefined &&
            compound.OperatorKind is BinaryOperatorKind.Subtract or BinaryOperatorKind.Divide &&
            IntegerConstant(compound.Value) is { } constant && constant > 0)
        {
            target = compound.Target;
            amount = constant;
            divide = compound.OperatorKind == BinaryOperatorKind.Divide;
            checkedStep = compound.IsChecked;
        }
        else
        { return null; }
        var width = counter.Type.SpecialType == SpecialType.System_Int32 ? 32 : 64;
        if (target is not ILocalReferenceOperation local || !SymbolEqualityComparer.Default.Equals(local.Local, counter) ||
            amount > (BigInteger.One << (width - 1)) - 1 || divide && amount <= 1)
        { return null; }
        var rank = new ResourceLoopRank(divide ? ResourceLoopKind.DecreasingDivide : ResourceLoopKind.DecreasingSubtract,
            initial.Expression, ResourceExpression.Zero, amount, width, checkedStep, initial.Lower, ResourceExpression.Zero);
        var count = divide ? GeometricCount(initial, amount, descending: true) : DivideCeiling(initial, amount);
        return (operations.Take(operations.Length - 1).ToImmutableArray(), rank, count);
    }
    private static BigInteger? IntegerConstant(IOperation? operation)
    {
        return operation?.ConstantValue is { HasValue: true } value ? value.Value switch
        {
            int number => number,
            long number => number,
            _ => null
        } : null;
    }

    private static Bound Difference(Bound upper, BigInteger lower)
    {
        upper = ArgumentNullGuard.NotNull(upper, nameof(upper));
        return new(ResourceExpression.Difference(upper.Expression, lower), ResourceExpression.Difference(upper.Lower, lower));
    }

    private static Bound DivideCeiling(Bound numerator, BigInteger denominator)
    {
        numerator = ArgumentNullGuard.NotNull(numerator, nameof(numerator));
        return new(ResourceExpression.DivideCeiling(numerator.Expression, denominator), ResourceExpression.DivideCeiling(numerator.Lower, denominator));
    }

    private Bound GeometricCount(Bound size, BigInteger factor, bool descending)
    {
        size = ArgumentNullGuard.NotNull(size, nameof(size));
        return new(ResourceExpression.Geometric(size.Expression, factor, descending, _cancellation), ResourceExpression.Geometric(size.Lower, factor, descending, _cancellation));
    }

    private Cost For(IForLoopOperation loop, int depth)
    {
        loop = ArgumentNullGuard.NotNull(loop, nameof(loop));
        if (loop.Before.Length != 1 || loop.Before[0] is not IVariableDeclarationGroupOperation group ||
            group.Declarations.Length != 1 || group.Declarations[0].Declarators.Length != 1 ||
            group.Declarations[0].Declarators[0] is not { Initializer: { } initializer } declaration ||
            declaration.Symbol.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64) ||
            declaration.Symbol.RefKind != RefKind.None ||
            loop.Condition is not IBinaryOperation { OperatorMethod: null } condition ||
            condition.LeftOperand is not ILocalReferenceOperation counter ||
            !SymbolEqualityComparer.Default.Equals(counter.Local, declaration.Symbol) ||
            loop.AtLoopBottom.Length != 1 ||
            loop.AtLoopBottom[0] is not IExpressionStatementOperation step)
        { return Unknown("UnsupportedLoop"); }

        var increasing = condition.OperatorKind == BinaryOperatorKind.LessThan;
        var decreasing = condition.OperatorKind == BinaryOperatorKind.GreaterThan;
        if (!increasing && !decreasing)
        { return Unknown("UnsupportedLoop"); }
        var comparison = Size(condition.RightOperand);
        if (comparison is null)
        { return Unknown("UnsupportedLoopBound"); }
        var start = IntegerConstant(initializer.Value);
        var initial = start is { } constant ? Bound.Number(BigInteger.Max(0, constant)) : Size(initializer.Value);
        if (initial is null || increasing && (start is null || start < 0) ||
            decreasing && (IntegerConstant(condition.RightOperand) is not { } lowerBound || lowerBound < 0))
        { return Unknown("UnsupportedLoopStart"); }

        IOperation? target;
        BinaryOperatorKind kind;
        BigInteger amount;
        bool checkedStep;
        if (step.Operation is IIncrementOrDecrementOperation { OperatorMethod: null } increment)
        {
            target = increment.Target;
            kind = increment.Kind == OperationKind.Increment ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract;
            amount = 1;
            checkedStep = increment.IsChecked;
        }
        else if (step.Operation is ICompoundAssignmentOperation { OperatorMethod: null } compound &&
            !compound.InConversion.IsUserDefined && !compound.OutConversion.IsUserDefined &&
            IntegerConstant(compound.Value) is { } stride && stride > 0)
        {
            target = compound.Target;
            kind = compound.OperatorKind;
            amount = stride;
            checkedStep = compound.IsChecked;
        }
        else
        { return Unknown("UnsupportedLoop"); }
        if (target is not ILocalReferenceOperation local ||
            !SymbolEqualityComparer.Default.Equals(local.Local, counter.Local))
        { return Unknown("UnsupportedLoop"); }

        Bound count;
        Bound envelope;
        if (increasing && kind is BinaryOperatorKind.Add or BinaryOperatorKind.Multiply)
        {
            var maximum = declaration.Symbol.Type.SpecialType == SpecialType.System_Int32
                ? new BigInteger(int.MaxValue) : new BigInteger(long.MaxValue);
            // For unchecked progress, every possible last update must stay
            // representable. Checked overflow stops the execution and therefore
            // can only reduce this mathematical prefix bound.
            if (!checkedStep && (comparison.Maximum is not { } limit ||
                (kind == BinaryOperatorKind.Add ? limit - 1 + amount > maximum : (limit - 1) * amount > maximum)))
            { return Unknown("LoopCounterMayOverflow"); }
            if (kind == BinaryOperatorKind.Multiply && (start != 1 || amount <= 1))
            { return Unknown("UnprovedLoopProgress"); }
            count = kind == BinaryOperatorKind.Add ? DivideCeiling(Difference(comparison, start!.Value), amount)
                : GeometricCount(comparison, amount, descending: false);
            envelope = comparison;
        }
        else if (decreasing && kind is BinaryOperatorKind.Subtract or BinaryOperatorKind.Divide)
        {
            var lower = IntegerConstant(condition.RightOperand)!.Value;
            if (kind == BinaryOperatorKind.Subtract && !checkedStep &&
                amount > (declaration.Symbol.Type.SpecialType == SpecialType.System_Int32 ? new BigInteger(int.MaxValue) : new BigInteger(long.MaxValue)))
            { return Unknown("LoopCounterMayOverflow"); }
            if (kind == BinaryOperatorKind.Divide && (lower != 0 || amount <= 1))
            { return Unknown("UnprovedLoopProgress"); }
            // Admitted lower bounds are nonnegative. The final subtraction is
            // >= 1 - Int64.MaxValue and therefore cannot wrap.
            count = kind == BinaryOperatorKind.Subtract ? DivideCeiling(Difference(initial, lower), amount)
                : GeometricCount(initial, amount, descending: true);
            envelope = initial;
        }
        else
        { return Unknown("UnprovedLoopProgress"); }
        _counters.Add(declaration.Symbol, new Bound(envelope.Expression, ResourceExpression.Zero));
        var body = Visit(loop.Body, depth + 1);
        _counters.Remove(declaration.Symbol);
        var rankKind = kind switch
        {
            BinaryOperatorKind.Add => ResourceLoopKind.IncreasingAdd,
            BinaryOperatorKind.Multiply => ResourceLoopKind.IncreasingMultiply,
            BinaryOperatorKind.Subtract => ResourceLoopKind.DecreasingSubtract,
            _ => ResourceLoopKind.DecreasingDivide
        };
        var rank = new ResourceLoopRank(rankKind, initial.Expression, comparison.Expression, amount,
            declaration.Symbol.Type.SpecialType == SpecialType.System_Int32 ? 32 : 64, checkedStep, initial.Lower, comparison.Lower);
        return body.Scale(count, rank, ResourceRepeatKind.Loop);
    }
}
