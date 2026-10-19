namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsIntegralConvertToInt64(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return method is { IsStatic: true, Name: "ToInt64", Arity: 0, Parameters.Length: 1 } &&
            method.ContainingType.GetDocumentationCommentId() == "T:System.Convert" &&
            IsFrameworkCollectionMember(method) && method.ReturnType.SpecialType == SpecialType.System_Int64 &&
            method.Parameters[0].RefKind == RefKind.None &&
            (method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean ||
                IsComparisonIntegerType(method.Parameters[0].Type));
    }

    internal static TotalScalarRule IntegralConvertToInt64(IrFactory factory, IrTerm value)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        var target = factory.GetOrCreateIntegerType(64, true);
        return factory.GetTypeInfo(value.Type).Kind == IrTypeKind.Boolean
            ? Exact(factory.Conditional(value, factory.Integer(target, 1), factory.Integer(target, 0)))
            : ConvertInteger(factory, value, target, true);
    }
}
