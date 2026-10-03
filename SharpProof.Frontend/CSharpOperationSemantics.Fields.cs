namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    // Stores project their scalar result and effects. Heap reads remain outside
    // this subset, so no subsequent value can depend on omitted heap state.
    internal static bool IsSupportedFieldWrite(IFieldSymbol field, IAssemblySymbol sourceAssembly)
    {
        return SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, sourceAssembly) &&
            IsScalar(field.Type) && field.ContainingType.IsReferenceType &&
            !field.IsVolatile && !field.IsReadOnly && !field.IsConst &&
            field.ContainingType.StaticConstructors.Length == 0;
    }

    // An instance field read runs no code; its only fault is a null receiver.
    // The heap is not modeled, so the value read is an approximation: it can
    // support universal proofs but never a concrete refutation.
    internal static bool IsSupportedFieldRead(IFieldSymbol field)
    {
        return !field.IsStatic && !field.IsVolatile && !field.HasConstantValue &&
            field.ContainingType.IsReferenceType && IsValueDomain(field.Type);
    }

    internal static TotalScalarRule FieldRead(IrFactory factory, IrTerm value, IrTerm? receiver)
    {
        return FieldWrite(factory, value, receiver);
    }

    // The field a nonvirtual instance property getter returns directly: an
    // auto-property's backing field, or a getter whose whole body returns one
    // field of the same instance.
    // `base.Property` calls the getter without virtual dispatch.
    internal static bool IsBaseAccess(IOperation? instance)
    {
        return instance is IInstanceReferenceOperation && instance.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax;
    }

    internal static IFieldSymbol? GetterField(IPropertySymbol property, bool nonVirtual = false)
    {
        if (property.IsStatic || property.IsIndexer || !nonVirtual && (property.IsVirtual || property.IsOverride) ||
            property.IsAbstract || property.GetMethod is not { } getter || !property.ContainingType.IsReferenceType)
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
        return field != null && SymbolEqualityComparer.Default.Equals(field.Type, property.Type) &&
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
