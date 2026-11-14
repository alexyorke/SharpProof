using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    // Nominal field slots require an owner whose instance storage cannot
    // overlap. Source pseudoattributes and metadata flags are separate authority.
    internal static bool HasIndependentFieldStorage(IFieldSymbol field)
    {
        if (field.IsStatic)
        { return true; }
        var owner = field.OriginalDefinition.ContainingType.OriginalDefinition;
        if (owner.MetadataToken != 0)
        {
            try
            {
                var metadata = owner.ContainingModule.GetMetadata();
                var reader = metadata!.GetMetadataReader();
                var entity = MetadataTokens.Handle(owner.MetadataToken);
                var handle = (TypeDefinitionHandle)entity;
                return (reader.GetTypeDefinition(handle).Attributes & TypeAttributes.LayoutMask) is
                    TypeAttributes.AutoLayout or TypeAttributes.SequentialLayout;
            }
            catch (Exception exception) when (exception is
                BadImageFormatException or ArgumentException or ObjectDisposedException)
            { }
        }
        else if (owner.DeclaringSyntaxReferences.Length != 0)
        {
            foreach (var attribute in owner.GetAttributes())
            {
                if (attribute.AttributeClass is not { } attributeType || attributeType.TypeKind == TypeKind.Error)
                { return false; }
                if (attributeType.Name != "StructLayoutAttribute" || !HasLayoutQualifier(attributeType))
                { continue; }
                // The short overload and uncertain matching forms stay outside
                // this value model; assembly identity is not a pseudoattribute test.
                if (attributeType.ContainingType != null ||
                    attribute.AttributeConstructor is not { Parameters.Length: 1 } constructor ||
                    constructor.Parameters[0].RefKind != RefKind.None ||
                    constructor.Parameters[0].Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } kindType ||
                    kindType.ContainingType != null || kindType.MetadataName != "LayoutKind" || !HasLayoutQualifier(kindType) ||
                    kindType.EnumUnderlyingType?.SpecialType != SpecialType.System_Int32 ||
                    attribute.ConstructorArguments.Length != 1)
                { return false; }
                var argument = attribute.ConstructorArguments[0];
                if (argument.Kind != TypedConstantKind.Enum || !SymbolEqualityComparer.Default.Equals(argument.Type, kindType) ||
                    argument.Value is not int kind ||
                    kind != (int)System.Runtime.InteropServices.LayoutKind.Auto &&
                    kind != (int)System.Runtime.InteropServices.LayoutKind.Sequential)
                { return false; }
            }
            return true;
        }
        return false;
    }

    private static bool HasLayoutQualifier(INamedTypeSymbol type)
    {
        var interop = type.ContainingSymbol;
        var runtime = interop.ContainingSymbol;
        var system = runtime?.ContainingSymbol;
        return interop.Name == "InteropServices" && runtime?.Name == "Runtime" && system?.Name == "System" &&
            system.ContainingSymbol is INamespaceSymbol { IsGlobalNamespace: true };
    }

    internal static bool HasSynchronizedEntry(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return (method.MethodImplementationFlags & System.Reflection.MethodImplAttributes.Synchronized) != 0;
    }

    // Stores project their scalar result and effects. Heap reads remain outside
    // this subset, so no subsequent value can depend on omitted heap state.
    internal static bool IsSupportedFieldWrite(IFieldSymbol field, IAssemblySymbol sourceAssembly)
    {
        field = RepositoryParameterGuard.NotNull(field, nameof(field));
        sourceAssembly = RepositoryParameterGuard.NotNull(sourceAssembly, nameof(sourceAssembly));
        return HasIndependentFieldStorage(field) && SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, sourceAssembly) &&
            (IsScalar(field.Type) || IsReferenceDomain(field.Type)) && field.ContainingType.IsReferenceType &&
            !field.IsVolatile && !field.IsReadOnly && !field.IsConst &&
            // An instance store never runs type initialization; a compiler-
            // generated static constructor only runs static field initializers.
            (field.ContainingType.StaticConstructors.Length == 0 ||
                !field.IsStatic && field.ContainingType.StaticConstructors.All(constructor => constructor.IsImplicitlyDeclared));
    }

    // A source readonly struct whose instance fields hold scalars, references
    // or other such structs. Its value is modeled as a reference to an
    // immutable object, and null stands for default(S): the two cannot be told
    // apart, because every modeled read of a struct value is guarded and
    // returns the field's default for null, boxing yields an unconstrained
    // reference, and C# has no reference comparison of struct values.
    // Readonly fields make construction the only store.
    internal static bool IsModeledStruct(ITypeSymbol? type)
    { return IsModeledStruct(type, 0); }

    private static bool IsModeledStruct(ITypeSymbol? type, int depth)
    {
        if (depth > 8 || type is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Struct, IsReadOnly: true, IsRefLikeType: false, IsRecord: false, IsGenericType: false,
                SpecialType: SpecialType.None, IsUnmanagedType: var _
            } named ||
            named.DeclaringSyntaxReferences.IsEmpty || named.ContainingType is { IsGenericType: true } ||
            named.GetAttributes().Any(static attribute => attribute.AttributeClass is
            {
                Name: "StructLayoutAttribute", ContainingNamespace: { Name: "InteropServices" }
            }))
        { return false; }
        foreach (var member in named.GetMembers())
        {
            if (member is IFieldSymbol { IsStatic: false, IsConst: false } field &&
                (!HasIndependentFieldStorage(field) || field.IsVolatile || field.IsFixedSizeBuffer ||
                    !(IsScalar(field.Type) || IsReferenceDomain(field.Type) || IsModeledStruct(field.Type, depth + 1))))
            { return false; }
        }
        return true;
    }

    // C# lets only an instance constructor of the declaring type assign a
    // readonly instance field, and only through its own `this`: the object
    // under construction. Such a store is modeled like any other instance
    // store. A store through another instance of the type, or from any other
    // member, is not.
    internal static bool IsConstructorReadOnlyStore(IFieldSymbol field, IOperation? instance, IMethodSymbol target, IAssemblySymbol sourceAssembly)
    {
        field = RepositoryParameterGuard.NotNull(field, nameof(field));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        sourceAssembly = RepositoryParameterGuard.NotNull(sourceAssembly, nameof(sourceAssembly));
        return field is { IsReadOnly: true, IsStatic: false, IsVolatile: false, IsConst: false } &&
            target is { MethodKind: MethodKind.Constructor, IsStatic: false } &&
            SymbolEqualityComparer.Default.Equals(target.ContainingType.OriginalDefinition, field.ContainingType.OriginalDefinition) &&
            instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } &&
            HasIndependentFieldStorage(field) && SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, sourceAssembly) &&
            (IsScalar(field.Type) || IsReferenceDomain(field.Type) || IsModeledStruct(field.Type)) &&
            (field.ContainingType.IsReferenceType || IsModeledStruct(field.ContainingType));
    }

    // The backing field of a get-only auto-property, which a constructor of
    // its type assigns by assigning the property.
    internal static IFieldSymbol? GetOnlyAutoPropertyField(IPropertySymbol property)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        if (property.IsStatic || property.IsIndexer || property.IsVirtual || property.IsOverride || property.IsAbstract ||
            property.SetMethod != null || property.GetMethod is not { } getter ||
            getter.DeclaringSyntaxReferences.Length != 1 ||
            getter.DeclaringSyntaxReferences[0].GetSyntax() is not Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax { Body: null, ExpressionBody: null })
        { return null; }
        return property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
    }

    // An instance field read runs no code; its only fault is a null receiver.
    // A static field read runs no code when its type has no initializer.
    // A scalar or reference instance field of a class is modeled; any other
    // field read is an approximation.
    internal static bool IsSupportedFieldRead(IFieldSymbol field)
    {
        field = RepositoryParameterGuard.NotNull(field, nameof(field));
        return HasIndependentFieldStorage(field) && !field.IsVolatile && !field.HasConstantValue && IsValueDomain(field.Type) &&
            (field.IsStatic ? field.ContainingType.StaticConstructors.Length == 0
                : field.ContainingType.IsReferenceType || IsModeledStruct(field.ContainingType));
    }

    internal static TotalScalarRule FieldRead(IrFactory factory, IrTerm value, IrTerm? receiver)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return FieldWrite(factory, value, receiver);
    }

    // The field a nonvirtual property getter returns directly: an
    // auto-property's backing field, or a getter whose whole body returns one
    // field of the same instance or type.
    // `base.Property` calls the getter without virtual dispatch.
    internal static bool IsBaseAccess(IOperation? instance)
    {
        return instance is IInstanceReferenceOperation && instance.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax;
    }

    // The backing field a nonvirtual auto-property's setter stores.
    internal static IFieldSymbol? SetterField(IPropertySymbol property, bool readsValue = false)
    {
        if (property.IsIndexer || property.IsVirtual || property.IsOverride || property.IsAbstract ||
            property.SetMethod is not { IsInitOnly: false } setter || HasSynchronizedEntry(setter) ||
            readsValue && property.GetMethod is { } getter && HasSynchronizedEntry(getter) ||
            !property.IsStatic && !property.ContainingType.IsReferenceType ||
            setter.DeclaringSyntaxReferences.Length != 1 ||
            setter.DeclaringSyntaxReferences[0].GetSyntax() is not Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax { Body: null, ExpressionBody: null })
        { return null; }
        return property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
    }

    internal static IFieldSymbol? GetterField(IPropertySymbol property, bool nonVirtual = false)
    {
        if (property.IsIndexer || !nonVirtual && (property.IsVirtual || property.IsOverride) ||
            property.IsAbstract || property.GetMethod is not { } getter || HasSynchronizedEntry(getter) ||
            !property.IsStatic && !property.ContainingType.IsReferenceType && !IsModeledStruct(property.ContainingType))
        { return null; }
        var backing = property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
        if (backing != null)
        { return IsSupportedFieldRead(backing) ? backing : null; }
        if (getter.DeclaringSyntaxReferences.Length != 1)
        { return null; }
        var expression = getter.DeclaringSyntaxReferences[0].GetSyntax() switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax { ExpressionBody: { } arrow } => arrow.Expression,
            Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax { Body.Statements: { Count: 1 } statements } =>
                (statements[0] as Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax)?.Expression,
            Microsoft.CodeAnalysis.CSharp.Syntax.ArrowExpressionClauseSyntax arrow => arrow.Expression,
            _ => null
        };
        while (expression is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax parenthesized)
        { expression = parenthesized.Expression; }
        var name = expression switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax
            {
                Expression: Microsoft.CodeAnalysis.CSharp.Syntax.ThisExpressionSyntax,
                Name: Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax member
            } => member.Identifier.ValueText,
            _ => null
        };
        var field = name == null ? null : property.ContainingType.GetMembers(name).OfType<IFieldSymbol>().SingleOrDefault();
        return field != null && field.IsStatic == property.IsStatic && SymbolEqualityComparer.Default.Equals(field.Type, property.Type) &&
            IsSupportedFieldRead(field) ? field : null;
    }

    internal static TotalScalarRule FieldWrite(IrFactory factory, IrTerm value, IrTerm? receiver)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return receiver == null ? Exact(value) : new(value,
            [new(IrExceptionKind.NullReference,
                factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type)))],
            FrontendSubsetClassification.Exact);
    }
}
