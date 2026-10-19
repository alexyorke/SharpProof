using System.Numerics;

namespace SharpProof.Compiler.Semantics;

internal sealed partial class ResourceBoundsAnalysis
{
    private Cost While(IWhileLoopOperation loop, int depth)
    {
        loop = ArgumentNullGuard.NotNull(loop, nameof(loop));
        if (!loop.ConditionIsTop || loop.ConditionIsUntil ||
            loop.Condition is not IBinaryOperation { OperatorKind: BinaryOperatorKind.GreaterThan, OperatorMethod: null } condition ||
            condition.LeftOperand is not ILocalReferenceOperation counter ||
            IntegerConstant(condition.RightOperand) != 0)
        { return Unknown("UnsupportedLoop"); }
        if (!_localProgress.TryGetValue(counter.Local, out var initial))
        { return Unknown("UnprovedLoopProgress"); }
        var operations = loop.Body is IBlockOperation block ? block.Operations : ImmutableArray.Create(loop.Body);
        if (operations.IsEmpty || operations[operations.Length - 1] is not IExpressionStatementOperation step)
        { return Unknown("UnprovedLoopProgress"); }
        IOperation? target;
        BigInteger amount;
        bool geometric;
        if (step.Operation is IIncrementOrDecrementOperation { OperatorMethod: null, Kind: OperationKind.Decrement } decrement)
        {
            target = decrement.Target;
            amount = 1;
            geometric = false;
        }
        else if (step.Operation is ICompoundAssignmentOperation { OperatorMethod: null } compound &&
            !compound.InConversion.IsUserDefined && !compound.OutConversion.IsUserDefined &&
            compound.OperatorKind is BinaryOperatorKind.Subtract or BinaryOperatorKind.Divide &&
            IntegerConstant(compound.Value) is { } constant && constant > 0)
        {
            target = compound.Target;
            amount = constant;
            geometric = compound.OperatorKind == BinaryOperatorKind.Divide;
        }
        else
        { return Unknown("UnprovedLoopProgress"); }
        if (target is not ILocalReferenceOperation local ||
            !SymbolEqualityComparer.Default.Equals(local.Local, counter.Local) ||
            counter.Local.RefKind != RefKind.None ||
            counter.Local.Type.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_Int64) ||
            amount > (counter.Local.Type.SpecialType == SpecialType.System_Int32 ? new BigInteger(int.MaxValue) : new BigInteger(long.MaxValue)) ||
            geometric && amount <= 1)
        { return Unknown("UnprovedLoopProgress"); }
        var count = geometric ? GeometricCount(initial, amount, descending: true) : DivideCeiling(initial, amount);
        // The rank is the nonnegative counter size. Every continuing path must
        // reach the checked tail update: all other writes, aliases, jumps and
        // continue statements in the prefix are rejected by the closed visitor.
        _localProgress.Remove(counter.Local);
        _counters.Add(counter.Local, initial);
        var body = Sequence(operations.Take(operations.Length - 1), depth + 1);
        _counters.Remove(counter.Local);
        _localProgress.Remove(counter.Local);
        return new Cost(Bound.One, Bound.Zero, Bound.Zero).Add(body).Scale(count);
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
        return new(ResourceExpression.Difference(upper.Expression, lower));
    }

    private static Bound DivideCeiling(Bound numerator, BigInteger denominator)
    {
        numerator = ArgumentNullGuard.NotNull(numerator, nameof(numerator));
        return new(ResourceExpression.DivideCeiling(numerator.Expression, denominator));
    }

    private Bound GeometricCount(Bound size, BigInteger factor, bool descending)
    {
        size = ArgumentNullGuard.NotNull(size, nameof(size));
        return new(ResourceExpression.Geometric(size.Expression, factor, descending, _cancellation));
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
        _counters.Add(declaration.Symbol, envelope);
        var body = Visit(loop.Body, depth + 1);
        _counters.Remove(declaration.Symbol);
        return new Cost(Bound.One, Bound.Zero, Bound.Zero).Add(body).Scale(count);
    }
}
