namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsArrayCollectionDeclaration(IPropertySymbol property)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        if (property.IsStatic || property.ReturnsByRef || property.ReturnsByRefReadonly ||
            property.ContainingType.TypeKind != TypeKind.Interface || property.ContainingType.TypeArguments.Length != 1 ||
            property.GetMethod is not { } getter || !IsFrameworkCollectionMember(getter))
        { return false; }
        return property.OriginalDefinition.GetDocumentationCommentId() switch
        {
            "P:System.Collections.Generic.ICollection`1.Count" =>
                property.Parameters.IsEmpty && property.Type.SpecialType == SpecialType.System_Int32,
            "P:System.Collections.Generic.IList`1.Item(System.Int32)" =>
                property.IsIndexer && property.Parameters.Length == 1 &&
                property.Parameters[0].RefKind == RefKind.None && property.Parameters[0].Type.SpecialType == SpecialType.System_Int32 &&
                SymbolEqualityComparer.Default.Equals(property.Type, property.ContainingType.TypeArguments[0]),
            _ => false
        };
    }

    // This deliberately requires a visible array-to-interface conversion.
    // A statically typed List can be subclassed and reimplement its interfaces;
    // a scalar SZ array has one known implementation and cannot be covariant.
    // A matching `as` conversion preserves the same reference, including null.
    internal static IOperation? ArrayCollectionReceiver(IPropertyReferenceOperation property)
    {
        property = RepositoryParameterGuard.NotNull(property, nameof(property));
        if (!IsArrayCollectionDeclaration(property.Property))
        { return null; }
        var receiver = property.Instance;
        if (receiver is not IConversionOperation
            { OperatorMethod: null, Conversion: { Exists: true, IsReference: true } } conversion)
        { return null; }
        if (conversion.Type is not INamedTypeSymbol target ||
            target.OriginalDefinition.GetDocumentationCommentId() is not
                ("T:System.Collections.Generic.ICollection`1" or "T:System.Collections.Generic.IList`1") ||
            !SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, property.Property.ContainingAssembly))
        { return null; }
        var operand = conversion.Operand;
        return operand.Type is IArrayTypeSymbol { IsSZArray: true } array &&
            IsScalar(array.ElementType) && property.Property.ContainingType.TypeArguments.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(array.ElementType, property.Property.ContainingType.TypeArguments[0])
            ? operand : null;
    }

    internal static TotalScalarRule ArrayCollectionGuard(IrFactory factory, IrTerm receiver, IrTerm index)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        receiver = RepositoryParameterGuard.NotNull(receiver, nameof(receiver));
        index = RepositoryParameterGuard.NotNull(index, nameof(index));
        var guard = ElementGuard(factory, receiver, index, null);
        // The array interface differs from direct array indexing. Keep the exact
        // framework fault so catch dispatch and AllowedExceptions agree with .NET.
        return new(guard.Value, [guard.Throws[0], new(IrExceptionKind.ArgumentOutOfRange, guard.Throws[1].Condition)], guard.Classification);
    }
}
