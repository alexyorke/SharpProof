namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    private static TotalScalarRule ConvertInteger(IrFactory factory, IrTerm value, IrTypeId target, bool isChecked)
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

    internal static TotalScalarRule Compound(IrFactory factory, ICompoundAssignmentOperation operation, IrTerm left, IrTerm right)
    {
        if (operation.IsLifted || operation.OperatorMethod != null || operation.InConversion.IsUserDefined || operation.OutConversion.IsUserDefined ||
            !TryBinary(operation.OperatorKind, out var kind) || kind is not (IrBinaryOperator.Add or IrBinaryOperator.Subtract or IrBinaryOperator.Multiply))
        { return Fail(factory, FrontendAbstention.UnsupportedMutation); }
        var first = factory.GetTypeInfo(left.Type);
        var second = factory.GetTypeInfo(right.Type);
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
        var rule = IntegerArithmetic(factory, kind, factory.Cast(promoted, left), factory.Cast(promoted, right), operation.IsChecked);
        var conversion = ConvertInteger(factory, rule.Value, left.Type, operation.IsChecked);
        return new(conversion.Value, [.. rule.Throws, .. conversion.Throws], conversion.Classification);
    }

    private static TotalScalarRule IntegerArithmetic(IrFactory factory, IrBinaryOperator kind, IrTerm left, IrTerm right, bool isChecked)
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
}
