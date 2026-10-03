namespace SharpProof.Frontend;

// Type mapping has no dependency on operation visitors or variable bindings.
internal sealed class RoslynTypeMapper(IrFactory factory)
{
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    internal Func<ITypeSymbol?, ITypeSymbol?> TypeSpecializer = static type => type;

    internal IrTypeId GetTypeId(ITypeSymbol? type)
    {
        type = TypeSpecializer(type);
        if (type == null)
        {
            return _factory.ObjectType;
        }

        if (type.TypeKind == TypeKind.Error)
        {
            return _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                "error:" + CompilerIdentityBridge.CreateTypeDisplay(type));
        }

        if (_factory.Semantics == IrExecutionSemantics.Total &&
            CSharpOperationSemantics.MapType(_factory, type.SpecialType) is { } scalar)
        {
            return scalar;
        }

        // Total IR keeps multidimensional arrays as opaque references.
        if (type is IArrayTypeSymbol array && (array.IsSZArray || _factory.Semantics != IrExecutionSemantics.Total))
        {
            var element = GetTypeId(array.ElementType);
            return _factory.GetOrCreateSequenceType(
                CompilerIdentityBridge.InternType(_factory, array), element,
                CompilerIdentityBridge.CreateTypeDisplay(array));
        }
        if (CSharpOperationSemantics.IsSupportedInteger(type.SpecialType))
        {
            return _factory.IntegerType;
        }

        return CSharpOperationSemantics.TryGetBuiltInType(
                _factory, type.SpecialType) ??
            _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                CompilerIdentityBridge.CreateTypeDisplay(type));
    }

    internal bool IsSupportedValueDomain(ITypeSymbol? type)
    {
        return CompilerIdentityBridge.IsSupportedValueDomain(
            TypeSpecializer(type));
    }
}
