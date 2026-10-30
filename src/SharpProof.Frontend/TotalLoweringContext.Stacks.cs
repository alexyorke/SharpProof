namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsStackStateDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return method != null && IsFrameworkCollectionMember(method) && !method.IsVirtual &&
            symbol.OriginalDefinition.GetDocumentationCommentId() is
                "M:System.Collections.Generic.Stack`1.#ctor" or "M:System.Collections.Generic.Stack`1.#ctor(System.Int32)" or
                "M:System.Collections.Generic.Stack`1.Push(`0)" or "M:System.Collections.Generic.Stack`1.Pop" or
                "P:System.Collections.Generic.Stack`1.Count";
    }

    internal static bool IsStackStateMember(IMethodSymbol method, ITypeSymbol? knownElement = null)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return IsStackStateDeclaration(method.AssociatedSymbol ?? method) && !method.IsStatic &&
            method.ContainingType is { TypeArguments.Length: 1 } type &&
            (method.MethodKind == MethodKind.Constructor || method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsValueDomain(knownElement ?? type.TypeArguments[0]));
    }
}

internal sealed partial class TotalLoweringContext
{
    internal static bool IsStackReceiverType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol { Name: "Stack", Arity: 1 } named &&
            named.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Stack`1" &&
            named.GetMembers().Any(CSharpOperationSemantics.IsStackStateDeclaration);
    }
}
