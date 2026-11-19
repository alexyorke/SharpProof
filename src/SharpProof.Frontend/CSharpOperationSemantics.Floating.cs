namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsFloatingPoint(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
    }

    // Built-in integer conversions to these types round or preserve the value
    // without overflow. Widening float to double is also nonthrowing.
    internal static bool IsOpaqueFloatingConversion(IConversionOperation conversion)
    {
        conversion = RepositoryParameterGuard.NotNull(conversion, nameof(conversion));
        return conversion is { IsTryCast: false, OperatorMethod: null } &&
            conversion.Conversion is { Exists: true, IsNumeric: true, IsUserDefined: false, IsNullable: false } &&
            IsFloatingPoint(conversion.Type) &&
            conversion.Operand.Type is { TypeKind: not TypeKind.Enum } source &&
            (TryGetScalarInteger(source.SpecialType, out _) ||
                source.SpecialType == SpecialType.System_Single && conversion.Type!.SpecialType == SpecialType.System_Double);
    }
}
