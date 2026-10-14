namespace SharpProof.CompilerArtifact;

// The scalar signature domain accepted by audited specification packs.
// This conversion does not resolve methods or admit catalog definitions.
internal static class CompilerSpecificationScalarDomain
{
    internal static IrTypeId MapType(IrFactory factory, ITypeSymbol type)
    {
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
        return expected switch
        {
            IrTypeKind.Boolean => type.SpecialType == SpecialType.System_Boolean,
            IrTypeKind.Integer => type.SpecialType is
                SpecialType.System_Int32 or SpecialType.System_Int64,
            _ => false
        };
    }
}
