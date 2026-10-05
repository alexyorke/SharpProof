namespace SharpProof.Frontend;

// Type mapping has no dependency on operation visitors or variable bindings.
internal sealed class RoslynTypeMapper(IrFactory factory)
{
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    internal Func<ITypeSymbol?, ITypeSymbol?> TypeSpecializer = static type => type;
    private readonly Dictionary<ITypeSymbol, bool> _closedSealedTypes = new(SymbolEqualityComparer.Default);

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

        var mapped = CSharpOperationSemantics.TryGetBuiltInType(
                _factory, type.SpecialType) ??
            _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                CompilerIdentityBridge.CreateTypeDisplay(type));
        if (_factory.Semantics == IrExecutionSemantics.Total && _factory.GetTypeInfo(mapped).Kind == IrTypeKind.Reference)
        {
            if (!_closedSealedTypes.TryGetValue(type, out var certified))
            { _closedSealedTypes.Add(type, certified = CompilerIdentityBridge.IsClosedSealedReferenceType(type)); }
            if (certified)
            { _factory.RegisterClosedSealedReferenceType(mapped); }
        }
        return mapped;
    }

    internal bool IsSupportedValueDomain(ITypeSymbol? type)
    {
        return CompilerIdentityBridge.IsSupportedValueDomain(
            TypeSpecializer(type));
    }
}
