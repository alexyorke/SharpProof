namespace SharpProof.Frontend;

// The one table from a primitive C# type to its CoreCLR storage width.
internal static class ResourceStorageKinds
{
    // Callers that only size fresh storage count float and double by width;
    // callers that model field values leave them unsupported.
    internal static ResourceStorageKind Primitive(SpecialType type, bool includeFloatingPoint)
    {
        return type switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => ResourceStorageKind.Byte,
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => ResourceStorageKind.Word16,
            SpecialType.System_Int32 or SpecialType.System_UInt32 => ResourceStorageKind.Word32,
            SpecialType.System_Int64 or SpecialType.System_UInt64 => ResourceStorageKind.Word64,
            SpecialType.System_Single when includeFloatingPoint => ResourceStorageKind.Word32,
            SpecialType.System_Double when includeFloatingPoint => ResourceStorageKind.Word64,
            _ => ResourceStorageKind.Unspecified
        };
    }
}
