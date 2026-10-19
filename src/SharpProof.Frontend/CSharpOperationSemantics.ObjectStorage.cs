namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static ImmutableArray<ResourceStorageKind>? CompleteObjectStorage(ITypeSymbol? candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidate is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Class, IsRecord: false, IsAnonymousType: false,
                IsGenericType: false, IsComImport: false, BaseType.SpecialType: SpecialType.System_Object
            } type || type.DeclaringSyntaxReferences.IsEmpty || !type.StaticConstructors.IsEmpty)
        { return null; }
        foreach (var attribute in type.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attribute.AttributeClass is
                {
                    MetadataName: "StructLayoutAttribute", ContainingType: null,
                    ContainingNamespace:
                    {
                        Name: "InteropServices", ContainingNamespace:
                        {
                            Name: "Runtime", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                        }
                    }
                })
            { return null; }
        }
        var members = type.GetMembers();
        if (members.Length > 4096)
        { return null; }
        var fields = ImmutableArray.CreateBuilder<ResourceStorageKind>();
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IEventSymbol or IMethodSymbol { MethodKind: MethodKind.Destructor })
            { return null; }
            if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field)
            { continue; }
            if (field.IsFixedSizeBuffer || fields.Count == ResourceAllocationShape.MaximumFields)
            { return null; }
            var storage = field.Type.IsReferenceType ? ResourceStorageKind.Reference : field.Type.SpecialType switch
            {
                SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => ResourceStorageKind.Byte,
                SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => ResourceStorageKind.Word16,
                SpecialType.System_Int32 or SpecialType.System_UInt32 => ResourceStorageKind.Word32,
                SpecialType.System_Int64 or SpecialType.System_UInt64 => ResourceStorageKind.Word64,
                _ => ResourceStorageKind.Unspecified
            };
            if (storage == ResourceStorageKind.Unspecified)
            { return null; }
            fields.Add(storage);
        }
        return fields.ToImmutable();
    }
}
