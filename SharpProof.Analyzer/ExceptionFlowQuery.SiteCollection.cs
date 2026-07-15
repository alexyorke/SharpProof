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
        var provenRuntimeHazards = CollectProvenRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            SymbolicRuntimeHazardKind.DirectThrow,
            SymbolicRuntimeHazardKind.Rethrow,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            SymbolicRuntimeHazardKind.DivideByZero,
            SymbolicRuntimeHazardKind.NegativeArrayLength,
            SymbolicRuntimeHazardKind.NegativeStackAllocLength,
            SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            SymbolicRuntimeHazardKind.UnboxNull,
            SymbolicRuntimeHazardKind.InvalidCast,
            SymbolicRuntimeHazardKind.ArrayTypeMismatch,
            SymbolicRuntimeHazardKind.ArgumentNull,
            SymbolicRuntimeHazardKind.DynamicNullBinding,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
            SymbolicRuntimeHazardKind.InvalidCollectionCardinality,
            SymbolicRuntimeHazardKind.IndexOutOfRange,
            SymbolicRuntimeHazardKind.NullDereference).ToArray();

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind is SymbolicRuntimeHazardKind.DirectThrow or SymbolicRuntimeHazardKind.Rethrow),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     static hazard => hazard.ExceptionType,
                     static hazard => hazard.Category,
                     static _ => ExceptionSources.Throw,
                     siteContext))
            yield return entry;

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
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.DivideByZero),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.DivideByZeroException,
                     ExceptionCategories.DefiniteDivideByZero,
                     ExceptionSources.BinaryOperator))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.CheckedIntegralOverflow),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     static _ => ExceptionTypes.OverflowException,
                     static _ => ExceptionCategories.DefiniteCheckedIntegralOverflow,
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard) is CastExpressionSyntax
                         ? ExceptionSources.CheckedConversion
                         : ExceptionSources.CheckedOperator,
                     siteContext))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.NegativeArrayLength),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
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
                     provenRuntimeHazards.Where(hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.NullDereference &&
                         ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard) is
                             MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or
                             InvocationExpressionSyntax or AwaitExpressionSyntax),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     static _ => ExceptionTypes.NullReferenceException,
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard) is AwaitExpressionSyntax
                         ? ExceptionCategories.DefiniteAwaitNull
                         : ExceptionCategories.DefiniteNullDereference,
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard) is AwaitExpressionSyntax
                         ? ExceptionSources.AwaitExpression
                         : ExceptionSources.NullReceiver,
                     siteContext))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.ArgumentNull &&
                         string.Equals(hazard.Category, ExceptionCategories.DefiniteLockNull,
                             StringComparison.Ordinal)),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.ArgumentNullException,
                     ExceptionCategories.DefiniteLockNull,
                     ExceptionSources.LockReceiver))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.DynamicNullBinding),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     static hazard => hazard.ExceptionType,
                     static hazard => hazard.Category,
                     static hazard => GetDynamicNullBindingHazardSource(hazard.Category),
                     siteContext))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.NullableValueWithoutValue),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.InvalidOperationException,
                     ExceptionCategories.DefiniteNullableValueWithoutValue,
                     ExceptionSources.NullableValue))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.UnboxNull),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.NullReferenceException,
                     ExceptionCategories.DefiniteUnboxNull,
                     ExceptionSources.Cast))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.InvalidCast),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.InvalidCastException,
                     ExceptionCategories.DefiniteInvalidCast,
                     ExceptionSources.Cast))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.ArrayTypeMismatch),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.ArrayTypeMismatchException,
                     ExceptionCategories.DefiniteArrayTypeMismatch,
                     ExceptionSources.ArrayStore))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.IndexOutOfRange &&
                         string.Equals(hazard.Category, ExceptionCategories.DefiniteIndexOutOfRange,
                             StringComparison.Ordinal)),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.IndexOutOfRangeException,
                     ExceptionCategories.DefiniteIndexOutOfRange,
                     ExceptionSources.ArrayIndex))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.IndexOutOfRange &&
                         string.Equals(hazard.Category, ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                             StringComparison.Ordinal)),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     siteContext,
                     ExceptionTypes.IndexOutOfRangeException,
                     ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                     ExceptionSources.ArrayGetValue))
            yield return entry;

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazards.Where(static hazard =>
                         hazard.Kind == SymbolicRuntimeHazardKind.ArgumentOutOfRange &&
                         (string.Equals(hazard.Category, ExceptionCategories.DefiniteRangeOutOfRange,
                              StringComparison.Ordinal) ||
                          string.Equals(hazard.Category, ExceptionCategories.DefiniteSliceOutOfRange,
                              StringComparison.Ordinal))),
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard),
                     static _ => ExceptionTypes.ArgumentOutOfRangeException,
                     static _ => ExceptionCategories.DefiniteRangeOutOfRange,
                     hazard => ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard) is InvocationExpressionSyntax
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
