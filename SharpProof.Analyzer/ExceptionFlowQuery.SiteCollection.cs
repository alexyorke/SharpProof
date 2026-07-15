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
    private readonly record struct ExceptionSiteCollectionContext(
        SyntaxNode MethodNode,
        SemanticModel SemanticModel,
        CancellationToken CancellationToken,
        IMethodSymbol MethodSymbol,
        SmtAnalysisService SmtAnalysis);

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
        var siteContext = new ExceptionSiteCollectionContext(
            methodNode,
            semanticModel,
            cancellationToken,
            methodSymbol,
            smtAnalysis);

        foreach (var throwNode in ExceptionSiteClassifier.GetThrowNodes(methodNode))
        {
            if (IsInStaticallyUnreachableBranch(throwNode, semanticModel, cancellationToken, smtAnalysis)) continue;

            if (IsShadowedByThrowingFinally(throwNode, semanticModel, cancellationToken, smtAnalysis)) continue;

            var isDefinitelyThrowNull = ExceptionSiteClassifier.IsDefinitelyThrowNull(
                throwNode,
                semanticModel,
                cancellationToken,
                smtAnalysis);
            var exceptionType = isDefinitelyThrowNull
                ? semanticModel.Compilation.GetTypeByMetadataName(ExceptionTypes.NullReferenceException)
                : ExceptionSiteClassifier.GetThrownExceptionType(throwNode, semanticModel, cancellationToken);
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
                     ExceptionSiteClassifier.GetDefiniteDivideByZeroNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.DivideByZeroException,
                     ExceptionCategories.DefiniteDivideByZero,
                     ExceptionSources.BinaryOperator))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteCheckedIntegralOverflowNodes(
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
                     siteContext))
            yield return entry;

        var provenRuntimeHazards = CollectProvenRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            SymbolicRuntimeHazardKind.NegativeStackAllocLength,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
            SymbolicRuntimeHazardKind.InvalidCollectionCardinality,
            SymbolicRuntimeHazardKind.IndexOutOfRange,
            SymbolicRuntimeHazardKind.NullDereference).ToArray();

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.CheckedIntegralOverflow &&
                         ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard) is InvocationExpressionSyntax),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.OverflowException,
                     ExceptionCategories.DefiniteCheckedIntegralOverflow,
                     ExceptionSources.CheckedOperator))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteNegativeArrayLengthNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.OverflowException,
                     ExceptionCategories.DefiniteNegativeArrayLength,
                     ExceptionSources.ArrayLength))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.NegativeStackAllocLength),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.OverflowException,
                     ExceptionCategories.DefiniteNegativeStackAllocLength,
                     ExceptionSources.StackAllocLength))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteNullDereferenceNodes(
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
                     siteContext))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteLockNullNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.ArgumentNullException,
                     ExceptionCategories.DefiniteLockNull,
                     ExceptionSources.LockReceiver))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteDynamicNullBindingSites(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static site => site.Site,
                     static _ => SymbolicDynamicNullBindingFacts.RuntimeBinderExceptionType,
                     static site => site.Category,
                     static site => site.Source,
                     siteContext))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteNullableValueAccessNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.InvalidOperationException,
                     ExceptionCategories.DefiniteNullableValueWithoutValue,
                     ExceptionSources.NullableValue))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteUnboxNullCastNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.NullReferenceException,
                     ExceptionCategories.DefiniteUnboxNull,
                     ExceptionSources.Cast))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteInvalidCastNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.InvalidCastException,
                     ExceptionCategories.DefiniteInvalidCast,
                     ExceptionSources.Cast))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteArrayTypeMismatchStoreNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.ArrayTypeMismatchException,
                     ExceptionCategories.DefiniteArrayTypeMismatch,
                     ExceptionSources.ArrayStore))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteIndexOutOfRangeNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.IndexOutOfRangeException,
                     ExceptionCategories.DefiniteIndexOutOfRange,
                     ExceptionSources.ArrayIndex))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteArrayGetValueIndexOutOfRangeNodes(
                         methodNode,
                         semanticModel,
                         cancellationToken,
                         smtAnalysis),
                     static node => node,
                     siteContext,
                     ExceptionTypes.IndexOutOfRangeException,
                     ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                     ExceptionSources.ArrayGetValue))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     ExceptionSiteClassifier.GetDefiniteArgumentOutOfRangeNodes(
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
                     siteContext))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange &&
                         string.Equals(
                             hazard.Category,
                             ExceptionCategories.DefiniteCountIndexOutOfRange,
                             StringComparison.Ordinal)),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.ArgumentOutOfRangeException,
                     ExceptionCategories.DefiniteCountIndexOutOfRange,
                     ExceptionSources.CountIndex))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.SwitchExpressionNoMatch),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.SwitchExpressionException,
                     ExceptionCategories.DefiniteSwitchExpressionNoMatch,
                     ExceptionSources.SwitchExpression))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.InvalidCollectionCardinality),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.InvalidOperationException,
                     ExceptionCategories.DefiniteInvalidCollectionCardinality,
                     ExceptionSources.CollectionOperation))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         (hazard.Kind is SymbolicRuntimeHazardKind.IndexOutOfRange or
                             SymbolicRuntimeHazardKind.NullDereference) &&
                         IsAnalyzerOnlySymbolicHazardCategory(hazard.Category)),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     static hazard => hazard.ExceptionType,
                     static hazard => hazard.Category,
                     static hazard => GetAnalyzerOnlySymbolicHazardSource(hazard.Category),
                     siteContext))
            yield return entry;
    }

    private static IEnumerable<UncaughtExceptionSiteEntry> CreateProvenExceptionSiteEntries<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, SyntaxNode> getSite,
        ExceptionSiteCollectionContext context,
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
            context);
    }

    private static IEnumerable<UncaughtExceptionSiteEntry> CreateProvenExceptionSiteEntries<TCandidate>(
        IEnumerable<TCandidate> candidates,
        Func<TCandidate, SyntaxNode> getSite,
        Func<TCandidate, string> getExceptionMetadataName,
        Func<TCandidate, string> getCategory,
        Func<TCandidate, string> getSource,
        ExceptionSiteCollectionContext context)
    {
        foreach (var candidate in candidates)
        {
            var entry = TryCreateProvenExceptionSiteEntry(
                getSite(candidate),
                context,
                getExceptionMetadataName(candidate),
                getCategory(candidate),
                getSource(candidate));
            if (entry != null) yield return entry;
        }
    }

    private static UncaughtExceptionSiteEntry? TryCreateProvenExceptionSiteEntry(
        SyntaxNode site,
        ExceptionSiteCollectionContext context,
        string exceptionMetadataName,
        string category,
        string source)
    {
        if (IsInStaticallyUnreachableBranch(
                site,
                context.SemanticModel,
                context.CancellationToken,
                context.SmtAnalysis))
            return null;

        if (IsShadowedByThrowingFinally(
                site,
                context.SemanticModel,
                context.CancellationToken,
                context.SmtAnalysis))
            return null;

        var exceptionType = context.SemanticModel.Compilation.GetTypeByMetadataName(exceptionMetadataName);
        if (IsCaughtWithinMethod(
                site,
                exceptionType,
                context.MethodNode,
                context.SemanticModel,
                context.CancellationToken,
                context.SmtAnalysis))
            return null;

        return new UncaughtExceptionSiteEntry(
            site,
            context.MethodSymbol,
            new ExceptionCandidate(
                exceptionType,
                exceptionMetadataName,
                category,
                source));
    }
}
