namespace SharpProof.Frontend;

internal static class CompilerMethodScopes
{
    internal static IEnumerable<ISymbol> Enumerate(IMethodSymbol method)
    {
        yield return method;
        if (method.AssociatedSymbol is IPropertySymbol property)
        {
            yield return property;
        }

        for (var type = method.ContainingType; type != null;
             type = type.ContainingType)
        {
            yield return type;
        }

        if (method.ContainingAssembly != null)
        {
            yield return method.ContainingAssembly;
        }
    }
}
