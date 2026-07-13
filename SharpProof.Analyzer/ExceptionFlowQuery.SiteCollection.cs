using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionSources = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionSources;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowQuery
{
    private static IEnumerable<UncaughtExceptionSiteEntry> CollectUncaughtExceptionSiteEntries(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        foreach (var throwNode in ExceptionFlowAnalyzer.GetThrowNodes(methodNode))
        {
            if (IsInStaticallyUnreachableBranch(throwNode, semanticModel, cancellationToken, smtAnalysis)) continue;

            if (IsShadowedByThrowingFinally(throwNode, semanticModel, cancellationToken, smtAnalysis)) continue;

            var isDefinitelyThrowNull = ExceptionFlowAnalyzer.IsDefinitelyThrowNull(
                throwNode,
                semanticModel,
                cancellationToken,
                smtAnalysis);
            var exceptionType = isDefinitelyThrowNull
                ? semanticModel.Compilation.GetTypeByMetadataName(ExceptionTypes.NullReferenceException)
                : ExceptionFlowAnalyzer.GetThrownExceptionType(throwNode, semanticModel, cancellationToken);
            if (IsCaughtWithinMethod(throwNode, exceptionType, methodNode, semanticModel, cancellationToken,
                    smtAnalysis)) continue;

            yield return new UncaughtExceptionSiteEntry(
                throwNode,
                methodSymbol,
                new ExceptionCandidate(
                    exceptionType,
                    isDefinitelyThrowNull
                        ? ExceptionTypes.NullReferenceException
                        : exceptionType?.ToDisplayString(ExceptionTypeDisplayFormat) ?? ExceptionTypes.Unknown,
                    isDefinitelyThrowNull
                        ? ExceptionCategories.DefiniteThrowNull
                        : IsRethrow(throwNode)
                            ? ExceptionCategories.Rethrow
                            : ExceptionCategories.DirectThrow,
                    ExceptionSources.Throw));
        }

        foreach (var calleeCallSite in ExceptionFlowAnalyzer.GetCalleeCallSites(methodNode, semanticModel,
                     cancellationToken))
        {
            if (!ExceptionPathStateService.IsMethodCallCandidatePathReachable(calleeCallSite, semanticModel,
                    cancellationToken, smtAnalysis)) continue;

            if (IsShadowedByThrowingFinally(calleeCallSite.CallSite, semanticModel, cancellationToken, smtAnalysis))
                continue;

            var calleeDisplay = calleeCallSite.Method.OriginalDefinition.ToDisplayString();
            if (calleeCallSite.IsDynamicDispatch)
            {
                var dynamicDispatchException = new ExceptionCandidate(
                    null,
                    ExceptionTypes.Unknown,
                    ExceptionCategories.DynamicDispatch,
                    GetExceptionSourceMethodDisplay(calleeCallSite.Method.OriginalDefinition));
                if (!IsCaughtWithinMethod(calleeCallSite.CallSite, dynamicDispatchException.Type, methodNode,
                        semanticModel, cancellationToken, smtAnalysis))
                    yield return new UncaughtExceptionSiteEntry(
                        calleeCallSite.CallSite,
                        calleeCallSite.Method,
                        dynamicDispatchException,
                        calleeDisplay);
            }

            foreach (var exception in CollectCalleeExceptions(
                         calleeCallSite.Method,
                         semanticModel.Compilation,
                         cancellationToken,
                         exceptionSummaryCatalog,
                         visitedMethods,
                         smtAnalysis,
                         attributePolicy))
            {
                if (IsCaughtWithinMethod(calleeCallSite.CallSite, exception.Type, methodNode, semanticModel,
                        cancellationToken, smtAnalysis)) continue;

                yield return new UncaughtExceptionSiteEntry(calleeCallSite.CallSite, calleeCallSite.Method, exception,
                    calleeDisplay);
            }
        }

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteDivideByZeroNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.DivideByZeroException,
                     ExceptionCategories.DefiniteDivideByZero,
                     ExceptionSources.BinaryOperator))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteCheckedIntegralOverflowNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     static _ => ExceptionTypes.OverflowException,
                     static _ => ExceptionCategories.DefiniteCheckedIntegralOverflow,
                     static node => node is CastExpressionSyntax
                         ? ExceptionSources.CheckedConversion
                         : ExceptionSources.CheckedOperator,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     CollectProvenInvocationCheckedIntegralOverflowHazards(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.OverflowException,
                     ExceptionCategories.DefiniteCheckedIntegralOverflow,
                     ExceptionSources.CheckedOperator))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteNegativeArrayLengthNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.OverflowException,
                     ExceptionCategories.DefiniteNegativeArrayLength,
                     ExceptionSources.ArrayLength))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     CollectProvenNegativeStackAllocLengthHazards(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.OverflowException,
                     ExceptionCategories.DefiniteNegativeStackAllocLength,
                     ExceptionSources.StackAllocLength))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteNullDereferenceNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     static _ => ExceptionTypes.NullReferenceException,
                     static node => node is AwaitExpressionSyntax
                         ? ExceptionCategories.DefiniteAwaitNull
                         : ExceptionCategories.DefiniteNullDereference,
                     static node => node is AwaitExpressionSyntax
                         ? ExceptionSources.AwaitExpression
                         : ExceptionSources.NullReceiver,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteLockNullNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.ArgumentNullException,
                     ExceptionCategories.DefiniteLockNull,
                     ExceptionSources.LockReceiver))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteDynamicNullBindingSites(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static site => site.Site,
                     static _ => SymbolicDynamicNullBindingFacts.RuntimeBinderExceptionType,
                     static site => site.Category,
                     static site => site.Source,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteNullableValueAccessNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.InvalidOperationException,
                     ExceptionCategories.DefiniteNullableValueWithoutValue,
                     ExceptionSources.NullableValue))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteUnboxNullCastNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.NullReferenceException,
                     ExceptionCategories.DefiniteUnboxNull,
                     ExceptionSources.Cast))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteInvalidCastNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.InvalidCastException,
                     ExceptionCategories.DefiniteInvalidCast,
                     ExceptionSources.Cast))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteArrayTypeMismatchStoreNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.ArrayTypeMismatchException,
                     ExceptionCategories.DefiniteArrayTypeMismatch,
                     ExceptionSources.ArrayStore))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteIndexOutOfRangeNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.IndexOutOfRangeException,
                     ExceptionCategories.DefiniteIndexOutOfRange,
                     ExceptionSources.ArrayIndex))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteArrayGetValueIndexOutOfRangeNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.IndexOutOfRangeException,
                     ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                     ExceptionSources.ArrayGetValue))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionFlowAnalyzer.GetDefiniteArgumentOutOfRangeNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     static _ => ExceptionTypes.ArgumentOutOfRangeException,
                     static _ => ExceptionCategories.DefiniteRangeOutOfRange,
                     static node => node is InvocationExpressionSyntax
                         ? ExceptionSources.SpanSlice
                         : ExceptionSources.RangeSlice,
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     CollectProvenCountIndexOutOfRangeHazards(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.ArgumentOutOfRangeException,
                     ExceptionCategories.DefiniteCountIndexOutOfRange,
                     ExceptionSources.CountIndex))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     CollectProvenSwitchExpressionNoMatchHazards(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.SwitchExpressionException,
                     ExceptionCategories.DefiniteSwitchExpressionNoMatch,
                     ExceptionSources.SwitchExpression))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     CollectProvenInvalidCollectionCardinalityHazards(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis,
                     ExceptionTypes.InvalidOperationException,
                     ExceptionCategories.DefiniteInvalidCollectionCardinality,
                     ExceptionSources.CollectionOperation))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     CollectProvenAnalyzerOnlySymbolicHazards(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     static hazard => hazard.ExceptionType,
                     static hazard => hazard.Category,
                     static hazard => GetAnalyzerOnlySymbolicHazardSource(hazard.Category),
                     methodNode,
                     semanticModel,
                     cancellationToken,
                     methodSymbol,
                     smtAnalysis))
            yield return entry;
    }

    private static IEnumerable<UncaughtExceptionSiteEntry> CreateProvenExceptionSiteEntries<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, SyntaxNode> getSite,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        SmtAnalysisService smtAnalysis,
        string exceptionMetadataName,
        string category,
        string source)
    {
        return CreateProvenExceptionSiteEntries(
            candidates,
            getSite,
            _ => exceptionMetadataName,
            _ => category,
            _ => source,
            methodNode,
            semanticModel,
            cancellationToken,
            methodSymbol,
            smtAnalysis);
    }

    private static IEnumerable<UncaughtExceptionSiteEntry> CreateProvenExceptionSiteEntries<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, SyntaxNode> getSite,
        Func<TCandidate, string> getExceptionMetadataName,
        Func<TCandidate, string> getCategory,
        Func<TCandidate, string> getSource,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var candidate in candidates)
        {
            var entry = TryCreateProvenExceptionSiteEntry(
                getSite(candidate),
                methodNode,
                semanticModel,
                cancellationToken,
                methodSymbol,
                smtAnalysis,
                getExceptionMetadataName(candidate),
                getCategory(candidate),
                getSource(candidate));
            if (entry != null) yield return entry;
        }
    }

    private static UncaughtExceptionSiteEntry? TryCreateProvenExceptionSiteEntry(
        SyntaxNode site,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        SmtAnalysisService smtAnalysis,
        string exceptionMetadataName,
        string category,
        string source)
    {
        if (IsInStaticallyUnreachableBranch(site, semanticModel, cancellationToken, smtAnalysis)) return null;

        if (IsShadowedByThrowingFinally(site, semanticModel, cancellationToken, smtAnalysis)) return null;

        var exceptionType = semanticModel.Compilation.GetTypeByMetadataName(exceptionMetadataName);
        if (IsCaughtWithinMethod(site, exceptionType, methodNode, semanticModel, cancellationToken, smtAnalysis))
            return null;

        return new UncaughtExceptionSiteEntry(
            site,
            methodSymbol,
            new ExceptionCandidate(
                exceptionType,
                exceptionMetadataName,
                category,
                source));
    }
}
