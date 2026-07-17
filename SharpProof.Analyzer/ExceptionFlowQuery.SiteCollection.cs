using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine
{
    private readonly record struct ExceptionSiteCollectionContext(
        SyntaxNode MethodNode,
        SemanticModel SemanticModel,
        CancellationToken CancellationToken,
        IMethodSymbol MethodSymbol,
        SmtAnalysisService SmtAnalysis);

    private static IEnumerable<ExceptionFlowSite> CollectUncaughtExceptionSiteEntries(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        ImmutableArray<SymbolicRuntimeHazard> runtimeHazards)
    {
        var siteContext = new ExceptionSiteCollectionContext(
            methodNode,
            semanticModel,
            cancellationToken,
            methodSymbol,
            smtAnalysis);
        var provenRuntimeHazardSites = ProjectProvenRuntimeHazardSites(methodNode, runtimeHazards);

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazardSites.Where(static site => site.BeforeCallees),
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
                if (!IsCaughtWithinMethod(calleeCallSite.CallSite, null, methodNode,
                        semanticModel, cancellationToken, smtAnalysis))
                    yield return new ExceptionFlowSite(
                        calleeCallSite.CallSite,
                        calleeCallSite.Method,
                        null,
                        ExceptionTypes.Unknown,
                        ExceptionCategories.DynamicDispatch,
                        GetExceptionSourceMethodDisplay(calleeCallSite.Method.OriginalDefinition),
                        calleeDisplay);
            }

            foreach (var exception in CollectCalleeExceptionSites(
                         calleeCallSite,
                         semanticModel.Compilation,
                         cancellationToken,
                         exceptionSummaryCatalog,
                         visitedMethods,
                         smtAnalysis,
                         attributePolicy))
            {
                if (IsCaughtWithinMethod(calleeCallSite.CallSite, exception.Type, methodNode, semanticModel,
                        cancellationToken, smtAnalysis)) continue;

                yield return exception;
            }
        }

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazardSites.Where(static site => !site.BeforeCallees),
                     siteContext))
            yield return entry;
    }

    private static IEnumerable<ExceptionFlowSite> CreateProvenExceptionSiteEntries(
        IEnumerable<ProvenRuntimeHazardSite> sites,
        ExceptionSiteCollectionContext context)
    {
        foreach (var site in sites)
        {
            var entry = TryCreateProvenExceptionSiteEntry(
                site.Site,
                context,
                site.Hazard.ExceptionType,
                site.Category,
                site.Source);
            if (entry != null) yield return entry;
        }
    }

    private static ExceptionFlowSite? TryCreateProvenExceptionSiteEntry(
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

        return new ExceptionFlowSite(
            site,
            context.MethodSymbol,
            exceptionType,
            exceptionMetadataName,
            category,
            source);
    }
}
