using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace SharpProof.CodeFixes;

internal static class CompanionScaffolding
{
    internal static bool IsSupported(INamedTypeSymbol target)
    {
        if (target == null)
        { throw new ArgumentNullException(nameof(target)); }
        if (target.ContainingType != null || target.TypeKind is not (TypeKind.Interface or TypeKind.Class) ||
            target.IsUnboundGenericType || target.IsFileLocal || target.TypeParameters.Any(parameter =>
                (parameter ?? throw new ArgumentNullException(nameof(parameter))).Name == target.Name + "Contracts"))
        {
            return false;
        }
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
        return true;
    }

    internal static MethodDeclarationSyntax Method(IMethodSymbol target)
    {
        if (target == null)
        { throw new ArgumentNullException(nameof(target)); }
        using var workspace = new AdhocWorkspace();
        var generator = SyntaxGenerator.GetGenerator(workspace, LanguageNames.CSharp);
        var method = (MethodDeclarationSyntax)generator.Declaration(target);
        method = method.WithConstraintClauses(PreserveNullableConstraints(method.ConstraintClauses, target.TypeParameters));
        // Extension syntax is unnecessary for a companion and can change overload
        // resolution for existing calls when the new file joins the compilation.
        var parameters = new List<ParameterSyntax>();
        for (var index = 0; index < method.ParameterList.Parameters.Count; index++)
        {
            parameters.Add(method.ParameterList.Parameters[index].WithModifiers(ParameterModifiers(target.Parameters[index])));
        }
        method = method.WithParameterList(method.ParameterList.WithParameters(SyntaxFactory.SeparatedList(parameters)));
        var returnType = (TypeSyntax)generator.TypeExpression(target.ReturnType);
        method = method.WithReturnType(target.ReturnsByRef || target.ReturnsByRefReadonly
            ? SyntaxFactory.RefType(returnType).WithReadOnlyKeyword(target.ReturnsByRefReadonly ? SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword) : default)
            : returnType);
        method = method.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithExplicitInterfaceSpecifier(null).WithExpressionBody(null).WithSemicolonToken(default)
            .WithBody(SyntaxFactory.Block(SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(
                SyntaxFactory.ParseName("global::System.NotImplementedException")).WithArgumentList(SyntaxFactory.ArgumentList()))));
        if (!target.IsStatic)
        {
            var name = "receiver";
            while (target.Parameters.Any(parameter => (parameter ?? throw new ArgumentNullException(nameof(parameter))).Name == name) ||
                target.TypeParameters.Any(parameter => (parameter ?? throw new ArgumentNullException(nameof(parameter))).Name == name))
            { name = "_" + name; }
            var receiver = SyntaxFactory.Parameter(SyntaxFactory.Identifier(name))
                .WithType((TypeSyntax)generator.TypeExpression(target.ContainingType));
            method = method.WithParameterList(method.ParameterList.WithParameters(method.ParameterList.Parameters.Insert(0, receiver)));
        }
        return method.WithAdditionalAnnotations(Formatter.Annotation);
    }

    internal static ClassDeclarationSyntax Companion(INamedTypeSymbol target)
    {
        if (target == null)
        { throw new ArgumentNullException(nameof(target)); }
        using var workspace = new AdhocWorkspace();
        var generator = SyntaxGenerator.GetGenerator(workspace, LanguageNames.CSharp);
        var original = (TypeDeclarationSyntax)generator.Declaration(target);
        var parameters = original.TypeParameterList;
        if (parameters != null)
        {
            var builder = new List<TypeParameterSyntax>();
            foreach (var parameter in parameters.Parameters)
            { builder.Add(parameter.WithVarianceKeyword(default)); }
            parameters = parameters.WithParameters(SyntaxFactory.SeparatedList(builder));
        }
        var declaration = SyntaxFactory.ClassDeclaration(target.Name + "Contracts")
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(target.DeclaredAccessibility == Accessibility.Public ? SyntaxKind.PublicKeyword : SyntaxKind.InternalKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithTypeParameterList(parameters).WithConstraintClauses(PreserveNullableConstraints(original.ConstraintClauses, target.TypeParameters));
        var attributeTarget = target.IsGenericType ? target.ConstructUnboundGenericType() : target;
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::SharpProof.Attributes.ContractFor"))
            .WithArgumentList(SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.AttributeArgument(SyntaxFactory.TypeOfExpression((TypeSyntax)generator.TypeExpression(attributeTarget))))));
        declaration = declaration.AddAttributeLists(SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute)));
        foreach (var method in target.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind == MethodKind.Ordinary && !method.IsImplicitlyDeclared)
            { declaration = declaration.AddMembers(Method(method)); }
        }
        return declaration.WithAdditionalAnnotations(Formatter.Annotation);
    }

    internal static ImmutableArray<IMethodSymbol> MissingByName(INamedTypeSymbol companion, INamedTypeSymbol target)
    {
        if (companion == null)
        { throw new ArgumentNullException(nameof(companion)); }
        if (target == null)
        { throw new ArgumentNullException(nameof(target)); }
        var missing = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var method in target.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind == MethodKind.Ordinary && !method.IsImplicitlyDeclared && companion.GetMembers(method.Name).Length == 0 &&
                method.Name != companion.Name)
            { missing.Add(method); }
        }
        return missing.ToImmutable();
    }

    internal static string NamespaceName(INamespaceSymbol symbol)
    {
        if (symbol == null)
        { throw new ArgumentNullException(nameof(symbol)); }
        var parts = new Stack<string>();
        for (var current = symbol; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        { parts.Push("@" + current.Name); }
        return string.Join(".", parts);
    }

    internal static async Task<bool> HasNullableContextAsync(INamedTypeSymbol target, Project project, bool annotationsEnabled, CancellationToken cancellationToken)
    {
        if (target == null)
        { throw new ArgumentNullException(nameof(target)); }
        if (project == null)
        { throw new ArgumentNullException(nameof(project)); }
        foreach (var reference in target.DeclaringSyntaxReferences.Concat(target.GetMembers().OfType<IMethodSymbol>()
            .SelectMany(static method => (method ?? throw new ArgumentNullException(nameof(method))).DeclaringSyntaxReferences)))
        {
            var document = project.Solution.GetDocument(reference.SyntaxTree);
            if (document == null)
            { return false; }
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model == null || ((model.GetNullableContext(reference.Span.Start) & NullableContext.AnnotationsEnabled) != 0) != annotationsEnabled)
            { return false; }
        }
        return true;
    }

    internal static INamedTypeSymbol? ContractTarget(INamedTypeSymbol companion, Compilation compilation)
    {
        if (companion == null)
        { throw new ArgumentNullException(nameof(companion)); }
        if (compilation == null)
        { throw new ArgumentNullException(nameof(compilation)); }
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
        if (target == null)
        { throw new ArgumentNullException(nameof(target)); }
        if (compilation == null)
        { throw new ArgumentNullException(nameof(compilation)); }
        var companions = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var contractFor = compilation.GetTypeByMetadataName("SharpProof.Attributes.ContractForAttribute");
        if (contractFor?.ContainingAssembly.Identity.Name != "SharpProof.Attributes")
        { return companions.ToImmutable(); }
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var member in current.GetMembers().OfType<INamespaceOrTypeSymbol>())
            { pending.Push(member); }
            if (current is INamedTypeSymbol type && type.GetAttributes().Any(attribute =>
                SymbolEqualityComparer.Default.Equals((attribute ?? throw new ArgumentNullException(nameof(attribute))).AttributeClass, contractFor) &&
                attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is INamedTypeSymbol contractTarget &&
                SymbolEqualityComparer.Default.Equals(contractTarget.OriginalDefinition, target.OriginalDefinition)))
            { companions.Add(type); }
        }
        return companions.ToImmutable();
    }

    private static SyntaxList<TypeParameterConstraintClauseSyntax> PreserveNullableConstraints(
        SyntaxList<TypeParameterConstraintClauseSyntax> clauses, ImmutableArray<ITypeParameterSymbol> parameters)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(clauses, nameof(clauses));
        global::SharpProof.Internal.CodeFixInputValidation.Require(parameters.IsDefault ? null : (object)parameters, nameof(parameters));
        foreach (var parameter in parameters)
        {
            global::SharpProof.Internal.CodeFixInputValidation.Require(parameter, nameof(parameters));
            if (!parameter.HasReferenceTypeConstraint || parameter.ReferenceTypeConstraintNullableAnnotation != NullableAnnotation.Annotated)
            { continue; }
            for (var index = 0; index < clauses.Count; index++)
            {
                var clause = clauses[index];
                if (clause.Name.Identifier.ValueText != parameter.Name)
                { continue; }
                var constraints = clause.Constraints;
                foreach (var constraint in clause.Constraints.OfType<ClassOrStructConstraintSyntax>())
                {
                    if (constraint.ClassOrStructKeyword.IsKind(SyntaxKind.ClassKeyword))
                    {
                        constraints = constraints.Replace(constraint, constraint.WithQuestionToken(SyntaxFactory.Token(SyntaxKind.QuestionToken)));
                    }
                }
                clauses = clauses.Replace(clause, clause.WithConstraints(constraints));
            }
        }
        return clauses;
    }

    private static bool SupportedConstraints(ImmutableArray<ITypeParameterSymbol> parameters, bool requirePublic)
    {
        return parameters.All(parameter => !(parameter ?? throw new ArgumentNullException(nameof(parameter))).AllowsRefLikeType &&
            parameter.ConstraintTypes.All(type => CanExpose(type ?? throw new ArgumentNullException(nameof(type)), requirePublic)));
    }

    private static SyntaxTokenList ParameterModifiers(IParameterSymbol parameter)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(parameter, nameof(parameter));
        var modifiers = new List<SyntaxToken>();
        if (parameter.IsParams)
        { modifiers.Add(SyntaxFactory.Token(SyntaxKind.ParamsKeyword)); }
        if (parameter.ScopedKind != ScopedKind.None && parameter.RefKind != RefKind.Out)
        { modifiers.Add(SyntaxFactory.Token(SyntaxKind.ScopedKeyword)); }
        switch (parameter.RefKind)
        {
            case RefKind.Ref:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.RefKeyword));
                break;
            case RefKind.Out:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.OutKeyword));
                break;
            case RefKind.In:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.InKeyword));
                break;
            case RefKind.RefReadOnlyParameter:
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.RefKeyword));
                modifiers.Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));
                break;
            default:
                break;
        }
        return SyntaxFactory.TokenList(modifiers);
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
        }
        return named.TypeArguments.All(argument => CanExpose(argument ?? throw new ArgumentNullException(nameof(argument)), requirePublic));
    }
}
