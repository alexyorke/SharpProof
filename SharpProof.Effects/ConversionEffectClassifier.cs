namespace SharpProof.Effects;

/// <summary>
/// Classifies the intrinsic allocation and exception effects of conversions.
/// Operator-method effects remain owned by the operation scanner.
/// </summary>
internal sealed class ConversionEffectClassifier(
    EffectAnalysisSession session,
    ManagedFlowResult? abstractFlow)
{
    internal EffectSummary Classify(
        IConversionOperation operation,
        Microsoft.CodeAnalysis.CSharp.Conversion conversion)
    {
        return Classify(
            operation,
            conversion,
            SkipsLiftedOperator(operation));
    }

    internal EffectSummary Classify(
        IConversionOperation operation,
        Microsoft.CodeAnalysis.CSharp.Conversion conversion,
        bool skipsLiftedOperator)
    {
        if (!conversion.Exists)
        {
            return EffectSummaryOperations.Unsupported();
        }

        // These categories can overlap. Handle the effectful categories first,
        // then the effect-neutral categories, and fail closed for every
        // remaining Roslyn conversion category.
        if (conversion.IsDynamic)
        {
            return EffectSummaryOperations.Unsupported();
        }

        if (conversion.IsBoxing)
        {
            return ClassifyBoxing(operation);
        }

        if (conversion.IsUnboxing)
        {
            return ClassifyUnboxing(operation);
        }

        if (conversion.IsUserDefined)
        {
            // Checked user-defined conversions select a different user method;
            // they do not add an intrinsic numeric overflow check. The
            // scanner owns every effect of the selected operator method.
            return ClassifyNullableConversion(
                operation,
                EffectSummary.Empty);
        }

        if (conversion.IsReference)
        {
            if (!conversion.IsExplicit || operation.IsTryCast ||
                IsDefinitelyNull(operation, operation.Operand) ||
                HasExactPreservedRuntimeType(operation))
            {
                return EffectSummary.Empty;
            }

            return Throw(FrameworkTypeMetadataNames.InvalidCastException);
        }

        if (conversion.IsNullable)
        {
            return ClassifyNullableConversion(
                operation,
                CheckedOverflow(
                    operation.IsChecked,
                    operation,
                    skipsLiftedOperator));
        }

        if (conversion is { IsNumeric: true } or { IsEnumeration: true })
        {
            return CheckedOverflow(
                operation.IsChecked,
                operation,
                skipsLiftedOperator);
        }

        if (conversion.IsInterpolatedString)
        {
            return EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        }

        if (conversion is
        { IsAnonymousFunction: true } or
        { IsMethodGroup: true })
        {
            var allocation = EffectSummaryOperations.Allocate(
                EffectAllocationKind.Managed);
            var methodReference = conversion.IsMethodGroup
                ? MethodGroupConversionFacts
                    .GetDelegateConstructorCheckedTarget(operation)
                : null;
            return methodReference?.Instance is { } instance &&
                !IsDefinitelyNonNull(operation, instance) &&
                !DefiniteOperationFacts.IsDefinitelyNonNull(instance)
                    ? EffectSummaryOperations.Join(
                        allocation,
                        Throw(FrameworkTypeMetadataNames.ArgumentException))
                    : allocation;
        }

        if (conversion is
        { IsIdentity: true } or
        { IsNullLiteral: true } or
        { IsDefaultLiteral: true } or
        { IsConstantExpression: true } or
        { IsThrow: true } or
        { IsObjectCreation: true } or
        { IsSwitchExpression: true } or
        { IsConditionalExpression: true })
        {
            return EffectSummary.Empty;
        }

        // Collection expressions, interpolated-string handlers, tuple
        // conversions, stackalloc/span/inline-array conversions, pointer and
        // native-integer conversions are not modeled by this effect domain.
        return EffectSummaryOperations.Unsupported();
    }

    internal EffectSummary CheckedOverflow(
        bool isChecked,
        IOperation operation)
    {
        return CheckedOverflow(
            isChecked,
            operation,
            SkipsLiftedOperator(operation));
    }

    internal EffectSummary CheckedOverflow(
        bool isChecked,
        IOperation operation,
        bool skipsLiftedOperator)
    {
        var isSafeDecimalConversion = operation is IConversionOperation conversion &&
            (IsDecimalType(conversion.Operand.Type) ||
             IsDecimalType(conversion.Type)) &&
            !DecimalConversionMayOverflow(conversion);
        return ((isChecked && !isSafeDecimalConversion &&
                 !IsKnownNonOverflowingDecimalOperation(operation)) ||
                IsAlwaysCheckedDecimal(operation)) &&
               !skipsLiftedOperator &&
               abstractFlow?.ProvesNoOverflow(operation) != true
            ? Throw(FrameworkTypeMetadataNames.OverflowException)
            : EffectSummary.Empty;
    }

    private static bool IsKnownNonOverflowingDecimalOperation(
        IOperation operation)
    {
        return operation switch
        {
            IBinaryOperation binary when binary.OperatorMethod == null &&
                binary.OperatorKind == BinaryOperatorKind.Remainder =>
                IsDecimalOperation(binary),
            ICompoundAssignmentOperation assignment when
                assignment.OperatorMethod == null &&
                assignment.OperatorKind == BinaryOperatorKind.Remainder =>
                IsDecimalOperation(assignment),
            IUnaryOperation unary when unary.OperatorMethod == null &&
                unary.OperatorKind == UnaryOperatorKind.Plus =>
                IsDecimalOperation(unary),
            _ => false
        };
    }

    private static bool IsAlwaysCheckedDecimal(IOperation operation)
    {
        return operation switch
        {
            IConversionOperation conversion =>
                DecimalConversionMayOverflow(conversion),
            IBinaryOperation binary when binary.OperatorMethod == null &&
                binary.OperatorKind is
                    BinaryOperatorKind.Add or
                    BinaryOperatorKind.Subtract or
                    BinaryOperatorKind.Multiply or
                    BinaryOperatorKind.Divide =>
                IsDecimalOperation(binary),
            ICompoundAssignmentOperation assignment when
                assignment.OperatorMethod == null &&
                assignment.OperatorKind is
                    BinaryOperatorKind.Add or
                    BinaryOperatorKind.Subtract or
                    BinaryOperatorKind.Multiply or
                    BinaryOperatorKind.Divide =>
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

    private static bool DecimalConversionMayOverflow(
        IConversionOperation operation)
    {
        if (operation.OperatorMethod != null)
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
        return CompilerIdentityBridge.GetNullableUnderlyingType(type) ?? type;
    }

    internal bool SkipsLiftedOperator(IOperation operation)
    {
        return SkipsLiftedOperator(operation, abstractFlow);
    }

    internal static bool SkipsLiftedOperator(
        IOperation operation,
        ManagedFlowResult? flow)
    {
        return operation switch
        {
            IConversionOperation conversion when
                IsLiftedNullableUserConversion(conversion) =>
                IsDefinitelyNull(operation, conversion.Operand, flow),
            IBinaryOperation { IsLifted: true } binary =>
                IsDefinitelyNull(operation, binary.LeftOperand, flow) ||
                IsDefinitelyNull(operation, binary.RightOperand, flow),
            IUnaryOperation { IsLifted: true } unary =>
                IsDefinitelyNull(operation, unary.Operand, flow),
            IIncrementOrDecrementOperation { IsLifted: true } increment =>
                IsDefinitelyNull(operation, increment.Target, flow),
            ICompoundAssignmentOperation { IsLifted: true } assignment =>
                IsDefinitelyNull(operation, assignment.Target, flow) ||
                IsDefinitelyNull(operation, assignment.Value, flow),
            _ => false
        };
    }

    private static bool IsDefinitelyNull(
        IOperation origin,
        IOperation operand,
        ManagedFlowResult? flow)
    {
        return ManagedAbstractValue.IsNullableType(operand.Type) &&
            (operand.ConstantValue is { HasValue: true, Value: null } ||
            flow?.TryEvaluate(origin, operand, out var value) == true &&
            value.IsDefinitelyNull);
    }

    internal static bool IsLiftedNullableUserConversion(
        IConversionOperation operation)
    {
        return operation.OperatorMethod is
        {
            Parameters.Length: 1,
            ReturnType.IsValueType: true
        } method &&
            method.Parameters[0].Type.IsValueType &&
            !ManagedAbstractValue.IsNullableType(
                method.Parameters[0].Type) &&
            !ManagedAbstractValue.IsNullableType(method.ReturnType) &&
            ManagedAbstractValue.IsNullableType(operation.Operand.Type) &&
            ManagedAbstractValue.IsNullableType(operation.Type);
    }

    private EffectSummary ClassifyBoxing(IConversionOperation operation)
    {
        if (!ManagedAbstractValue.IsNullableType(operation.Operand.Type))
        {
            return EffectSummaryOperations.Allocate(
                EffectAllocationKind.Managed);
        }

        if (abstractFlow?.TryEvaluate(
                operation,
                operation.Operand,
                out var operand) == true)
        {
            if (operand.IsDefinitelyNull)
            {
                return EffectSummary.Empty;
            }

            if (operand.IsDefinitelyNonNull)
            {
                return EffectSummaryOperations.Allocate(
                    EffectAllocationKind.Managed);
            }
        }

        return EffectSummaryOperations.Allocate(
            EffectAllocationKind.Unknown);
    }

    private EffectSummary ClassifyUnboxing(IConversionOperation operation)
    {
        var nullableTarget = ManagedAbstractValue.IsNullableType(operation.Type);
        if (HasExactPreservedRuntimeType(operation))
        {
            return EffectSummary.Empty;
        }

        if (abstractFlow?.TryEvaluate(
                operation,
                operation.Operand,
                out var operand) == true)
        {
            if (operand.IsDefinitelyNull)
            {
                return nullableTarget
                    ? EffectSummary.Empty
                    : Throw(FrameworkTypeMetadataNames.NullReferenceException);
            }

            if (operand.IsDefinitelyNonNull)
            {
                return Throw(FrameworkTypeMetadataNames.InvalidCastException);
            }
        }

        return nullableTarget
            ? Throw(FrameworkTypeMetadataNames.InvalidCastException)
            : Throw(
                FrameworkTypeMetadataNames.InvalidCastException,
                FrameworkTypeMetadataNames.NullReferenceException);
    }

    private static bool HasExactPreservedRuntimeType(
        IConversionOperation operation)
    {
        var operand = operation.Operand;
        while (operand is IParenthesizedOperation parenthesized)
        {
            operand = parenthesized.Operand;
        }

        if (operand is not IConversionOperation preserved ||
            preserved.OperatorMethod != null ||
            !string.Equals(
                preserved.Syntax.Language,
                LanguageNames.CSharp,
                StringComparison.Ordinal))
        {
            return false;
        }

        var conversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions
            .GetConversion(preserved);
        if (!conversion.IsBoxing &&
            !(conversion.IsReference && conversion.IsImplicit))
        {
            return false;
        }

        var source = preserved.Operand.Type;
        var target = CompilerIdentityBridge.GetNullableUnderlyingType(
            operation.Type) ?? operation.Type;
        if (ManagedAbstractValue.IsNullableType(source))
        {
            if (!ManagedAbstractValue.IsNullableType(operation.Type))
            {
                return false;
            }

            source = CompilerIdentityBridge.GetNullableUnderlyingType(source);
        }

        return source != null && target != null &&
            SymbolEqualityComparer.Default.Equals(source, target);
    }

    private EffectSummary ClassifyNullableConversion(
        IConversionOperation operation,
        EffectSummary result)
    {
        if (ManagedAbstractValue.IsNullableType(operation.Operand.Type) &&
            !ManagedAbstractValue.IsNullableType(operation.Type) &&
            !IsDefinitelyNonNull(operation, operation.Operand))
        {
            result = EffectSummaryOperations.Join(
                result,
                Throw(FrameworkTypeMetadataNames.InvalidOperationException));
        }

        return result;
    }

    private bool IsDefinitelyNull(IOperation origin, IOperation value)
    {
        return abstractFlow?.TryEvaluate(origin, value, out var result) == true &&
               result.IsDefinitelyNull;
    }

    private bool IsDefinitelyNonNull(IOperation origin, IOperation value)
    {
        return abstractFlow?.TryEvaluate(origin, value, out var result) == true &&
               result.IsDefinitelyNonNull;
    }

    private EffectSummary Throw(params string[] exceptionMetadataNames)
    {
        return EffectSummaryOperations.Throw(
            session.ResolveExceptionSet(exceptionMetadataNames));
    }
}
