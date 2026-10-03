namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    // An element access on a single-dimensional array faults on a null array,
    // then on an index outside it. A multidimensional array's bounds are not
    // modeled; `outside` is then an approximated flag.
    internal static TotalScalarRule ElementGuard(IrFactory factory, IrTerm receiver, IrTerm? index, IrTerm? outside)
    {
        var isNull = factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type));
        if (index == null)
        {
            return new(factory.Boolean(true), [new(IrExceptionKind.NullReference, isNull),
                new(IrExceptionKind.IndexOutOfRange, outside!)], FrontendSubsetClassification.Exact);
        }
        var info = factory.GetTypeInfo(index.Type);
        if (info.Kind != IrTypeKind.Integer || info.Width is not (8 or 16 or 32))
        { return Fail(factory, FrontendAbstention.UnsupportedType); }
        if (info.Width < 32)
        { index = factory.Cast(factory.IntegerType, index); }
        var length = factory.Length(receiver);
        var limit = index.Type == length.Type ? length : factory.Cast(index.Type, length);
        var beyond = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, index, limit);
        if (factory.GetTypeInfo(index.Type).Signed)
        {
            beyond = factory.Binary(IrBinaryOperator.OrElse,
                factory.Binary(IrBinaryOperator.LessThan, index, factory.Integer(index.Type, 0L)), beyond);
        }
        return new(factory.Boolean(true), [new(IrExceptionKind.NullReference, isNull),
            new(IrExceptionKind.IndexOutOfRange, beyond)], FrontendSubsetClassification.Exact);
    }

    // Stores and approximated reads cover single- and multidimensional arrays
    // of value-domain elements with 8- to 32-bit integer indexes.
    internal static bool IsModeledElementAccess(IArrayElementReferenceOperation access)
    {
        return access.ArrayReference.Type is IArrayTypeSymbol array && array.Rank == access.Indices.Length &&
            IsValueDomain(array.ElementType) && access.Indices.All(index =>
                index.Type?.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_SByte or
                    SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_Char);
    }

    private static TotalScalarRule ArrayReadRule(IrFactory factory, IArrayElementReferenceOperation access, ImmutableArray<IrTerm> operands)
    {
        if (access.Indices.Length != 1 || access.ArrayReference.Type is not IArrayTypeSymbol { IsSZArray: true } array ||
            !IsScalar(array.ElementType) || operands.Length != 2)
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        var receiver = operands[0];
        var index = operands[1];
        var info = factory.GetTypeInfo(index.Type);
        if (info.Kind != IrTypeKind.Integer || info.Width is not (8 or 16 or 32 or 64))
        { return Fail(factory, FrontendAbstention.UnsupportedType); }
        // Long/ulong indexing inserts native-width conversions whose overflow
        // can precede even the null check. Source evidence does not bind that
        // execution architecture, so keep those indexes closed.
        if (info.Width == 64)
        { return Fail(factory, FrontendAbstention.UnsupportedOperationKind); }
        if (info.Width < 32)
        { index = factory.Cast(factory.IntegerType, index); }
        var length = factory.Length(receiver);
        var limit = index.Type == length.Type ? length : factory.Cast(index.Type, length);
        var outside = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, index, limit);
        if (factory.GetTypeInfo(index.Type).Signed)
        {
            outside = factory.Binary(IrBinaryOperator.OrElse,
                factory.Binary(IrBinaryOperator.LessThan, index, factory.Integer(index.Type, 0L)), outside);
        }
        // The bounds guard precedes the unsigned-to-signed reinterpretation.
        var narrow = index.Type == factory.IntegerType ? index : factory.Cast(factory.IntegerType, index);
        return new(factory.SequenceAccess(receiver, narrow),
            [new(IrExceptionKind.NullReference, factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type))),
             new(IrExceptionKind.IndexOutOfRange, outside)], FrontendSubsetClassification.Exact);
    }
}
