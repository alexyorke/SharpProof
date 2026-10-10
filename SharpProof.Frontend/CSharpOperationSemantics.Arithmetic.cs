namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static TotalScalarRule Int32MathAbs(IrFactory factory, IrTerm value)
    {
        var type = factory.GetTypeInfo(value.Type);
        if (type.Kind != IrTypeKind.Integer || type.Width != 32 || !type.Signed)
        { return Fail(factory, FrontendAbstention.UnsupportedType); }
        var zero = Number(factory, value.Type, 0);
        var negative = factory.Binary(IrBinaryOperator.LessThan, value, zero);
        var result = factory.Conditional(negative, factory.Binary(IrBinaryOperator.Subtract, zero, value), value);
        return new(result, [new(IrExceptionKind.Overflow,
            factory.Binary(IrBinaryOperator.Equal, value, Number(factory, value.Type, int.MinValue)))],
            FrontendSubsetClassification.Exact);
    }

    internal static TotalScalarRule ConvertInteger(IrFactory factory, IrTerm value, IrTypeId target, bool isChecked)
    {
        if (value.Type == target)
        { return Exact(value); }
        var sourceInfo = factory.GetTypeInfo(value.Type);
        var targetInfo = factory.GetTypeInfo(target);
        if (sourceInfo.Kind != IrTypeKind.Integer || targetInfo.Kind != IrTypeKind.Integer)
        { return Fail(factory, FrontendAbstention.ConversionMayChangeValue); }
        var guards = ImmutableArray.CreateBuilder<TotalThrow>();
        if (isChecked)
        {
            var (sourceMin, sourceMax) = Bounds(sourceInfo);
            var (targetMin, targetMax) = Bounds(targetInfo);
            if (targetMin > sourceMin)
            { guards.Add(new(IrExceptionKind.Overflow, factory.Binary(IrBinaryOperator.LessThan, value, Number(factory, value.Type, targetMin)))); }
            if (targetMax < sourceMax)
            { guards.Add(new(IrExceptionKind.Overflow, factory.Binary(IrBinaryOperator.GreaterThan, value, Number(factory, value.Type, targetMax)))); }
        }
        return new(factory.Cast(target, value), guards.ToImmutable(), FrontendSubsetClassification.Exact);
    }

    internal static TotalScalarRule Increment(IrFactory factory, IIncrementOrDecrementOperation operation, IrTerm value)
    {
        if (operation.IsLifted || operation.OperatorMethod != null || factory.GetTypeInfo(value.Type).Kind != IrTypeKind.Integer)
        { return Fail(factory, FrontendAbstention.UnsupportedMutation); }
        // Built-in ++/-- converts the promoted arithmetic result back to the storage
        // type. Its wrap value and checked range fault are exactly this one-unit step.
        return IntegerArithmetic(factory, operation.Kind == OperationKind.Increment ? IrBinaryOperator.Add : IrBinaryOperator.Subtract,
            value, factory.Integer(value.Type, 1L), operation.IsChecked);
    }

    internal static bool IsShift(BinaryOperatorKind kind)
    {
        return kind is BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift;
    }

    // Built-in shifts take an int count and an int, uint, long or ulong value.
    // The CLR uses only the low five (32-bit) or six (64-bit) count bits; >>
    // is arithmetic for signed values and >>> is always logical. Shifts never
    // overflow, including in a checked context.
    internal static TotalScalarRule Shift(IrFactory factory, BinaryOperatorKind kind, IrTerm value, IrTerm count)
    {
        var info = factory.GetTypeInfo(value.Type);
        var countInfo = factory.GetTypeInfo(count.Type);
        if (!IsShift(kind) || info.Kind != IrTypeKind.Integer || info.Width is not (32 or 64) ||
            countInfo.Kind != IrTypeKind.Integer || countInfo.Width != 32 || !countInfo.Signed)
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        var operand = kind == BinaryOperatorKind.UnsignedRightShift && info.Signed
            ? factory.Cast(factory.GetOrCreateIntegerType(info.Width, false), value) : value;
        // Widening or reinterpreting the count preserves its low six bits.
        var mask = count.Type == operand.Type ? count : factory.Cast(operand.Type, count);
        var shifted = factory.Binary(kind == BinaryOperatorKind.LeftShift ? IrBinaryOperator.ShiftLeft : IrBinaryOperator.ShiftRight,
            operand, mask);
        return Exact(shifted.Type == value.Type ? shifted : factory.Cast(value.Type, shifted));
    }

    internal static bool IsBitwise(IrBinaryOperator kind)
    {
        return kind is IrBinaryOperator.BitwiseAnd or IrBinaryOperator.BitwiseOr or IrBinaryOperator.BitwiseXor;
    }

    // Built-in &, | and ^ apply to promoted integer operands of one type and to
    // bool operands, never overflow, and evaluate both operands. On bool values
    // they are the logical and, or and inequality of the two operand values.
    internal static TotalScalarRule Bitwise(IrFactory factory, IrBinaryOperator kind, IrTerm left, IrTerm right)
    {
        var info = factory.GetTypeInfo(left.Type);
        if (!IsBitwise(kind) || left.Type != right.Type)
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        if (info.Kind == IrTypeKind.Integer)
        { return Exact(factory.Binary(kind, left, right)); }
        if (info.Kind != IrTypeKind.Boolean)
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        return Exact(factory.Binary(kind switch
        {
            IrBinaryOperator.BitwiseAnd => IrBinaryOperator.AndAlso,
            IrBinaryOperator.BitwiseOr => IrBinaryOperator.OrElse,
            _ => IrBinaryOperator.NotEqual
        }, left, right));
    }

    // Built-in ~ takes a promoted int, uint, long or ulong operand: ~x == x ^ all-ones.
    internal static TotalScalarRule Complement(IrFactory factory, IrTerm value, IrTypeId target)
    {
        var info = factory.GetTypeInfo(value.Type);
        if (value.Type != target || info.Kind != IrTypeKind.Integer || info.Width is not (32 or 64))
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        var ones = info.Width == 64 ? ulong.MaxValue : uint.MaxValue;
        return Exact(factory.Binary(IrBinaryOperator.BitwiseXor, value, factory.IntegerBits(value.Type, ones)));
    }

    // checkOverflow is the compilation's default overflow context, or null when unknown.
    internal static TotalScalarRule Compound(IrFactory factory, ICompoundAssignmentOperation operation, IrTerm left, IrTerm right,
        bool? checkOverflow)
    {
        if (IsShift(operation.OperatorKind))
        { return CompoundShift(factory, operation, left, right, checkOverflow); }
        if (operation.IsLifted || operation.OperatorMethod != null || operation.InConversion.IsUserDefined || operation.OutConversion.IsUserDefined ||
            !TryBinary(operation.OperatorKind, out var kind) ||
            kind is not (IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply) && !IsBitwise(kind))
        { return Fail(factory, FrontendAbstention.UnsupportedMutation); }
        var first = factory.GetTypeInfo(left.Type);
        var second = factory.GetTypeInfo(right.Type);
        // bool &=, |= and ^= combine two bool values without conversions.
        if (first.Kind == IrTypeKind.Boolean && left.Type == right.Type && IsBitwise(kind))
        {
            var logical = Bitwise(factory, kind, left, right);
            return logical.Classification.IsExact ? logical : Fail(factory, FrontendAbstention.UnsupportedMutation);
        }
        if (first.Kind != IrTypeKind.Integer || second.Kind != IrTypeKind.Integer)
        { return Fail(factory, FrontendAbstention.UnsupportedMutation); }
        // Roslyn has already applied RHS conversions, including constant conversions.
        // Numeric binary promotion then chooses the operator type before OutConversion.
        var promoted = first.Width == 64 && !first.Signed || second.Width == 64 && !second.Signed
            ? factory.GetOrCreateIntegerType(64, false)
            : first.Width == 64 || second.Width == 64 || (first.Width == 32 && !first.Signed && second.Signed && second.Width == 32) ||
                (second.Width == 32 && !second.Signed && first.Signed && first.Width == 32)
                ? factory.GetOrCreateIntegerType(64, true)
                : first.Width == 32 && !first.Signed || second.Width == 32 && !second.Signed
                    ? factory.GetOrCreateIntegerType(32, false) : factory.GetOrCreateIntegerType(32, true);
        var rule = IsBitwise(kind)
            ? Exact(factory.Binary(kind, factory.Cast(promoted, left), factory.Cast(promoted, right)))
            : IntegerArithmetic(factory, kind, factory.Cast(promoted, left), factory.Cast(promoted, right), operation.IsChecked);
        var conversion = ConvertInteger(factory, rule.Value, left.Type, operation.IsChecked);
        return new(conversion.Value, [.. rule.Throws, .. conversion.Throws], conversion.Classification);
    }

    internal static TotalScalarRule IntegerArithmetic(IrFactory factory, IrBinaryOperator kind, IrTerm left, IrTerm right, bool isChecked)
    {
        var value = factory.Binary(kind, left, right);
        if (!isChecked)
        { return Exact(value); }
        var info = factory.GetTypeInfo(left.Type);
        var zero = factory.Integer(left.Type, 0L);
        IrTerm Compare(IrBinaryOperator op, IrTerm a, IrTerm b)
        { return factory.Binary(op, a, b); }
        IrTerm And(IrTerm a, IrTerm b)
        { return Compare(IrBinaryOperator.AndAlso, a, b); }
        IrTerm Or(IrTerm a, IrTerm b)
        { return Compare(IrBinaryOperator.OrElse, a, b); }
        IrTerm Negative(IrTerm a)
        { return Compare(IrBinaryOperator.LessThan, a, zero); }
        IrTerm Nonnegative(IrTerm a)
        { return Compare(IrBinaryOperator.GreaterThanOrEqual, a, zero); }
        IrTerm overflow;
        if (kind == IrBinaryOperator.Multiply)
        {
            var (minimum, maximum) = Bounds(info);
            var max = Number(factory, left.Type, maximum);
            var min = Number(factory, left.Type, minimum);
            var rightPositive = Compare(IrBinaryOperator.GreaterThan, right, zero);
            var maxOverRight = Compare(IrBinaryOperator.Divide, max, right);
            overflow = info.Signed
                ? factory.Conditional(Compare(IrBinaryOperator.GreaterThan, left, zero),
                    factory.Conditional(rightPositive, Compare(IrBinaryOperator.GreaterThan, left, maxOverRight),
                        Compare(IrBinaryOperator.LessThan, right, Compare(IrBinaryOperator.Divide, min, left))),
                    And(Negative(left), factory.Conditional(rightPositive,
                        Compare(IrBinaryOperator.LessThan, left, Compare(IrBinaryOperator.Divide, min, right)),
                        And(Negative(right), Compare(IrBinaryOperator.LessThan, left, maxOverRight)))))
                : And(Compare(IrBinaryOperator.NotEqual, right, zero), Compare(IrBinaryOperator.GreaterThan, left, maxOverRight));
        }
        else if (!info.Signed)
        { overflow = Compare(IrBinaryOperator.LessThan, kind == IrBinaryOperator.Add ? value : left, kind == IrBinaryOperator.Add ? left : right); }
        else if (kind == IrBinaryOperator.Add)
        { overflow = Or(And(And(Nonnegative(left), Nonnegative(right)), Negative(value)), And(And(Negative(left), Negative(right)), Nonnegative(value))); }
        else
        { overflow = Or(And(And(Nonnegative(left), Negative(right)), Negative(value)), And(And(Negative(left), Nonnegative(right)), Nonnegative(value))); }
        return new(value, [new(IrExceptionKind.Overflow, overflow)], FrontendSubsetClassification.Exact);
    }

    private static TotalScalarRule CompoundShift(IrFactory factory, ICompoundAssignmentOperation operation, IrTerm left, IrTerm right,
        bool? checkOverflow)
    {
        var info = factory.GetTypeInfo(left.Type);
        if (operation.IsLifted || operation.OperatorMethod != null || operation.InConversion.IsUserDefined ||
            operation.OutConversion.IsUserDefined || info.Kind != IrTypeKind.Integer)
        { return Fail(factory, FrontendAbstention.UnsupportedMutation); }
        // Narrow storage is promoted to int, shifted, then narrowed again.
        var promoted = info.Width < 32 ? factory.Cast(factory.GetOrCreateIntegerType(32, true), left) : left;
        var rule = Shift(factory, operation.OperatorKind, promoted, right);
        if (!rule.Classification.IsExact)
        { return Fail(factory, FrontendAbstention.UnsupportedMutation); }
        // The narrowing storage conversion follows the enclosing checked context
        // even though the shift itself is never checked.
        var isChecked = info.Width < 32 ? CheckedContext(operation, checkOverflow) : false;
        if (isChecked == null)
        { return Fail(factory, FrontendAbstention.UnsupportedMutation); }
        var conversion = ConvertInteger(factory, rule.Value, left.Type, isChecked.Value);
        return new(conversion.Value, conversion.Throws, conversion.Classification);
    }

    private static bool? CheckedContext(IOperation operation, bool? checkOverflow)
    {
        for (var node = operation.Syntax; node != null; node = node.Parent)
        {
            if (node.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CheckedStatement) ||
                node.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CheckedExpression))
            { return true; }
            if (node.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UncheckedStatement) ||
                node.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UncheckedExpression))
            { return false; }
        }
        return checkOverflow;
    }
}
