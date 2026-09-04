namespace SharpProof.Effects;

/// <summary>
/// Resolves the implicit formatting performed by built-in string
/// concatenation and interpolation. Roslyn does not expose the runtime
/// formatting dispatch as an invocation in either operation tree.
/// </summary>
internal static class StringConcatenationEffectResolver
{

    internal static bool DefersInterpolationFormatting(
        IInterpolatedStringOperation interpolation,
        Compilation compilation)
    {
        return IsDeferredInterpolationType(
                interpolation.Type,
                compilation) ||
            interpolation.Parent is IConversionOperation conversion &&
            IsDeferredInterpolationType(conversion.Type, compilation);
    }

    internal static bool DefersInterpolationFormatting(
        IInterpolationOperation interpolation,
        Compilation compilation)
    {
        return interpolation.Parent is IInterpolatedStringOperation owner &&
            DefersInterpolationFormatting(owner, compilation);
    }

    internal static bool IsBuiltInStringConcatenation(
        IBinaryOperation binary)
    {
        return binary.OperatorKind == BinaryOperatorKind.Add &&
            binary.Type?.SpecialType == SpecialType.System_String &&
            !binary.ConstantValue.HasValue &&
            binary.OperatorMethod == null;
    }

    internal static bool IsBuiltInStringConcatenation(
        ICompoundAssignmentOperation assignment)
    {
        return assignment.OperatorKind == BinaryOperatorKind.Add &&
            assignment.Type?.SpecialType == SpecialType.System_String &&
            assignment.OperatorMethod == null;
    }

    internal static EffectSummary Resolve(
        IBinaryOperation binary,
        Compilation compilation,
        EffectCallSiteResolver calls,
        ManagedFlowResult? flow,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
    {
        if (!IsBuiltInStringConcatenation(binary))
        {
            return EffectSummary.Empty;
        }

        var allocation = EffectSummaryOperations.Allocate(
            EffectAllocationKind.Managed);
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

    internal static EffectSummary Resolve(
        ICompoundAssignmentOperation assignment,
        Compilation compilation,
        EffectCallSiteResolver calls,
        ManagedFlowResult? flow,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
    {
        if (!IsBuiltInStringConcatenation(assignment))
        {
            return EffectSummary.Empty;
        }

        return EffectSummaryOperations.Join(
            EffectSummaryOperations.Allocate(
                EffectAllocationKind.Managed),
            ResolveFormattedValue(
                assignment.Target,
                assignment,
                compilation,
                calls,
                flow,
                classifyRegion),
            ResolveFormattedValue(
                assignment.Value,
                assignment,
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
        return ResolveFormattedValue(
            formatted,
            origin,
            calls,
            classifyRegion);
    }

    internal static (EffectSummary Summary, bool CompletesNormally)
        ResolveFormattedValueEffects(
        IOperation operand,
        IOperation origin,
        Compilation compilation,
        EffectCallSiteResolver calls,
        ManagedFlowResult? flow,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion,
        OperationCompletionEvaluator completionEvaluator)
    {
        var formatted = ResolveFormattedValueCall(
            operand,
            origin,
            compilation,
            flow);
        return (
            ResolveFormattedValue(
                formatted,
                origin,
                calls,
                classifyRegion),
            CanFormattedValueCompleteNormally(
                formatted,
                origin,
                completionEvaluator));
    }

    private static EffectSummary ResolveFormattedValue(
        FormattedValueCall formatted,
        IOperation origin,
        EffectCallSiteResolver calls,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
    {
        if (!formatted.IsRequired)
        {
            return EffectSummary.Empty;
        }
        if (formatted.Target == null)
        {
            return EffectSummaryOperations.Unsupported();
        }

        var argumentRegions = Enumerable.Repeat(
                EffectRegionSet.Empty,
                formatted.Target.Parameters.Length)
            .ToImmutableArray();
        var actualArguments = Enumerable.Repeat<IOperation?>(
                null,
                formatted.Target.Parameters.Length)
            .ToImmutableArray();
        return calls.Resolve(
            formatted.Target,
            classifyRegion(formatted.Operand, false),
            argumentRegions,
            actualArguments,
            IsDispatchUncertain(
                formatted.Target,
                formatted.ReceiverType,
                formatted.IsInterpolation),
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
        return CanFormattedValueCompleteNormally(
            formatted,
            origin,
            completionEvaluator);
    }

    private static bool CanFormattedValueCompleteNormally(
        FormattedValueCall formatted,
        IOperation origin,
        OperationCompletionEvaluator completionEvaluator)
    {
        return !formatted.IsRequired ||
            formatted.Target == null ||
            IsDispatchUncertain(
                formatted.Target,
                formatted.ReceiverType,
                formatted.IsInterpolation) ||
            completionEvaluator.CanCompleteInvocation(
                formatted.Target,
                formatted.Operand,
                origin);
    }

    internal static bool TryResolveFormattedValueMethod(
        IOperation operand,
        IOperation origin,
        Compilation compilation,
        ManagedFlowResult? flow,
        out IMethodSymbol? target,
        out bool dispatchUncertain)
    {
        var formatted = ResolveFormattedValueCall(
            operand,
            origin,
            compilation,
            flow);
        target = formatted.Target;
        dispatchUncertain = target != null && IsDispatchUncertain(
            target,
            formatted.ReceiverType,
            formatted.IsInterpolation);
        return formatted.IsRequired;
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
                IsRequired: false,
                IsInterpolation: false);
        }

        var receiverType = UnwrapNullable(operand.Type);
        var isInterpolation = origin is IInterpolationOperation;
        var target = isInterpolation &&
            TryResolveIFormattableToString(
                receiverType,
                compilation,
                out var formattingMethod)
                ? formattingMethod
                : ResolveToString(receiverType, compilation);
        return new(
            operand,
            target,
            receiverType,
            IsRequired: true,
            IsInterpolation: isInterpolation);
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

    private static bool IsDeferredInterpolationType(
        ITypeSymbol? type,
        Compilation compilation)
    {
        if (type == null)
        {
            return false;
        }

        var formattableString = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.FormattableString);
        var formattable = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.IFormattable);
        return formattableString != null &&
                SymbolEqualityComparer.Default.Equals(
                    type,
                    formattableString) ||
            formattable != null &&
                SymbolEqualityComparer.Default.Equals(type, formattable);
    }

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? type)
    {
        return CompilerIdentityBridge.GetNullableUnderlyingType(type) ?? type;
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

    private static bool TryResolveIFormattableToString(
        ITypeSymbol? receiverType,
        Compilation compilation,
        out IMethodSymbol? target)
    {
        target = null;
        if (receiverType == null)
        {
            return false;
        }

        var formattable = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.IFormattable);
        var formatProvider = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.IFormatProvider);
        if (formattable == null || formatProvider == null)
        {
            return false;
        }

        var interfaceMethod = formattable.GetMembers("ToString")
            .OfType<IMethodSymbol>()
            .SingleOrDefault(method =>
                IsIFormattableToString(method, formatProvider));
        if (interfaceMethod == null ||
            !ImplementsIFormattable(receiverType, formattable))
        {
            return false;
        }

        target = receiverType is INamedTypeSymbol
        {
            TypeKind: not TypeKind.Interface
        } named
            ? named.FindImplementationForInterfaceMember(
                interfaceMethod) as IMethodSymbol
            : interfaceMethod;
        return true;
    }

    private static bool ImplementsIFormattable(
        ITypeSymbol receiverType,
        INamedTypeSymbol formattable,
        HashSet<ITypeSymbol>? visited = null)
    {
        visited ??= new HashSet<ITypeSymbol>(
            SymbolEqualityComparer.Default);
        if (!visited.Add(receiverType))
        {
            return false;
        }

        if (receiverType is INamedTypeSymbol named)
        {
            return SymbolEqualityComparer.Default.Equals(
                    named.OriginalDefinition,
                    formattable) ||
                named.AllInterfaces.Any(@interface =>
                    SymbolEqualityComparer.Default.Equals(
                        @interface.OriginalDefinition,
                        formattable));
        }

        return receiverType is ITypeParameterSymbol typeParameter &&
            typeParameter.ConstraintTypes.Any(constraint =>
                ImplementsIFormattable(
                    constraint,
                    formattable,
                    visited));
    }

    private static bool IsIFormattableToString(
        IMethodSymbol method,
        INamedTypeSymbol formatProvider)
    {
        return method.MethodKind == MethodKind.Ordinary &&
            !method.IsStatic &&
            method.Arity == 0 &&
            method.Parameters.Length == 2 &&
            method.Parameters[0].Type.SpecialType ==
                SpecialType.System_String &&
            SymbolEqualityComparer.Default.Equals(
                method.Parameters[1].Type,
                formatProvider) &&
            method.ReturnType.SpecialType == SpecialType.System_String;
    }

    private static bool IsDispatchUncertain(
        IMethodSymbol method,
        ITypeSymbol? receiverType,
        bool isInterpolation)
    {
        if (isInterpolation &&
            (receiverType is ITypeParameterSymbol ||
             receiverType?.IsValueType != true &&
             receiverType is not INamedTypeSymbol { IsSealed: true }))
        {
            return true;
        }

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
        bool IsRequired,
        bool IsInterpolation);
}
