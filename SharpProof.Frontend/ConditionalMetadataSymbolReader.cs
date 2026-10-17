using System.Reflection;
using System.Reflection.Metadata;

namespace SharpProof.Frontend;

// Reads conditional attributes while preserving successfully decoded symbols.
internal static class ConditionalMetadataSymbolReader
{
    internal static void AppendSymbols(MetadataReader reader, MethodDefinitionHandle handle,
        ImmutableArray<string>.Builder symbols)
    {
        try
        {
            foreach (var attributeHandle in reader.GetMethodDefinition(handle).GetCustomAttributes())
            {
                try
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);
                    if (IsMetadataConditionalConstructor(reader, attribute) &&
                        ReadMetadataConditionalSymbol(reader, attribute.Value) is { } symbol)
                    { symbols.Add(symbol); }
                }
                catch (BadImageFormatException)
                { }
            }
        }
        catch (BadImageFormatException)
        { }
    }

    private static bool IsMetadataConditionalConstructor(MetadataReader reader, CustomAttribute attribute)
    {
        var constructor = attribute.Constructor;
        EntityHandle owner;
        StringHandle name;
        BlobHandle signature;
        if (constructor.Kind == HandleKind.MethodDefinition)
        {
            var definition = reader.GetMethodDefinition((MethodDefinitionHandle)constructor);
            owner = definition.GetDeclaringType();
            name = definition.Name;
            signature = definition.Signature;
        }
        else
        {
            var reference = reader.GetMemberReference((MemberReferenceHandle)constructor);
            owner = reference.Parent;
            name = reference.Name;
            signature = reference.Signature;
        }
        if (reader.GetString(name) != ".ctor" || !IsMetadataConditionalType(reader, owner))
        { return false; }
        var blob = reader.GetBlobReader(signature);
        return blob.ReadByte() == 0x20 && blob.ReadByte() == 0x01 &&
            blob.ReadByte() == 0x01 &&
            blob.ReadSignatureTypeCode() == SignatureTypeCode.String && blob.RemainingBytes == 0;
    }

    private static bool IsMetadataConditionalType(MetadataReader reader, EntityHandle owner)
    {
        StringHandle name;
        StringHandle namespaceName;
        switch (owner.Kind)
        {
            case HandleKind.TypeDefinition:
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)owner);
                if ((definition.Attributes & TypeAttributes.VisibilityMask) is not (TypeAttributes.NotPublic or TypeAttributes.Public))
                { return false; }
                name = definition.Name;
                namespaceName = definition.Namespace;
                break;
            case HandleKind.TypeReference:
                var reference = reader.GetTypeReference((TypeReferenceHandle)owner);
                if (reference.ResolutionScope.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference)
                { return false; }
                name = reference.Name;
                namespaceName = reference.Namespace;
                break;
            default:
                return false;
        }
        return reader.GetString(name) == "ConditionalAttribute" &&
            reader.GetString(namespaceName) == "System.Diagnostics";
    }

    private static string? ReadMetadataConditionalSymbol(MetadataReader reader, BlobHandle value)
    {
        if (value.IsNil)
        { return null; }
        var blob = reader.GetBlobReader(value);
        if (blob.Length <= 4 || blob.ReadByte() != 1 || blob.ReadByte() != 0 ||
            !blob.TryReadCompressedInteger(out var length) || blob.RemainingBytes < length)
        { return null; }
        // Match compiler string extraction; named/trailing data is separate.
        return blob.ReadUTF8(length).TrimEnd('\0');
    }

}
