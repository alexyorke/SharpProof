using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace SharpProof.Identity;

internal static class EcmaStructuralMethodIdentityAdapter
{
    internal static StructuralMethodIdentity Create(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var signature = definition.DecodeSignature(new StructuralTypeProvider(reader), null);
        var parameterAttributes = GetParameterAttributes(reader, definition);
        var parameters = ImmutableArray.CreateBuilder<StructuralParameterIdentity>(signature.ParameterTypes.Length);
        for (var index = 0; index < signature.ParameterTypes.Length; index++)
        {
            parameterAttributes.TryGetValue(index + 1, out var attributes);
            var parameterType = signature.ParameterTypes[index];
            parameters.Add(new StructuralParameterIdentity(
                parameterType.Key,
                GetParameterRefKind(parameterType, attributes)));
        }

        parameterAttributes.TryGetValue(0, out var returnAttributes);
        var metadataName = reader.GetString(definition.Name);
        return new StructuralMethodIdentity(
            GetTypeDefinitionMetadataName(reader, definition.GetDeclaringType()),
            GetMethodKind(metadataName),
            GetLogicalName(metadataName),
            definition.GetGenericParameters().Count,
            parameters,
            signature.ReturnType.Key,
            GetReturnRefKind(signature.ReturnType, returnAttributes));
    }

    internal static StructuralMethodIdentity Create(MetadataReader reader, MemberReferenceHandle handle)
    {
        var reference = reader.GetMemberReference(handle);
        var signature = reference.DecodeMethodSignature(new StructuralTypeProvider(reader), null);
        var metadataName = reader.GetString(reference.Name);
        return new StructuralMethodIdentity(
            GetMemberReferenceContainingMetadataType(reader, reference.Parent),
            GetMethodKind(metadataName),
            GetLogicalName(metadataName),
            signature.GenericParameterCount,
            signature.ParameterTypes.Select(static parameter => new StructuralParameterIdentity(
                parameter.Key,
                parameter.IsByRef ? "ref" : "none")),
            signature.ReturnType.Key,
            signature.ReturnType.IsByRef
                ? signature.ReturnType.IsReadOnlyModifier ? "ref-readonly" : "ref"
                : "none");
    }

    internal static string GetCanonicalKey(MetadataReader reader, MethodDefinitionHandle handle)
    {
        return Create(reader, handle).ToCanonicalKey();
    }

    private static Dictionary<int, ParameterAttributes> GetParameterAttributes(
        MetadataReader reader,
        MethodDefinition definition)
    {
        var result = new Dictionary<int, ParameterAttributes>();
        foreach (var parameterHandle in definition.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            result[parameter.SequenceNumber] = parameter.Attributes;
        }

        return result;
    }

    private static string GetParameterRefKind(StructuralDecodedType type, ParameterAttributes attributes)
    {
        if (!type.IsByRef) return "none";
        if ((attributes & ParameterAttributes.Out) != 0) return "out";
        if ((attributes & ParameterAttributes.In) != 0) return "in";
        return type.IsReadOnlyModifier ? "ref-readonly" : "ref";
    }

    private static string GetReturnRefKind(StructuralDecodedType type, ParameterAttributes attributes)
    {
        if (!type.IsByRef) return "none";
        return type.IsReadOnlyModifier || (attributes & ParameterAttributes.In) != 0
            ? "ref-readonly"
            : "ref";
    }

    private static string GetMethodKind(string metadataName)
    {
        if (metadataName == ".ctor") return "constructor";
        if (metadataName == ".cctor") return "static-constructor";
        if (metadataName.StartsWith("get_", StringComparison.Ordinal)) return "property-get";
        if (metadataName.StartsWith("set_", StringComparison.Ordinal)) return "property-set";
        if (metadataName.StartsWith("add_", StringComparison.Ordinal)) return "event-add";
        if (metadataName.StartsWith("remove_", StringComparison.Ordinal)) return "event-remove";
        if (metadataName is "op_Implicit" or "op_Explicit" or "op_CheckedImplicit" or "op_CheckedExplicit")
            return "conversion";
        if (metadataName.StartsWith("op_", StringComparison.Ordinal)) return "operator";
        if (metadataName == "Finalize") return "destructor";
        return "ordinary";
    }

    private static string GetLogicalName(string metadataName)
    {
        if (metadataName.StartsWith("get_", StringComparison.Ordinal) ||
            metadataName.StartsWith("set_", StringComparison.Ordinal) ||
            metadataName.StartsWith("add_", StringComparison.Ordinal))
            return metadataName.Substring(4);
        if (metadataName.StartsWith("remove_", StringComparison.Ordinal)) return metadataName.Substring(7);
        return metadataName;
    }

    private static string GetMemberReferenceContainingMetadataType(MetadataReader reader, EntityHandle parent)
    {
        return parent.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeDefinitionMetadataName(reader, (TypeDefinitionHandle)parent),
            HandleKind.TypeReference => GetTypeReferenceMetadataName(reader, (TypeReferenceHandle)parent),
            HandleKind.TypeSpecification => GetDefinitionNameFromTypeKey(
                reader.GetTypeSpecification((TypeSpecificationHandle)parent)
                    .DecodeSignature(new StructuralTypeProvider(reader), null).Key),
            HandleKind.MethodDefinition => GetTypeDefinitionMetadataName(
                reader,
                reader.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType()),
            _ => "<unknown>"
        };
    }

    private static string GetDefinitionNameFromTypeKey(string key)
    {
        if (!key.StartsWith("named:", StringComparison.Ordinal)) return key;
        var start = "named:".Length;
        var bracket = key.IndexOf('[', start);
        return bracket < 0 ? key.Substring(start) : key.Substring(start, bracket - start);
    }

    internal static string GetTypeDefinitionMetadataName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil)
            return GetTypeDefinitionMetadataName(reader, declaringType) + "+" + reader.GetString(definition.Name);

        var namespaceName = reader.GetString(definition.Namespace);
        var name = reader.GetString(definition.Name);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }

    internal static string GetTypeReferenceMetadataName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return GetTypeReferenceMetadataName(reader, (TypeReferenceHandle)reference.ResolutionScope) + "+" +
                   reader.GetString(reference.Name);

        var namespaceName = reader.GetString(reference.Namespace);
        var name = reader.GetString(reference.Name);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }
}

internal readonly record struct StructuralDecodedType(
    string Key,
    bool IsByRef = false,
    bool IsReadOnlyModifier = false);

internal sealed class StructuralTypeProvider : ISignatureTypeProvider<StructuralDecodedType, object?>
{
    private readonly MetadataReader _reader;

    internal StructuralTypeProvider(MetadataReader reader)
    {
        _reader = reader;
    }

    public StructuralDecodedType GetArrayType(StructuralDecodedType elementType, ArrayShape shape)
    {
        return new StructuralDecodedType("array:" + shape.Rank + "[" + elementType.Key + "]");
    }

    public StructuralDecodedType GetByReferenceType(StructuralDecodedType elementType)
    {
        return elementType with { IsByRef = true };
    }

    public StructuralDecodedType GetFunctionPointerType(MethodSignature<StructuralDecodedType> signature)
    {
        var parameters = string.Join(
            ";",
            signature.ParameterTypes.Select(static parameter =>
                (parameter.IsByRef ? "ref" : "none") + ":" + parameter.Key));
        var returnRefKind = signature.ReturnType.IsByRef
            ? signature.ReturnType.IsReadOnlyModifier ? "ref-readonly" : "ref"
            : "none";
        return new StructuralDecodedType(
            "fnptr:" + signature.Header.CallingConvention.ToString().ToLowerInvariant() + "(" + parameters +
            ")->" + returnRefKind + ":" + signature.ReturnType.Key);
    }

    public StructuralDecodedType GetGenericInstantiation(
        StructuralDecodedType genericType,
        ImmutableArray<StructuralDecodedType> typeArguments)
    {
        return new StructuralDecodedType(
            genericType.Key + "[" + string.Join(";", typeArguments.Select(static argument => argument.Key)) + "]");
    }

    public StructuralDecodedType GetGenericMethodParameter(object? genericContext, int index)
    {
        return new StructuralDecodedType("mparam:" + index);
    }

    public StructuralDecodedType GetGenericTypeParameter(object? genericContext, int index)
    {
        return new StructuralDecodedType("tparam:" + index);
    }

    public StructuralDecodedType GetModifiedType(
        StructuralDecodedType modifier,
        StructuralDecodedType unmodifiedType,
        bool isRequired)
    {
        var isReadOnly = isRequired &&
                         (modifier.Key.Contains(
                              "System.Runtime.CompilerServices.IsReadOnlyAttribute",
                              StringComparison.Ordinal) ||
                          modifier.Key.Contains(
                              "System.Runtime.InteropServices.InAttribute",
                              StringComparison.Ordinal));
        return unmodifiedType with { IsReadOnlyModifier = unmodifiedType.IsReadOnlyModifier || isReadOnly };
    }

    public StructuralDecodedType GetPinnedType(StructuralDecodedType elementType)
    {
        return elementType;
    }

    public StructuralDecodedType GetPointerType(StructuralDecodedType elementType)
    {
        return new StructuralDecodedType("pointer[" + elementType.Key + "]");
    }

    public StructuralDecodedType GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return new StructuralDecodedType("named:" + (typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.Void => "System.Void",
            _ => "<unknown>"
        }));
    }

    public StructuralDecodedType GetSZArrayType(StructuralDecodedType elementType)
    {
        return new StructuralDecodedType("array:1[" + elementType.Key + "]");
    }

    public StructuralDecodedType GetTypeFromDefinition(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        byte rawTypeKind)
    {
        return new StructuralDecodedType(
            "named:" + EcmaStructuralMethodIdentityAdapter.GetTypeDefinitionMetadataName(reader, handle));
    }

    public StructuralDecodedType GetTypeFromReference(
        MetadataReader reader,
        TypeReferenceHandle handle,
        byte rawTypeKind)
    {
        return new StructuralDecodedType(
            "named:" + EcmaStructuralMethodIdentityAdapter.GetTypeReferenceMetadataName(reader, handle));
    }

    public StructuralDecodedType GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind)
    {
        return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
