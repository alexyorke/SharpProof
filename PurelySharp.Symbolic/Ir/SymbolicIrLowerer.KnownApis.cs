using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static readonly ImmutableArray<KnownApiLoweringDescriptor> KnownApiLowerings =
            ImmutableArray.Create(
                new KnownApiLoweringDescriptor("object", nameof(object.ReferenceEquals), TryLowerObjectReferenceEqualsInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.Contains), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.StartsWith), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.EndsWith), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.IsNullOrEmpty), TryLowerStringNullOrPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.IsNullOrWhiteSpace), TryLowerStringNullOrPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.Equals), TryLowerStringEqualsInvocation),
                new KnownApiLoweringDescriptor("System.Text.RegularExpressions.Regex", nameof(Regex.IsMatch), TryLowerRegexIsMatchInvocation));

        private static bool TryLowerKnownApiInvocation(
            InvocationExpressionSyntax invocation,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation)
            {
                return false;
            }

            foreach (var descriptor in KnownApiLowerings)
            {
                if (descriptor.Matches(operation.TargetMethod) &&
                    descriptor.Handler(invocation, operation.TargetMethod, context, out condition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryLowerKnownApiInvocationTerm(
            InvocationExpressionSyntax invocation,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation)
            {
                return false;
            }

            foreach (var descriptor in KnownApiTermLowerings)
            {
                if (descriptor.Matches(operation.TargetMethod) &&
                    descriptor.Handler(invocation, operation.TargetMethod, context, out term))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryLowerKnownStaticValueMember(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            var memberSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol ??
                context.SemanticModel.GetSymbolInfo(memberAccess.Name, context.CancellationToken).Symbol;

            if (TryLowerStringStaticValueMember(memberSymbol, out term))
            {
                return true;
            }

            return TryLowerBigIntegerStaticValueMember(memberSymbol, out term);
        }
    }
}
