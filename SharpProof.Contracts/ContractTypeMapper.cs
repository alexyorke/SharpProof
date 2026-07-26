namespace SharpProof.Contracts;

internal sealed class ContractTypeMapper(IrFactory factory) {
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    internal IrTypeId GetTypeId(ITypeSymbol? type) {
        if (type == null) return _factory.ObjectType;
        if (type is IArrayTypeSymbol array)
            return _factory.GetOrCreateSequenceType(
                CompilerIdentityBridge.InternType(_factory, array),
                GetTypeId(array.ElementType),
                CompilerIdentityBridge.CreateTypeDisplay(array));
        return type.SpecialType switch {
            SpecialType.System_Boolean => _factory.BooleanType,
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Char or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 => _factory.IntegerType,
            SpecialType.System_String => _factory.StringType,
            SpecialType.System_Object => _factory.ObjectType,
            _ => _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                CompilerIdentityBridge.CreateTypeDisplay(type))
        };
    }
}
