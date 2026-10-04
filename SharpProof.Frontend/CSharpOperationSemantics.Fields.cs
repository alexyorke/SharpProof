namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    // Stores project their scalar result and effects. Heap reads remain outside
    // this subset, so no subsequent value can depend on omitted heap state.
    internal static bool IsSupportedFieldWrite(IFieldSymbol field, IAssemblySymbol sourceAssembly)
    {
        return SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, sourceAssembly) &&
            (IsScalar(field.Type) || IsReferenceDomain(field.Type)) && field.ContainingType.IsReferenceType &&
            !field.IsVolatile && !field.IsReadOnly && !field.IsConst &&
            // An instance store never runs type initialization; a compiler-
            // generated static constructor only runs static field initializers.
            (field.ContainingType.StaticConstructors.Length == 0 ||
                !field.IsStatic && field.ContainingType.StaticConstructors.All(constructor => constructor.IsImplicitlyDeclared));
    }

    // An instance field read runs no code; its only fault is a null receiver.
    // A static field read runs no code when its type has no initializer.
    // A scalar or reference instance field of a class is modeled; any other
    // field read is an approximation.
    internal static bool IsSupportedFieldRead(IFieldSymbol field)
    {
        return !field.IsVolatile && !field.HasConstantValue && IsValueDomain(field.Type) &&
            (field.IsStatic ? field.ContainingType.StaticConstructors.Length == 0 : field.ContainingType.IsReferenceType);
    }

    internal static TotalScalarRule FieldRead(IrFactory factory, IrTerm value, IrTerm? receiver)
    {
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
    internal static IFieldSymbol? SetterField(IPropertySymbol property)
    {
        if (property.IsIndexer || property.IsVirtual || property.IsOverride || property.IsAbstract ||
            property.SetMethod is not { IsInitOnly: false } setter || !property.IsStatic && !property.ContainingType.IsReferenceType ||
            setter.DeclaringSyntaxReferences.Length != 1 ||
            setter.DeclaringSyntaxReferences[0].GetSyntax() is not Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax { Body: null, ExpressionBody: null })
        { return null; }
        return property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
    }

    internal static IFieldSymbol? GetterField(IPropertySymbol property, bool nonVirtual = false)
    {
        if (property.IsIndexer || !nonVirtual && (property.IsVirtual || property.IsOverride) ||
            property.IsAbstract || property.GetMethod is not { } getter || !property.IsStatic && !property.ContainingType.IsReferenceType)
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
        return receiver == null ? Exact(value) : new(value,
            [new(IrExceptionKind.NullReference,
                factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type)))],
            FrontendSubsetClassification.Exact);
    }
}
