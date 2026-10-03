namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static TotalScalarRule ArrayEmpty(IrFactory factory, IrTypeId type)
    {
        return Exact(factory.EmptyArray(type));
    }

    internal static TotalScalarRule StringConcat(IrFactory factory, IrTerm left, IrTerm right)
    {
        return Exact(factory.RewriteBinary(IrBinaryOperator.StringConcat, left, right));
    }

    internal static bool IsReferenceDomain(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_Object or SpecialType.System_String ||
            type?.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Delegate ||
            type is IArrayTypeSymbol { IsSZArray: true } array &&
            (IsScalar(array.ElementType) || array.ElementType.SpecialType is SpecialType.System_Object or SpecialType.System_String);
    }

    internal static bool IsValueDomain(ITypeSymbol? type)
    {
        return IsScalar(type) || IsReferenceDomain(type);
    }

    internal static IrTerm DefaultValue(IrFactory factory, IrTypeId type)
    {
        return factory.GetTypeInfo(type).Kind switch
        {
            IrTypeKind.Boolean => factory.Boolean(false),
            IrTypeKind.Integer => factory.Integer(type, 0),
            IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence => factory.Null(type),
            _ => throw new ArgumentException("The type has no supported Total default value.", nameof(type))
        };
    }

    internal static bool IsLength(IPropertyReferenceOperation property)
    {
        return IsReferenceDomain(property.Instance?.Type) && property.Property.MetadataName == "Length" &&
            property.Type?.SpecialType == SpecialType.System_Int32 && CompilerIdentityBridge.IsIntrinsicSequenceLength(property);
    }

    internal static (IOperation Left, IOperation Right) EqualityOperands(IBinaryOperation operation)
    {
        if (operation.OperatorMethod != null || operation.IsLifted ||
            operation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        { return (operation.LeftOperand, operation.RightOperand); }
        IOperation Unwrap(IOperation operand)
        {
            return operand is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion &&
                conversion.Type?.SpecialType == SpecialType.System_Object && conversion.Conversion.IsReference &&
                (IsReferenceDomain(conversion.Operand.Type) || conversion.Operand.ConstantValue is { HasValue: true, Value: null })
                ? conversion.Operand : operand;
        }
        var left = Unwrap(operation.LeftOperand);
        var right = Unwrap(operation.RightOperand);
        var leftReference = IsReferenceDomain(left.Type);
        var rightReference = IsReferenceDomain(right.Type);
        var sameReferences = leftReference && rightReference && SymbolEqualityComparer.Default.Equals(left.Type, right.Type);
        var referenceAndNull = leftReference && right.ConstantValue is { HasValue: true, Value: null } ||
            rightReference && left.ConstantValue is { HasValue: true, Value: null };
        return sameReferences || referenceAndNull ? (left, right) : (operation.LeftOperand, operation.RightOperand);
    }

    private static TotalScalarRule? ReferenceRule(IrFactory factory, IOperation operation, ImmutableArray<IrTerm> operands)
    {
        if (operation is IArrayElementReferenceOperation access)
        { return ArrayReadRule(factory, access, operands); }
        if (operation is IPropertyReferenceOperation property && IsLength(property))
        {
            var receiver = operands[0];
            return new(factory.Length(receiver),
                [new(IrExceptionKind.NullReference, factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type)))],
                FrontendSubsetClassification.Exact);
        }
        if (operation is IConversionOperation conversion && IsReferenceDomain(operation.Type))
        {
            var target = new RoslynTypeMapper(factory).GetTypeId(operation.Type);
            if (conversion.OperatorMethod == null && (operands[0] is IrNullTerm ||
                    conversion.Conversion.IsIdentity && operands[0].Type == target))
            {
                return Exact(operands[0] is IrNullTerm ? factory.Null(target) : operands[0]);
            }
            return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
        }
        if (operation is IBinaryOperation binary && operands.Length == 2 &&
            factory.GetTypeInfo(operands[0].Type).Kind is IrTypeKind.Reference or IrTypeKind.Sequence or IrTypeKind.String)
        {
            var left = operands[0];
            var right = operands[1];
            if (left is IrNullTerm && left.Type != right.Type)
            { left = factory.Null(right.Type); }
            if (right is IrNullTerm && left.Type != right.Type)
            { right = factory.Null(left.Type); }
            if (binary.IsLifted || left.Type != right.Type ||
                binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals) ||
                factory.GetTypeInfo(left.Type).Kind == IrTypeKind.String &&
                    left is not IrNullTerm && right is not IrNullTerm ||
                binary.OperatorMethod != null && binary.OperatorMethod.ContainingType.SpecialType != SpecialType.System_String)
            {
                return Fail(factory, FrontendAbstention.UnsupportedOperationKind);
            }
            return Exact(factory.Binary(binary.OperatorKind == BinaryOperatorKind.Equals ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                left, right));
        }
        return null;
    }
}
