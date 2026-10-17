using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Frontend;

internal static class ConditionalMethodIdentityPolicy
{
    internal static IMethodSymbol ResolveNativeDeclaration(
        Compilation compilation, IMethodSymbol declaration, INamedTypeSymbol underlying)
    {
        if (underlying is null)
        {
            throw new System.ArgumentNullException(nameof(underlying));
        }
        if (declaration is null)
        {
            throw new System.ArgumentNullException(nameof(declaration));
        }
        if (compilation is not CSharpCompilation csharp)
        { throw new InvalidOperationException("Native conditional method compilation is unavailable."); }
        return ResolveNativeDeclaration(csharp, declaration,
            underlying.GetMembers(declaration.Name).OfType<IMethodSymbol>().ToImmutableArray());
    }

    internal static IMethodSymbol ResolveNativeDeclaration(
        CSharpCompilation csharp, IMethodSymbol declaration, ImmutableArray<IMethodSymbol> candidates)
    {
        if (declaration is null)
        {
            throw new System.ArgumentNullException(nameof(declaration));
        }
        if (csharp is null)
        {
            throw new System.ArgumentNullException(nameof(csharp));
        }
        var matches = candidates.Where(Matches).ToArray();
        if (matches.Length != 1)
        { throw new InvalidOperationException("Native conditional method declaration is not unique."); }
        return matches[0].OriginalDefinition;

        bool Matches(IMethodSymbol candidate)
        {
            if (candidate.MethodKind != declaration.MethodKind || candidate.IsStatic != declaration.IsStatic ||
                candidate.Arity != declaration.Arity || candidate.IsVararg != declaration.IsVararg ||
                candidate.RefKind != declaration.RefKind || candidate.Parameters.Length != declaration.Parameters.Length ||
                !SymbolEqualityComparer.Default.Equals(candidate.ContainingModule, declaration.ContainingModule) ||
                !SameType(candidate.ReturnType, declaration.ReturnType) ||
                !SameModifiers(candidate.ReturnTypeCustomModifiers, declaration.ReturnTypeCustomModifiers) ||
                !SameModifiers(candidate.RefCustomModifiers, declaration.RefCustomModifiers))
            { return false; }
            for (var ordinal = 0; ordinal < candidate.Parameters.Length; ordinal++)
            {
                var actual = candidate.Parameters[ordinal];
                var wrapped = declaration.Parameters[ordinal];
                if (actual.RefKind != wrapped.RefKind || !SameType(actual.Type, wrapped.Type) ||
                    !SameModifiers(actual.CustomModifiers, wrapped.CustomModifiers) ||
                    !SameModifiers(actual.RefCustomModifiers, wrapped.RefCustomModifiers))
                { return false; }
            }
            return true;
        }

        bool SameType(ITypeSymbol left, ITypeSymbol right)
        {
            if (right is null)
            {
                throw new System.ArgumentNullException(nameof(right));
            }
            if (left is null)
            {
                throw new System.ArgumentNullException(nameof(left));
            }
            return csharp.ClassifyConversion(left, right).IsIdentity && csharp.ClassifyConversion(right, left).IsIdentity;
        }
    }

    internal static bool SameModifiers(ImmutableArray<CustomModifier> left, ImmutableArray<CustomModifier> right)
    {
        if (left.Length != right.Length)
        { return false; }
        for (var ordinal = 0; ordinal < left.Length; ordinal++)
        {
            if (left[ordinal].IsOptional != right[ordinal].IsOptional ||
                !SymbolEqualityComparer.Default.Equals(left[ordinal].Modifier, right[ordinal].Modifier))
            { return false; }
        }
        return true;
    }

    internal static MethodDefinitionHandle GetMethodDefinitionHandle(
        MetadataReader reader, int metadataToken)
    {
        var entity = MetadataTokens.Handle(metadataToken);
        if (entity.Kind != HandleKind.MethodDefinition)
        { throw new BadImageFormatException("Conditional method token is not a MethodDef."); }
        var handle = (MethodDefinitionHandle)entity;
        var row = MetadataTokens.GetRowNumber(handle);
        if (row <= 0 || row > reader.MethodDefinitions.Count)
        { throw new BadImageFormatException("Conditional method token is outside its module."); }
        return handle;
    }
}
