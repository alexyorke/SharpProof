namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    /// <summary>
    /// Roslyn represents an increment or decrement that is implemented by
    /// implicit conversions as a predefined operator with no operator method.
    /// The conversion calls are not present in the operation tree, so such an
    /// operation must remain conservative unless its target is one of the
    /// language-defined numeric, enum, or pointer types.
    /// </summary>
    internal static bool IsUnsupportedImplicitIncrement(
        IIncrementOrDecrementOperation increment)
    {
        increment = RepositoryParameterGuard.NotNull(increment, nameof(increment));
        return increment.OperatorMethod == null &&
            !IsPredefinedIncrementType(
                NullableUnderlyingOrSelf(increment.Target.Type));
    }

    private static bool IsPredefinedIncrementType(ITypeSymbol? type)
    {
        return type?.TypeKind is TypeKind.Enum or TypeKind.Pointer ||
            type?.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Char or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_Decimal or
                SpecialType.System_IntPtr or
                SpecialType.System_UIntPtr;
    }

    private static ITypeSymbol? NullableUnderlyingOrSelf(ITypeSymbol? type)
    {
        return GetNullableUnderlyingType(type) ?? type;
    }

    private static ITypeSymbol? GetNullableUnderlyingType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1
        } nullable
            ? nullable.TypeArguments[0]
            : null;
    }
}
