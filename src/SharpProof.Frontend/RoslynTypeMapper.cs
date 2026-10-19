namespace SharpProof.Frontend;

// Type mapping has no dependency on operation visitors or variable bindings.
internal sealed class RoslynTypeMapper(IrFactory factory)
{
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    internal Func<ITypeSymbol?, ITypeSymbol?> TypeSpecializer = static type => type;
    private readonly Dictionary<ITypeSymbol, bool> _closedSealedTypes = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ITypeSymbol> _classTypesChecked = new(SymbolEqualityComparer.Default);

    internal IrTypeId GetTypeId(ITypeSymbol? type)
    {
        type = TypeSpecializer(type);
        // A tuple type is its underlying ValueTuple. Interning the tuple
        // symbol itself gives the same CLR type several identities, which
        // then fail exact type equality between a variable and its value.
        if (type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } tupleUnderlying })
        {
            type = tupleUnderlying;
        }
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
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
                _closedSealedTypes.Add(type, certified = CompilerIdentityBridge.IsClosedSealedReferenceType(type, cancellation.Token));
            }
            if (certified)
            { _factory.RegisterClosedSealedReferenceType(mapped); }
            if (_classTypesChecked.Add(type))
            {
                RegisterClassChain(type);
                if (type is INamedTypeSymbol { IsReferenceType: true } named)
                {
                    if (named.TypeKind == TypeKind.Interface)
                    { _factory.RegisterInterfaceReferenceType(mapped); }
                    if (named.AllInterfaces.Length <= 128)
                    {
                        foreach (var implemented in named.AllInterfaces)
                        { _factory.RegisterImplementedReferenceInterface(mapped, GetTypeId(implemented)); }
                    }
                }
            }
        }
        return mapped;
    }

    private void RegisterClassChain(ITypeSymbol type)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        if (!CompilerIdentityBridge.IsClosedReferenceType(type, cancellation.Token))
        { return; }
        var chain = new List<INamedTypeSymbol>();
        for (var current = (INamedTypeSymbol)type; current.SpecialType != SpecialType.System_Object; current = current.BaseType!)
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (chain.Count == 64 || current.BaseType == null || current.SpecialType != SpecialType.None)
            { return; }
            chain.Add(current);
        }
        IrTypeId? parent = null;
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var symbol = chain[index];
            var id = _factory.GetOrCreateReferenceType(CompilerIdentityBridge.InternType(_factory, symbol), CompilerIdentityBridge.CreateTypeDisplay(symbol));
            _factory.RegisterClosedClassReferenceType(id, parent);
            parent = id;
        }
    }

    internal bool IsSupportedValueDomain(ITypeSymbol? type)
    {
        return CompilerIdentityBridge.IsSupportedValueDomain(
            TypeSpecializer(type));
    }
}
