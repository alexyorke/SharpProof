using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
#if SHARPPROOF_EDITOR_SUGGESTIONS
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
#endif

#if SHARPPROOF_EDITOR_SUGGESTIONS
namespace SharpProof.EditorSuggestions;
#else
namespace SharpProof.CodeFixes;
#endif

// This compiler-only boundary is shared by analyzer admission and editor
// scaffolding. Keep Workspace types and editor services out of this class.
internal static class CompanionEligibility
{
    internal static bool IsSupported(INamedTypeSymbol target)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(target, nameof(target));
        if (target.ContainingType != null || target.TypeKind is not (TypeKind.Interface or TypeKind.Class) ||
            target.IsUnboundGenericType || target.IsFileLocal || target.TypeParameters.Any(parameter =>
                (parameter ?? throw new ArgumentNullException(nameof(parameter))).Name == target.Name + "Contracts"))
        { return false; }
        var requirePublic = target.DeclaredAccessibility == Accessibility.Public;
        if (!SupportedConstraints(target.TypeParameters, requirePublic))
        { return false; }
        foreach (var method in target.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary || method.IsImplicitlyDeclared)
            { continue; }
            if (method.IsVararg || method.Name == target.Name + "Contracts" ||
                method.TypeParameters.Any(parameter => (parameter ?? throw new ArgumentNullException(nameof(parameter))).AllowsRefLikeType ||
                    target.TypeParameters.Any(outer => (outer ?? throw new ArgumentNullException(nameof(outer))).Name == parameter.Name)) ||
                !SupportedConstraints(method.TypeParameters, requirePublic) || !CanExpose(method.ReturnType, requirePublic))
            { return false; }
            foreach (var parameter in method.Parameters)
            {
                if (!CanExpose(parameter.Type, requirePublic) || parameter.IsOptional != parameter.HasExplicitDefaultValue ||
                    parameter.GetAttributes().Any(attribute =>
                        (attribute ?? throw new ArgumentNullException(nameof(attribute))).AttributeClass is
                        {
                            MetadataName: "UnscopedRefAttribute", ContainingType: null,
                            ContainingNamespace:
                            {
                                Name: "CodeAnalysis", ContainingNamespace:
                                { Name: "Diagnostics", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
                            }
                        }))
                { return false; }
            }
        }
        var overloadGroups = target.GetMembers().OfType<IMethodSymbol>().Where(static method =>
            (method ?? throw new ArgumentNullException(nameof(method))) is { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false })
            .GroupBy(static method => (method ?? throw new ArgumentNullException(nameof(method))).Name, StringComparer.Ordinal);
        foreach (var group in overloadGroups)
        {
            var methods = group.ToArray();
            for (var first = 0; first < methods.Length; first++)
            {
                for (var second = first + 1; second < methods.Length; second++)
                {
                    if (ProjectedSignaturesMatch(methods[first], methods[second]))
                    { return false; }
                }
            }
        }
        return true;
    }

#if !SHARPPROOF_EDITOR_SUGGESTIONS
    internal static INamedTypeSymbol? ContractTarget(INamedTypeSymbol companion, Compilation compilation)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(companion, nameof(companion));
        global::SharpProof.Internal.CodeFixInputValidation.Require(compilation, nameof(compilation));
        var contractFor = compilation.GetTypeByMetadataName("SharpProof.Attributes.ContractForAttribute");
        if (contractFor?.ContainingAssembly.Identity.Name != "SharpProof.Attributes")
        { return null; }
        var attributes = companion.GetAttributes().Where(attribute =>
            SymbolEqualityComparer.Default.Equals((attribute ?? throw new ArgumentNullException(nameof(attribute))).AttributeClass, contractFor)).ToArray();
        return attributes.Length == 1 && attributes[0].ConstructorArguments.Length == 1
            ? attributes[0].ConstructorArguments[0].Value as INamedTypeSymbol : null;
    }

    internal static ImmutableArray<INamedTypeSymbol> FindCompanions(INamedTypeSymbol target, Compilation compilation, CancellationToken cancellationToken)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(target, nameof(target));
        global::SharpProof.Internal.CodeFixInputValidation.Require(compilation, nameof(compilation));
        var companions = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var contractFor = compilation.GetTypeByMetadataName("SharpProof.Attributes.ContractForAttribute");
        if (contractFor?.ContainingAssembly.Identity.Name != "SharpProof.Attributes")
        { return companions.ToImmutable(); }
        foreach (var type in SourceTypes(compilation, cancellationToken))
        {
            if (type.GetAttributes().Any(attribute =>
                SymbolEqualityComparer.Default.Equals((attribute ?? throw new ArgumentNullException(nameof(attribute))).AttributeClass, contractFor) &&
                attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol contractTarget &&
                SymbolEqualityComparer.Default.Equals(contractTarget.OriginalDefinition, target.OriginalDefinition)))
            { companions.Add(type); }
        }
        return companions.ToImmutable();
    }
#else
    internal static ImmutableHashSet<INamedTypeSymbol> ExistingTargets(Compilation compilation, CancellationToken cancellationToken)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(compilation, nameof(compilation));
        var contractFor = compilation.GetTypeByMetadataName("SharpProof.Attributes.ContractForAttribute");
        var targets = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in SourceTypes(compilation, cancellationToken))
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, contractFor) && attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is INamedTypeSymbol target)
                { targets.Add(target.OriginalDefinition); }
            }
        }
        return targets.ToImmutable();
    }

    internal static bool HasNullableContext(INamedTypeSymbol target, SemanticModel model, TypeDeclarationSyntax declaration)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(target, nameof(target));
        global::SharpProof.Internal.CodeFixInputValidation.Require(model, nameof(model));
        global::SharpProof.Internal.CodeFixInputValidation.Require(declaration, nameof(declaration));
        var annotationsEnabled = (model.GetNullableContext(declaration.SpanStart) & NullableContext.AnnotationsEnabled) != 0;
        foreach (var reference in target.DeclaringSyntaxReferences.Concat(target.GetMembers().OfType<IMethodSymbol>()
            .SelectMany(static method => (method ?? throw new ArgumentNullException(nameof(method))).DeclaringSyntaxReferences)))
        {
            // A syntax action has one compiler-supplied semantic model. Other
            // files remain available through the existing explicit refactoring.
            if (reference.SyntaxTree != model.SyntaxTree ||
                ((model.GetNullableContext(reference.Span.Start) & NullableContext.AnnotationsEnabled) != 0) != annotationsEnabled)
            { return false; }
        }
        return true;
    }
#endif

    private static IEnumerable<INamedTypeSymbol> SourceTypes(Compilation compilation, CancellationToken cancellationToken)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(compilation, nameof(compilation));
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var member in current.GetMembers().OfType<INamespaceOrTypeSymbol>())
            { pending.Push(member); }
            if (current is INamedTypeSymbol type)
            { yield return type; }
        }
    }

    private static bool SupportedConstraints(ImmutableArray<ITypeParameterSymbol> parameters, bool requirePublic)
    {
        return parameters.All(parameter => !(parameter ?? throw new ArgumentNullException(nameof(parameter))).AllowsRefLikeType &&
            parameter.ConstraintTypes.All(type => CanExpose(type ?? throw new ArgumentNullException(nameof(type)), requirePublic)));
    }

    private static bool CanExpose(ITypeSymbol type, bool requirePublic)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(type, nameof(type));
        if (type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Error)
        { return false; }
        if (type is IArrayTypeSymbol array)
        { return CanExpose(array.ElementType, requirePublic); }
        if (type is not INamedTypeSymbol named)
        { return true; }
        for (var current = named; current != null; current = current.ContainingType)
        {
            if (current.IsFileLocal || current.DeclaredAccessibility != Accessibility.Public &&
                (requirePublic || current.DeclaredAccessibility is not (Accessibility.Internal or Accessibility.ProtectedOrInternal)))
            { return false; }
            foreach (var argument in current.TypeArguments)
            {
                if (!CanExpose(argument, requirePublic))
                { return false; }
            }
        }
        return true;
    }

    private static bool ProjectedSignaturesMatch(IMethodSymbol left, IMethodSymbol right)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(left, nameof(left));
        global::SharpProof.Internal.CodeFixInputValidation.Require(right, nameof(right));
        var leftOffset = left.IsStatic ? 0 : 1;
        var rightOffset = right.IsStatic ? 0 : 1;
        var count = left.Parameters.Length + leftOffset;
        if (left.Name != right.Name || left.Arity != right.Arity || count != right.Parameters.Length + rightOffset)
        { return false; }
        for (var index = 0; index < count; index++)
        {
            var leftParameter = index < leftOffset ? null : left.Parameters[index - leftOffset];
            var rightParameter = index < rightOffset ? null : right.Parameters[index - rightOffset];
            var leftType = leftParameter?.Type ?? left.ContainingType;
            var rightType = rightParameter?.Type ?? right.ContainingType;
            if ((leftParameter is { RefKind: not RefKind.None }) != (rightParameter is { RefKind: not RefKind.None }) ||
                !RuntimeTypesMatch(leftType, rightType))
            { return false; }
        }
        return true;
    }

    private static bool RuntimeTypesMatch(ITypeSymbol left, ITypeSymbol right)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(left, nameof(left));
        global::SharpProof.Internal.CodeFixInputValidation.Require(right, nameof(right));
        if (left is ITypeParameterSymbol leftParameter && right is ITypeParameterSymbol rightParameter &&
            leftParameter.TypeParameterKind == TypeParameterKind.Method && rightParameter.TypeParameterKind == TypeParameterKind.Method)
        { return leftParameter.Ordinal == rightParameter.Ordinal; }
        if ((left.TypeKind == TypeKind.Dynamic || left.SpecialType == SpecialType.System_Object) &&
            (right.TypeKind == TypeKind.Dynamic || right.SpecialType == SpecialType.System_Object))
        { return true; }
        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        { return leftArray.Rank == rightArray.Rank && leftArray.IsSZArray == rightArray.IsSZArray && RuntimeTypesMatch(leftArray.ElementType, rightArray.ElementType); }
        if (left is INamedTypeSymbol leftNamed && right is INamedTypeSymbol rightNamed)
        {
            if (!SymbolEqualityComparer.Default.Equals(leftNamed.OriginalDefinition, rightNamed.OriginalDefinition) ||
                leftNamed.TypeArguments.Length != rightNamed.TypeArguments.Length)
            { return false; }
            for (var index = 0; index < leftNamed.TypeArguments.Length; index++)
            {
                if (!RuntimeTypesMatch(leftNamed.TypeArguments[index], rightNamed.TypeArguments[index]))
                { return false; }
            }
            return leftNamed.ContainingType == null && rightNamed.ContainingType == null ||
                leftNamed.ContainingType != null && rightNamed.ContainingType != null && RuntimeTypesMatch(leftNamed.ContainingType, rightNamed.ContainingType);
        }
        return SymbolEqualityComparer.Default.Equals(left, right);
    }
}
