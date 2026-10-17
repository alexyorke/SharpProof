using System.Reflection.Metadata;

namespace SharpProof.CompilerArtifact;

// Classify decoded metadata types without admitting unsupported scalar shapes.
internal sealed class CompilerScalarSignatureProvider : ISignatureTypeProvider<SpecialType, object?>
{
    public SpecialType GetPrimitiveType(PrimitiveTypeCode code)
    {
        return code switch
        {
            PrimitiveTypeCode.Boolean => SpecialType.System_Boolean,
            PrimitiveTypeCode.Char => SpecialType.System_Char,
            PrimitiveTypeCode.SByte => SpecialType.System_SByte,
            PrimitiveTypeCode.Byte => SpecialType.System_Byte,
            PrimitiveTypeCode.Int16 => SpecialType.System_Int16,
            PrimitiveTypeCode.UInt16 => SpecialType.System_UInt16,
            PrimitiveTypeCode.Int32 => SpecialType.System_Int32,
            PrimitiveTypeCode.UInt32 => SpecialType.System_UInt32,
            PrimitiveTypeCode.Int64 => SpecialType.System_Int64,
            PrimitiveTypeCode.UInt64 => SpecialType.System_UInt64,
            PrimitiveTypeCode.Object => SpecialType.System_Object,
            PrimitiveTypeCode.String => SpecialType.System_String,
            _ => SpecialType.None
        };
    }
    public SpecialType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    { if (reader is null) { throw new System.ArgumentNullException(nameof(reader)); } return SpecialType.None; }
    public SpecialType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    { if (reader is null) { throw new System.ArgumentNullException(nameof(reader)); } return SpecialType.None; }
    public SpecialType GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    { if (reader is null) { throw new System.ArgumentNullException(nameof(reader)); } return SpecialType.None; }
    public SpecialType GetSZArrayType(SpecialType elementType)
    { return SpecialType.None; }
    public SpecialType GetPointerType(SpecialType elementType)
    { return SpecialType.None; }
    public SpecialType GetByReferenceType(SpecialType elementType)
    { return SpecialType.None; }
    public SpecialType GetPinnedType(SpecialType elementType)
    { return SpecialType.None; }
    public SpecialType GetModifiedType(SpecialType modifier, SpecialType unmodifiedType, bool isRequired)
    { return SpecialType.None; }
    public SpecialType GetArrayType(SpecialType elementType, ArrayShape shape)
    { return SpecialType.None; }
    public SpecialType GetGenericInstantiation(SpecialType genericType, ImmutableArray<SpecialType> typeArguments)
    { if (typeArguments.IsDefault) { throw new System.ArgumentException("Array must be initialized.", nameof(typeArguments)); } return SpecialType.None; }
    public SpecialType GetGenericMethodParameter(object? genericContext, int index)
    { return SpecialType.None; }
    public SpecialType GetGenericTypeParameter(object? genericContext, int index)
    { return SpecialType.None; }
    public SpecialType GetFunctionPointerType(MethodSignature<SpecialType> signature)
    { return SpecialType.None; }
}
