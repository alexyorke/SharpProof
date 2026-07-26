namespace SharpProof.Frontend;

internal static class RoslynOperatorSemantics {
    internal static IrBinaryOperator? MapBinary(
        BinaryOperatorKind kind,
        SpecialType resultType) =>
        kind switch {
            BinaryOperatorKind.Add
                when resultType == SpecialType.System_String =>
                IrBinaryOperator.StringConcat,
            BinaryOperatorKind.Add => IrBinaryOperator.Add,
            BinaryOperatorKind.Subtract => IrBinaryOperator.Subtract,
            BinaryOperatorKind.Multiply => IrBinaryOperator.Multiply,
            BinaryOperatorKind.Divide => IrBinaryOperator.Divide,
            BinaryOperatorKind.Remainder => IrBinaryOperator.Remainder,
            BinaryOperatorKind.ConditionalAnd => IrBinaryOperator.AndAlso,
            BinaryOperatorKind.ConditionalOr => IrBinaryOperator.OrElse,
            BinaryOperatorKind.Equals => IrBinaryOperator.Equal,
            BinaryOperatorKind.NotEquals => IrBinaryOperator.NotEqual,
            BinaryOperatorKind.LessThan => IrBinaryOperator.LessThan,
            BinaryOperatorKind.LessThanOrEqual =>
                IrBinaryOperator.LessThanOrEqual,
            BinaryOperatorKind.GreaterThan => IrBinaryOperator.GreaterThan,
            BinaryOperatorKind.GreaterThanOrEqual =>
                IrBinaryOperator.GreaterThanOrEqual,
            _ => null
        };

    internal static bool IsIntegerArithmetic(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.Add or
            BinaryOperatorKind.Subtract or
            BinaryOperatorKind.Multiply or
            BinaryOperatorKind.Divide or
            BinaryOperatorKind.Remainder;

    internal static bool RequiresCheckedArithmetic(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.Add or
            BinaryOperatorKind.Subtract or
            BinaryOperatorKind.Multiply;

    internal static bool IsValuePreservingIntegerConversion(
        SpecialType source,
        SpecialType target) {
        var sourceRange = GetIntegerRange(source);
        var targetRange = GetIntegerRange(target);
        return sourceRange.HasValue &&
               targetRange.HasValue &&
               sourceRange.Value.Minimum >= targetRange.Value.Minimum &&
               sourceRange.Value.Maximum <= targetRange.Value.Maximum;
    }

    private static IntegerRange? GetIntegerRange(SpecialType type) =>
        type switch {
            SpecialType.System_SByte => new(sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte => new(byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 => new(short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 => new(ushort.MinValue, ushort.MaxValue),
            SpecialType.System_Char => new(char.MinValue, char.MaxValue),
            SpecialType.System_Int32 => new(int.MinValue, int.MaxValue),
            SpecialType.System_UInt32 => new(uint.MinValue, uint.MaxValue),
            SpecialType.System_Int64 => new(long.MinValue, long.MaxValue),
            _ => null
        };

    private readonly struct IntegerRange(long minimum, long maximum) {
        internal long Minimum { get; } = minimum;
        internal long Maximum { get; } = maximum;
    }
}
