namespace SharpProof.Frontend;

// `float`, `double` and `decimal` values are opaque: they may be stored,
// passed, returned and given to opaque calls, but the IR knows nothing of
// their contents. Rounding, NaN and signed zeros are not modeled, so no
// operation over them is exact integer reasoning.
internal static partial class CSharpOperationSemantics
{
    internal static bool IsFloatingPoint(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
    }

    private static bool IsBinaryFloatingPoint(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double;
    }

    // Built-in operations whose result is an unknown value and that run no
    // user code and never throw, in a checked context or not:
    // - `+`, `-`, `*`, `/`, `%`, unary `+` and `-`, and the six comparisons
    //   over `float` or `double` operands of one type. IEEE arithmetic gives
    //   an infinity or NaN instead of faulting, and `checked` applies only to
    //   integer operations. A comparison with NaN is false, and `-0.0 == 0.0`,
    //   so no comparison is integer or reference equality of its operands.
    // - A numeric conversion into `float`, `double` or `decimal` from a
    //   built-in integer or `char`, and from `float` to `double`. Each is exact
    //   or rounds; none overflows, since even `ulong.MaxValue` and
    //   `decimal`'s 96-bit range fit. The `decimal` conversions build the value
    //   in place and read no static state.
    // `decimal` arithmetic (which throws on overflow and division by zero),
    // conversions out of floating point (which throw in a checked context, or
    // for `decimal`, and otherwise give platform-dependent values), lifted and
    // user-defined operators stay unsupported.
    internal static bool IsOpaqueFloatingOperation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        switch (operation)
        {
            case IBinaryOperation { IsLifted: false, OperatorMethod: null } binary:
                return binary.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or
                        BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder or BinaryOperatorKind.Equals or
                        BinaryOperatorKind.NotEquals or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
                        BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual &&
                    IsBinaryFloatingPoint(binary.LeftOperand.Type) &&
                    binary.RightOperand.Type?.SpecialType == binary.LeftOperand.Type!.SpecialType &&
                    (binary.Type?.SpecialType == SpecialType.System_Boolean ||
                        binary.Type?.SpecialType == binary.LeftOperand.Type.SpecialType);
            case IUnaryOperation { IsLifted: false, OperatorMethod: null, OperatorKind: UnaryOperatorKind.Minus or UnaryOperatorKind.Plus } unary:
                return IsBinaryFloatingPoint(unary.Operand.Type) && unary.Type?.SpecialType == unary.Operand.Type!.SpecialType;
            case IConversionOperation
            {
                IsTryCast: false, OperatorMethod: null,
                Conversion: { Exists: true, IsNumeric: true, IsUserDefined: false, IsNullable: false }
            } conversion:
                {
                    var source = conversion.Operand.Type?.SpecialType ?? SpecialType.None;
                    return IsFloatingPoint(conversion.Type) && conversion.Operand.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } &&
                        (TryGetScalarInteger(source, out _) ||
                            source == SpecialType.System_Single && conversion.Type!.SpecialType == SpecialType.System_Double);
                }
            default:
                return false;
        }
    }
}
