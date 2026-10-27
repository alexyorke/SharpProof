namespace SharpProof.CompilerCollector;

// The scalar signature domain accepted by audited specification packs.
// This conversion does not resolve methods or admit catalog definitions.
internal static class CompilerSpecificationScalarDomain
{
    internal static bool TryMapSignature(IrFactory factory, IMethodSymbol method,
        out ImmutableArray<IrTypeId> parameters, out IrTypeId resultType, out IrTypeId integerType)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        parameters = method.Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (MapType(factory, parameter.Type))).ToImmutableArray();
        resultType = MapType(factory, method.ReturnType);
        integerType = default;
        if (resultType.IsDefault || parameters.Any(type => type.IsDefault))
        { return false; }
        var integerTypes = parameters.Add(resultType).Where(type => type.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(type)) : (factory.GetTypeInfo(type).Kind == IrTypeKind.Integer)).Distinct().ToImmutableArray();
        if (integerTypes.Length > 1)
        { return false; }
        integerType = integerTypes.Length == 0 ? factory.IntegerType : integerTypes[0];
        return true;
    }

    internal static IrTypeId MapType(IrFactory factory, ITypeSymbol type)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.SpecialType switch
        {
            SpecialType.System_Boolean => factory.BooleanType,
            SpecialType.System_Int32 => factory.GetOrCreateIntegerType(32, true),
            SpecialType.System_Int64 => factory.GetOrCreateIntegerType(64, true),
            _ => default
        };
    }

    internal static bool MatchesType(ITypeSymbol type, IrTypeKind expected)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return expected switch
        {
            IrTypeKind.Boolean => type.SpecialType == SpecialType.System_Boolean,
            IrTypeKind.Integer => type.SpecialType is
                SpecialType.System_Int32 or SpecialType.System_Int64,
            _ => false
        };
    }
}
