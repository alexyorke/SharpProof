namespace SharpProof.Symbolic.Ir;
internal static class SymbolicKnownApiLowerer {
    private static readonly KnownApiLoweringDescriptor<SymbolicTerm> MathAbsLowering = new(
        "System.Math",
        nameof(Math.Abs),
        SymbolicNumericLowerer.TryLowerIntegralMathAbsInvocation);
    private static readonly KnownApiLoweringDescriptor<SymbolicTerm> MathClampLowering = new(
        "System.Math",
        "Clamp",
        SymbolicNumericLowerer.TryLowerIntegralMathClampInvocation);
    private static readonly ImmutableArray<KnownApiLoweringDescriptor<SymbolicCondition>> KnownApiLowerings =
        [
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.Object", nameof(ReferenceEquals),
                SymbolicObjectLowerer.TryLowerObjectReferenceEqualsInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.Contains),
                SymbolicStringLowerer.TryLowerStringPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.StartsWith),
                SymbolicStringLowerer.TryLowerStringPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.EndsWith),
                SymbolicStringLowerer.TryLowerStringPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.IsNullOrEmpty),
                SymbolicStringLowerer.TryLowerStringNullOrPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.IsNullOrWhiteSpace),
                SymbolicStringLowerer.TryLowerStringNullOrPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.Equals),
                SymbolicStringLowerer.TryLowerStringEqualsInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.Text.RegularExpressions.Regex", nameof(Regex.IsMatch),
                SymbolicStringLowerer.TryLowerRegexIsMatchInvocation),
        ];
    private static readonly ImmutableArray<KnownApiLoweringDescriptor<SymbolicTerm>> KnownApiTermLowerings =
        [
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                        SpecialType.System_Nullable_T,
                        nameof(Nullable<int>.GetValueOrDefault),
                        SymbolicNullableLowerer.TryLowerNullableGetValueOrDefaultInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetLength),
                SymbolicIndexingLowerer.TryLowerArrayGetLengthInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetLongLength),
                SymbolicIndexingLowerer.TryLowerArrayGetLengthInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetLowerBound),
                SymbolicIndexingLowerer.TryLowerArrayBoundInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetUpperBound),
                SymbolicIndexingLowerer.TryLowerArrayBoundInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                "System.Math",
                nameof(Math.Min),
                SymbolicNumericLowerer.TryLowerIntegralMathMinMaxInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                "System.Math",
                nameof(Math.Max),
                SymbolicNumericLowerer.TryLowerIntegralMathMinMaxInvocation),
            MathAbsLowering,
            MathClampLowering,
        ];
    internal static bool IsMathAbs(IMethodSymbol method) => MathAbsLowering.Matches(method);
    internal static bool IsMathClamp(IMethodSymbol method) => MathClampLowering.Matches(method);
    internal static bool TryLowerKnownApiInvocation(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) => TryLowerKnownApiInvocation(invocation, context, KnownApiLowerings, out condition);
    internal static bool TryLowerKnownApiInvocationTerm(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicTerm term) => TryLowerKnownApiInvocation(invocation, context, KnownApiTermLowerings, out term);
    private static bool TryLowerKnownApiInvocation<TValue>(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        ImmutableArray<KnownApiLoweringDescriptor<TValue>> descriptors,
        out TValue value) {
        value = default!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation
            operation) return false;
        foreach (var descriptor in descriptors)
            if (descriptor.Matches(operation.TargetMethod) &&
                descriptor.Handler(invocation, operation.TargetMethod, context, out value))
                return true;
        return false;
    }
    internal static bool TryLowerKnownStaticValueMember(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol ??
                           context.SemanticModel.GetSymbolInfo(memberAccess.Name, context.CancellationToken).Symbol;
        if (SymbolicStringLowerer.TryLowerStringStaticValueMember(memberSymbol, out term)) return true;
        return SymbolicNumericLowerer.TryLowerBigIntegerStaticValueMember(memberSymbol, out term);
    }
}
