namespace SharpProof.Compiler.Semantics;

// The callables whose shape the verifier can represent at all: synchronous
// unless the caller admits async roots, non-generic, by-value signatures over managed types, outside unsafe code.
// Whether their bodies are supported is decided by lowering.
internal static class CallableSubset
{
    // With allowOutParameter, one out parameter of a bool, integer or enum
    // type is admitted: its postconditions can be verified, but writes to it
    // are writes to the caller's storage, which effect claims do not model.
    // With allowAsync, an async method the lowering admits as a root
    // (TotalLoweringContext.IsAdmittedAsync) is supported; whether its body
    // never awaits is decided by lowering.
    internal static bool IsSupported(IMethodSymbol method, SyntaxNode declaration, bool allowOutParameter = false, bool allowAsync = false)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        declaration = ArgumentNullGuard.NotNull(declaration, nameof(declaration));
        var outParameters = allowOutParameter && HasOneScalarOutParameter(method) ? 1 : 0;
        return (!method.IsAsync || allowAsync && TotalLoweringContext.IsAdmittedAsync(method)) && method.TypeParameters.Length == 0 && !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
            !IsUnsupportedType(method.ReturnType) &&
            method.Parameters.Count(static parameter => parameter.RefKind != RefKind.None) == outParameters &&
            !method.Parameters.Any(static parameter => IsUnsupportedType(parameter.Type)) &&
            !ContainsUnsafeSyntax(declaration) && declaration is not TypeDeclarationSyntax &&
            method.MethodKind is MethodKind.Ordinary or MethodKind.AnonymousFunction or MethodKind.Constructor or
                MethodKind.StaticConstructor or MethodKind.PropertyGet or MethodKind.PropertySet or
                MethodKind.ExplicitInterfaceImplementation;
    }

    internal static bool HasOneScalarOutParameter(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        var references = method.Parameters.Where(static parameter => parameter.RefKind != RefKind.None).ToImmutableArray();
        return references.Length == 1 && references[0].RefKind == RefKind.Out && IsScalar(references[0].Type);
    }

    private static bool IsScalar(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Enum || type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Char or
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

    private static bool IsUnsupportedType(ITypeSymbol type)
    {
        if (type.TypeKind is TypeKind.Dynamic or TypeKind.FunctionPointer or TypeKind.Pointer or
            TypeKind.TypeParameter)
        { return true; }
        if (type is IArrayTypeSymbol array)
        { return IsUnsupportedType(array.ElementType); }
        // Non-null compiler types surviving the kind filter are arrays or named types.
        var named = (INamedTypeSymbol)type;
        return named.IsRefLikeType || named.TypeArguments.Any(IsUnsupportedType);
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
