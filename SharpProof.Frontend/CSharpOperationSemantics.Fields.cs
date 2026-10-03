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

    internal static TotalScalarRule FieldWrite(IrFactory factory, IrTerm value, IrTerm? receiver)
    {
        return receiver == null ? Exact(value) : new(value,
            [new(IrExceptionKind.NullReference,
                factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type)))],
            FrontendSubsetClassification.Exact);
    }
}
