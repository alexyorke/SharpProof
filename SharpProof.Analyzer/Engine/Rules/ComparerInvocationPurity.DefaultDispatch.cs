namespace SharpProof.Analyzer.Engine.Rules;

internal static partial class ComparerInvocationPurity
{
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

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultHashDispatchPurity(
        ITypeSymbol elementType,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(elementType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return CheckDefaultGetHashCodeDispatchPurity(elementType, invocationOperation, context);
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultEqualityDispatchPurity(
        ITypeSymbol elementType,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        bool requiresHashCode = false)
    {
        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(elementType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (requiresHashCode)
        {
            var hashPurity = CheckDefaultGetHashCodeDispatchPurity(elementType, invocationOperation, context);
            if (!hashPurity.IsPure) return hashPurity;
        }

        if (DispatchedMemberResolution.TryGetIEquatableEqualsImplementation(elementType, out var equalsImplementation))
            return PurityCalleeResolver.GetCanonicalCalleePurityAtUse(
                equalsImplementation,
                invocationOperation.Syntax,
                context);

        if (DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(object.Equals), 1,
                out var objectEqualsOverride))
            return PurityCalleeResolver.GetCanonicalCalleePurityAtUse(
                objectEqualsOverride,
                invocationOperation.Syntax,
                context);

        if (elementType is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true })
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return CreateUnknownExternalCallImpurity(invocationOperation);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultGetHashCodeDispatchPurity(
        ITypeSymbol elementType, IInvocationOperation invocationOperation, PurityAnalysisContext context)
    {
        return DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(GetHashCode), 0,
            out var getHashCodeOverride)
            ? PurityCalleeResolver.GetCanonicalCalleePurityAtUse(
                getHashCodeOverride, invocationOperation.Syntax, context)
            : CreateUnknownExternalCallImpurity(invocationOperation);
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultComparisonDispatchPurity(
        ITypeSymbol keyType,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        return ComparerDispatchHelper.CheckDefaultComparisonPurity(
            keyType,
            invocationOperation.Syntax,
            context,
            () => CreateUnknownExternalCallImpurity(invocationOperation));
    }
}
