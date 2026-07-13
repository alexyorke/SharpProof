using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static PurityAnalysisEngine.PurityAnalysisResult CheckResolvedEqualityImplementation(
        IMethodSymbol implementation,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        var implementationPurity = PurityCalleeResolver.GetCalleePurity(implementation.OriginalDefinition, context);
        return implementationPurity.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : implementationPurity.WithCallee(implementation.OriginalDefinition, invocationOperation.Syntax);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CreateUnknownExternalCallImpurity(
        IInvocationOperation invocationOperation,
        ISymbol? symbol = null)
    {
        return PurityAnalysisEngine.ImpureResult(
            invocationOperation,
            "unknown_external_call",
            nameof(MethodInvocationPurityRule),
            symbol ?? invocationOperation.TargetMethod);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultHashDispatchPurity(
        ITypeSymbol elementType,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(elementType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(GetHashCode), 0,
                out var getHashCodeOverride)) return CreateUnknownExternalCallImpurity(invocationOperation);

        return CheckResolvedEqualityImplementation(
            getHashCodeOverride,
            invocationOperation,
            context);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultEqualityDispatchPurity(
        ITypeSymbol elementType,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        bool requiresHashCode = false)
    {
        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(elementType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (requiresHashCode)
        {
            if (!DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(GetHashCode), 0,
                    out var getHashCodeOverride)) return CreateUnknownExternalCallImpurity(invocationOperation);

            var hashPurity = CheckResolvedEqualityImplementation(
                getHashCodeOverride,
                invocationOperation,
                context);
            if (!hashPurity.IsPure) return hashPurity;
        }

        if (DispatchedMemberResolution.TryGetIEquatableEqualsImplementation(elementType, out var equalsImplementation))
            return CheckResolvedEqualityImplementation(
                equalsImplementation,
                invocationOperation,
                context);

        if (DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(object.Equals), 1,
                out var objectEqualsOverride))
            return CheckResolvedEqualityImplementation(
                objectEqualsOverride,
                invocationOperation,
                context);

        if (elementType is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true })
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return CreateUnknownExternalCallImpurity(invocationOperation);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultComparisonDispatchPurity(
        ITypeSymbol keyType,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(keyType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (DispatchedMemberResolution.TryGetIComparableCompareToImplementation(keyType,
                out var compareToImplementation))
            return CheckResolvedEqualityImplementation(
                compareToImplementation,
                invocationOperation,
                context);

        if (DispatchedMemberResolution.TryGetIComparableObjectCompareToImplementation(keyType,
                out var objectCompareToImplementation))
            return CheckResolvedEqualityImplementation(
                objectCompareToImplementation,
                invocationOperation,
                context);

        return CreateUnknownExternalCallImpurity(invocationOperation);
    }
}
