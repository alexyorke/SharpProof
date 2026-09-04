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
                CheckedOverflow(operation.IsChecked, operation));
        }

        if (conversion is { IsNumeric: true } or { IsEnumeration: true })
        {
            return CheckedOverflow(operation.IsChecked, operation);
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
        return isChecked &&
               !SkipsLiftedOperator(operation) &&
               abstractFlow?.ProvesNoOverflow(operation) != true
            ? Throw(FrameworkTypeMetadataNames.OverflowException)
            : EffectSummary.Empty;
    }

    internal bool SkipsLiftedOperator(IOperation operation)
    {
        return SkipsLiftedOperator(operation, abstractFlow);
    }

    internal static bool SkipsLiftedOperator(
        IOperation operation,
        ManagedFlowResult? flow)
    {
        var operands = operation switch
        {
            IConversionOperation conversion when
                IsLiftedNullableUserConversion(conversion) =>
                [conversion.Operand],
            IBinaryOperation { IsLifted: true } binary =>
                [binary.LeftOperand, binary.RightOperand],
            IUnaryOperation { IsLifted: true } unary =>
                [unary.Operand],
            IIncrementOrDecrementOperation { IsLifted: true } increment =>
                [increment.Target],
            ICompoundAssignmentOperation { IsLifted: true } assignment =>
                [assignment.Target, assignment.Value],
            _ => Array.Empty<IOperation>()
        };

        return operands.Any(operand =>
            ManagedAbstractValue.IsNullableType(operand.Type) &&
            (operand.ConstantValue is { HasValue: true, Value: null } ||
                flow?.TryEvaluate(
                    operation,
                    operand,
                    out var value) == true &&
                value.IsDefinitelyNull));
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
