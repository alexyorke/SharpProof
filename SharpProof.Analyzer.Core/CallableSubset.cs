namespace SharpProof.Analyzer;

// The callables whose shape the verifier can represent at all: synchronous,
// non-generic, by-value signatures over managed types, outside unsafe code.
// Whether their bodies are supported is decided by lowering.
internal static class CallableSubset
{
    internal static bool IsSupported(IMethodSymbol method, SyntaxNode declaration)
    {
        return !method.IsAsync && method.TypeParameters.Length == 0 && !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
            !IsUnsupportedType(method.ReturnType) &&
            !method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None || IsUnsupportedType(parameter.Type)) &&
            !ContainsUnsafeSyntax(declaration) && declaration is not TypeDeclarationSyntax &&
            method.MethodKind is MethodKind.Ordinary or MethodKind.AnonymousFunction or MethodKind.Constructor or
                MethodKind.StaticConstructor or MethodKind.PropertyGet or MethodKind.PropertySet or
                MethodKind.ExplicitInterfaceImplementation;
    }

    private static bool IsUnsupportedType(ITypeSymbol? type)
    {
        return type?.TypeKind is TypeKind.Delegate or TypeKind.Dynamic or TypeKind.FunctionPointer or TypeKind.Pointer or
            TypeKind.TypeParameter ||
            type switch
            {
                IArrayTypeSymbol array => IsUnsupportedType(array.ElementType),
                INamedTypeSymbol named => named.IsRefLikeType || named.TypeArguments.Any(IsUnsupportedType),
                _ => false
            };
    }

    private static bool ContainsUnsafeSyntax(SyntaxNode declaration)
    {
        return declaration.DescendantNodesAndSelf().Any(static node => node is UnsafeStatementSyntax) ||
            declaration.AncestorsAndSelf().Any(static node => Modifiers(node).Any(static modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword)));
    }

    private static SyntaxTokenList Modifiers(SyntaxNode node)
    {
        return node switch
        {
            BaseMethodDeclarationSyntax method => method.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            AccessorDeclarationSyntax accessor => accessor.Modifiers,
            LocalFunctionStatementSyntax localFunction => localFunction.Modifiers,
            TypeDeclarationSyntax type => type.Modifiers,
            DelegateDeclarationSyntax @delegate => @delegate.Modifiers,
            _ => default
        };
    }
}
