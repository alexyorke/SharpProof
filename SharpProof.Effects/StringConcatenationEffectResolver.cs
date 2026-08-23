namespace SharpProof.Effects;

/// <summary>
/// Resolves the implicit formatting performed by built-in string
/// concatenation. Roslyn represents the operation as a binary add with no
/// operator method, so the runtime <c>ToString</c> dispatch is otherwise absent
/// from the operation tree.
/// </summary>
internal static class StringConcatenationEffectResolver
{
    internal static EffectSummary Resolve(
        IBinaryOperation binary,
        Compilation compilation,
        EffectCallSiteResolver calls,
        ManagedFlowResult? flow,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
    {
        if (binary.OperatorKind != BinaryOperatorKind.Add ||
            binary.Type?.SpecialType != SpecialType.System_String ||
            binary.ConstantValue.HasValue)
        {
            return EffectSummary.Empty;
        }

        var allocation = EffectSummaryOperations.Allocate(
            EffectAllocationKind.Managed);
        if (binary.OperatorMethod != null)
        {
            return allocation;
        }

        return EffectSummaryOperations.Join(
            allocation,
            ResolveFormattedValue(
                binary.LeftOperand,
                binary,
                compilation,
                calls,
                flow,
                classifyRegion),
            ResolveFormattedValue(
                binary.RightOperand,
                binary,
                compilation,
                calls,
                flow,
                classifyRegion));
    }

    internal static EffectSummary ResolveFormattedValue(
        IOperation operand,
        IOperation origin,
        Compilation compilation,
        EffectCallSiteResolver calls,
        ManagedFlowResult? flow,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
    {
        var formatted = ResolveFormattedValueCall(
            operand,
            origin,
            compilation,
            flow);
        if (!formatted.IsRequired)
        {
            return EffectSummary.Empty;
        }
        if (formatted.Target == null)
        {
            return EffectSummaryOperations.Unsupported();
        }

        return calls.Resolve(
            formatted.Target,
            classifyRegion(formatted.Operand, false),
            ImmutableArray<EffectRegionSet>.Empty,
            ImmutableArray<IOperation?>.Empty,
            IsDispatchUncertain(
                formatted.Target,
                formatted.ReceiverType),
            origin,
            formatted.Operand);
    }

    internal static bool CanFormattedValueCompleteNormally(
        IOperation operand,
        IOperation origin,
        Compilation compilation,
        ManagedFlowResult? flow,
        OperationCompletionEvaluator completionEvaluator)
    {
        var formatted = ResolveFormattedValueCall(
            operand,
            origin,
            compilation,
            flow);
        return !formatted.IsRequired ||
            formatted.Target == null ||
            IsDispatchUncertain(
                formatted.Target,
                formatted.ReceiverType) ||
            completionEvaluator.CanCompleteInvocation(
                formatted.Target,
                formatted.Operand,
                origin);
    }

    private static FormattedValueCall ResolveFormattedValueCall(
        IOperation operand,
        IOperation origin,
        Compilation compilation,
        ManagedFlowResult? flow)
    {
        operand = UnwrapImplicitConversion(operand);
        if (operand.Type?.SpecialType == SpecialType.System_String ||
            operand.ConstantValue is { HasValue: true, Value: null } ||
            flow?.TryEvaluate(origin, operand, out var value) == true &&
            value.IsDefinitelyNull)
        {
            return new(
                operand,
                Target: null,
                ReceiverType: null,
                IsRequired: false);
        }

        var receiverType = UnwrapNullable(operand.Type);
        return new(
            operand,
            ResolveToString(receiverType, compilation),
            receiverType,
            IsRequired: true);
    }

    private static IOperation UnwrapImplicitConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversion &&
               conversion.IsImplicit &&
               conversion.OperatorMethod == null)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType:
                SpecialType.System_Nullable_T,
            TypeArguments: { Length: 1 } typeArguments
        }
            ? typeArguments[0]
            : type;
    }

    private static IMethodSymbol? ResolveToString(
        ITypeSymbol? receiverType,
        Compilation compilation)
    {
        if (receiverType is INamedTypeSymbol named)
        {
            for (var current = named;
                 current != null;
                 current = current.BaseType)
            {
                var target = current.GetMembers("ToString")
                    .OfType<IMethodSymbol>()
                    .SingleOrDefault(IsParameterlessToString);
                if (target != null)
                {
                    return target;
                }
            }

            if (named.TypeKind != TypeKind.Interface)
            {
                return null;
            }
        }

        if (receiverType is not IArrayTypeSymbol and
            not ITypeParameterSymbol and
            not INamedTypeSymbol { TypeKind: TypeKind.Interface })
        {
            return null;
        }

        return compilation.GetSpecialType(SpecialType.System_Object)
            .GetMembers("ToString")
            .OfType<IMethodSymbol>()
            .SingleOrDefault(IsParameterlessToString);
    }

    private static bool IsParameterlessToString(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
            !method.IsStatic &&
            method.Arity == 0 &&
            method.Parameters.IsEmpty &&
            method.ReturnType.SpecialType == SpecialType.System_String;
    }

    private static bool IsDispatchUncertain(
        IMethodSymbol method,
        ITypeSymbol? receiverType)
    {
        return receiverType?.IsValueType != true &&
            receiverType is not INamedTypeSymbol { IsSealed: true } &&
            !method.IsStatic &&
            (method.IsVirtual ||
             method.IsAbstract ||
             method.IsOverride ||
             method.ContainingType?.TypeKind == TypeKind.Interface) &&
            !method.IsSealed;
    }

    private readonly record struct FormattedValueCall(
        IOperation Operand,
        IMethodSymbol? Target,
        ITypeSymbol? ReceiverType,
        bool IsRequired);
}
