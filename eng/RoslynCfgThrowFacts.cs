using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Roslyn;

internal static class RoslynCfgThrowFacts
{
    internal static IEnumerable<BasicBlock> ReachableBlocks(
        ControlFlowGraph graph,
        CancellationToken cancellationToken = default)
    {
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.IsReachable)
            {
                yield return block;
            }
        }
    }

    internal static bool BuiltInOperationMayThrow(IOperation operation)
    {
        if (operation is IIncrementOrDecrementOperation increment &&
            IsUnsupportedImplicitIncrement(increment))
        {
            return true;
        }

        if (operation is IConversionOperation conversion &&
            (conversion.IsChecked && conversion.OperatorMethod == null &&
             !IsSafeDecimalConversion(conversion) ||
             DecimalConversionMayThrow(conversion) ||
             NullableUnwrapMayThrow(conversion)))
        {
            return true;
        }

        if (DecimalOperationMayThrow(operation))
        {
            return true;
        }

        return operation is
            IInvocationOperation or
            IDynamicInvocationOperation or
            IFunctionPointerInvocationOperation or
            IObjectCreationOperation or
            IArrayCreationOperation or
            IArrayElementReferenceOperation or
            IPropertyReferenceOperation or
            ILockOperation or
            ICompoundAssignmentOperation
            {
                IsChecked: true,
                OperatorMethod: null
            } or
            ICompoundAssignmentOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder
            } or
            IBinaryOperation
            {
                IsChecked: true,
                OperatorMethod: null
            } or
            IBinaryOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder
            } or
            IUnaryOperation
            { IsChecked: true, OperatorMethod: null } or
            IIncrementOrDecrementOperation
            { IsChecked: true, OperatorMethod: null };
    }

    internal static bool OperationMayThrow(IOperation operation)
    {
        if (operation is IConversionOperation conversion)
        {
            return conversion.OperatorMethod != null ||
                conversion.IsChecked && !IsSafeDecimalConversion(conversion) ||
                DecimalConversionMayThrow(conversion) ||
                NullableUnwrapMayThrow(conversion) ||
                (!conversion.IsTryCast && !conversion.IsImplicit &&
                 (conversion.Conversion.IsReference ||
                  conversion.Operand.Type?.IsReferenceType == true &&
                  conversion.Type?.IsValueType == true));
        }
        if (operation is IMethodReferenceOperation methodReference)
        {
            return !methodReference.Method.IsStatic &&
                methodReference.Instance?.Type?.IsReferenceType == true;
        }
        return BuiltInOperationMayThrow(operation) ||
            operation is
                IThrowOperation or
                IDynamicObjectCreationOperation or
                IDynamicIndexerAccessOperation or
                IDynamicMemberReferenceOperation or
                IFieldReferenceOperation { Instance: not null } or
                IEventAssignmentOperation or
                IAwaitOperation or
                ICompoundAssignmentOperation { OperatorMethod: not null } or
                IBinaryOperation { OperatorMethod: not null } or
                IUnaryOperation { OperatorMethod: not null } or
                IIncrementOrDecrementOperation { OperatorMethod: not null };
    }

    /// <summary>
    /// Roslyn represents an increment or decrement that is implemented by
    /// implicit conversions as a predefined operator with no operator method.
    /// The conversion calls are not present in the operation tree, so such an
    /// operation must remain conservative unless its target is one of the
    /// language-defined numeric, enum, or pointer types.
    /// </summary>
    internal static bool IsUnsupportedImplicitIncrement(
        IIncrementOrDecrementOperation increment)
    {
        return increment.OperatorMethod == null &&
            !IsPredefinedIncrementType(
                NullableUnderlyingOrSelf(increment.Target.Type));
    }

    private static bool IsPredefinedIncrementType(ITypeSymbol? type)
    {
        return type?.TypeKind is TypeKind.Enum or TypeKind.Pointer ||
            type?.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Char or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_Decimal or
                SpecialType.System_IntPtr or
                SpecialType.System_UIntPtr;
    }

    private static bool DecimalOperationMayThrow(IOperation operation)
    {
        return operation switch
        {
            IBinaryOperation binary when binary.OperatorMethod == null &&
                binary.OperatorKind is
                    BinaryOperatorKind.Add or
                    BinaryOperatorKind.Subtract or
                    BinaryOperatorKind.Multiply or
                    BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder =>
                IsDecimalOperation(binary),
            ICompoundAssignmentOperation assignment when
                assignment.OperatorMethod == null &&
                assignment.OperatorKind is
                    BinaryOperatorKind.Add or
                    BinaryOperatorKind.Subtract or
                    BinaryOperatorKind.Multiply or
                    BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder =>
                IsDecimalOperation(assignment),
            IUnaryOperation unary when unary.OperatorMethod == null &&
                unary.OperatorKind == UnaryOperatorKind.Minus =>
                IsDecimalOperation(unary),
            IIncrementOrDecrementOperation increment when
                increment.OperatorMethod == null =>
                IsDecimalOperation(increment),
            _ => false
        };
    }

    private static bool DecimalConversionMayThrow(
        IConversionOperation operation)
    {
        if (operation.OperatorMethod != null || operation.IsTryCast)
        {
            return false;
        }

        var conversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions
            .GetConversion(operation);
        if (!conversion.IsNumeric && !conversion.IsEnumeration &&
            !conversion.IsNullable)
        {
            return false;
        }

        var source = NullableUnderlyingOrSelf(operation.Operand.Type);
        var target = NullableUnderlyingOrSelf(operation.Type);
        if (source?.SpecialType == SpecialType.System_Decimal)
        {
            return target?.SpecialType is not (
                SpecialType.System_Decimal or
                SpecialType.System_Single or
                SpecialType.System_Double);
        }

        return target?.SpecialType == SpecialType.System_Decimal &&
            source?.SpecialType is
                SpecialType.System_Single or
                SpecialType.System_Double;
    }

    private static bool IsSafeDecimalConversion(
        IConversionOperation operation)
    {
        return !operation.IsTryCast &&
            (IsDecimalType(operation.Operand.Type) ||
             IsDecimalType(operation.Type)) &&
            !DecimalConversionMayThrow(operation);
    }

    private static bool NullableUnwrapMayThrow(IConversionOperation operation)
    {
        if (operation.OperatorMethod != null || operation.IsTryCast ||
            operation.Type == null ||
            GetNullableUnderlyingType(operation.Type) != null ||
            GetNullableUnderlyingType(operation.Operand.Type) == null)
        {
            return false;
        }

        var conversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions
            .GetConversion(operation);
        return conversion.IsNullable && conversion.IsExplicit;
    }

    private static bool IsDecimalOperation(IOperation operation)
    {
        return IsDecimalType(operation.Type) ||
            operation switch
            {
                IBinaryOperation binary =>
                    IsDecimalType(binary.LeftOperand.Type) ||
                    IsDecimalType(binary.RightOperand.Type),
                ICompoundAssignmentOperation assignment =>
                    IsDecimalType(assignment.Target.Type) ||
                    IsDecimalType(assignment.Value.Type),
                IUnaryOperation unary => IsDecimalType(unary.Operand.Type),
                IIncrementOrDecrementOperation increment =>
                    IsDecimalType(increment.Target.Type),
                _ => false
            };
    }

    private static bool IsDecimalType(ITypeSymbol? type)
    {
        return NullableUnderlyingOrSelf(type)?.SpecialType ==
            SpecialType.System_Decimal;
    }

    private static ITypeSymbol? NullableUnderlyingOrSelf(ITypeSymbol? type)
    {
        return GetNullableUnderlyingType(type) ?? type;
    }

    private static ITypeSymbol? GetNullableUnderlyingType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1
        } nullable
            ? nullable.TypeArguments[0]
            : null;
    }

    internal static IEnumerable<BasicBlock> ExceptionalSuccessors(
        ControlFlowGraph graph,
        BasicBlock block,
        CancellationToken cancellationToken = default)
    {
        var yielded = new HashSet<int>();
        for (var region = block.EnclosingRegion;
             region != null;
             region = region.EnclosingRegion)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (region.Kind != ControlFlowRegionKind.Try ||
                region.EnclosingRegion is not { } owner)
            {
                continue;
            }

            foreach (var handler in owner.NestedRegions.Where(candidate =>
                         candidate.Kind is ControlFlowRegionKind.Filter or
                             ControlFlowRegionKind.Catch or
                             ControlFlowRegionKind.FilterAndHandler or
                             ControlFlowRegionKind.Finally))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (yielded.Add(handler.FirstBlockOrdinal))
                {
                    yield return graph.Blocks[handler.FirstBlockOrdinal];
                }
            }
        }
    }
}
