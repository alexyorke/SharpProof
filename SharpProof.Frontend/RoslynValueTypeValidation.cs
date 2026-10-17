namespace SharpProof.Frontend;

// Source admission and the IR type after specialization are distinct. Storage
// and literal values deliberately have different supported type domains.
internal static class RoslynValueTypeValidation
{
    internal static IrTypeId ResolveConstantType(
        RoslynTypeMapper mapper, ITypeSymbol? sourceType, ITypeSymbol? specializedSourceType)
    {
        if (sourceType?.TypeKind == TypeKind.Error ||
            !mapper.IsSupportedValueDomain(specializedSourceType))
        { throw new FrontendLoweringRejectionException(FrontendAbstention.UnsupportedType); }

        var type = mapper.GetTypeId(specializedSourceType);
        if (sourceType is { IsValueType: true, SpecialType: SpecialType.None })
        { throw new FrontendLoweringRejectionException(FrontendAbstention.UnsupportedType); }
        return type;
    }

    internal static IrTerm LocalStorageDefault(
        IrFactory factory, ITypeSymbol sourceType, IrVarId variable)
    {
        if (variable.IsDefault)
        { throw new System.ArgumentOutOfRangeException(nameof(variable)); }
        if (factory is null)
        {
            throw new System.ArgumentNullException(nameof(factory));
        }
        if (!CSharpOperationSemantics.IsValueDomain(sourceType))
        { throw new FrontendLoweringRejectionException(FrontendAbstention.UnsupportedType); }
        var type = factory.GetVariableInfo(variable).Type;
        return CSharpOperationSemantics.DefaultValue(factory, type);
    }
}
