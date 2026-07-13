using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static readonly ImmutableArray<KnownApiLoweringDescriptor<SymbolicCondition>> KnownApiLowerings =
        ImmutableArray.Create(
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.Object", nameof(ReferenceEquals), TryLowerObjectReferenceEqualsInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.Contains), TryLowerStringPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.StartsWith), TryLowerStringPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.EndsWith), TryLowerStringPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.IsNullOrEmpty),
                TryLowerStringNullOrPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.IsNullOrWhiteSpace),
                TryLowerStringNullOrPredicateInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.String", nameof(string.Equals), TryLowerStringEqualsInvocation),
            new KnownApiLoweringDescriptor<SymbolicCondition>("System.Text.RegularExpressions.Regex", nameof(Regex.IsMatch),
                TryLowerRegexIsMatchInvocation));

    private static readonly ImmutableArray<KnownApiLoweringDescriptor<SymbolicTerm>> KnownApiTermLowerings =
        ImmutableArray.Create(
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Nullable_T,
                nameof(Nullable<int>.GetValueOrDefault),
                TryLowerNullableGetValueOrDefaultInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetLength),
                TryLowerArrayGetLengthInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetLongLength),
                TryLowerArrayGetLengthInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetLowerBound),
                TryLowerArrayBoundInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                SpecialType.System_Array,
                nameof(Array.GetUpperBound),
                TryLowerArrayBoundInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                "System.Math",
                nameof(Math.Min),
                TryLowerIntegralMathMinMaxInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                "System.Math",
                nameof(Math.Max),
                TryLowerIntegralMathMinMaxInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                "System.Math",
                nameof(Math.Abs),
                TryLowerIntegralMathAbsInvocation),
            new KnownApiLoweringDescriptor<SymbolicTerm>(
                "System.Math",
                "Clamp",
                TryLowerIntegralMathClampInvocation));

    private static bool TryLowerKnownApiInvocation(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        return TryLowerKnownApiInvocation(invocation, context, KnownApiLowerings, out condition);
    }

    private static bool TryLowerKnownApiInvocationTerm(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        return TryLowerKnownApiInvocation(invocation, context, KnownApiTermLowerings, out term);
    }

    private static bool TryLowerKnownApiInvocation<TValue>(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        ImmutableArray<KnownApiLoweringDescriptor<TValue>> descriptors,
        out TValue value)
    {
        value = default!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation
            operation) return false;

        foreach (var descriptor in descriptors)
            if (descriptor.Matches(operation.TargetMethod) &&
                descriptor.Handler(invocation, operation.TargetMethod, context, out value))
                return true;

        return false;
    }

    private static bool TryLowerKnownStaticValueMember(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol ??
                           context.SemanticModel.GetSymbolInfo(memberAccess.Name, context.CancellationToken).Symbol;

        if (TryLowerStringStaticValueMember(memberSymbol, out term)) return true;

        return TryLowerBigIntegerStaticValueMember(memberSymbol, out term);
    }
}
