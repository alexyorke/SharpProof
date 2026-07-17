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
        SemanticModel SemanticModel,
        IMethodSymbol MethodSymbol,
        ExceptionSiteAssessment Assessment);

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
            semanticModel,
            methodSymbol,
            new ExceptionSiteAssessment(methodNode, semanticModel, cancellationToken, smtAnalysis));
        var provenRuntimeHazardSites = ProjectProvenRuntimeHazardSites(methodNode, runtimeHazards);

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazardSites.Where(static site => site.BeforeCallees),
                     siteContext))
            yield return entry;

        foreach (var calleeCallSite in ExceptionFlowAnalyzer.GetCalleeCallSites(methodNode, semanticModel,
                     cancellationToken))
        {
            if (siteContext.Assessment.Assess(
                    calleeCallSite.CallSite,
                    calleeCallSite.UsingDisposeGuard,
                    static () => null,
                    out _) is not ExceptionSiteDisposition.Escapes)
                continue;

            var calleeDisplay = calleeCallSite.Method.OriginalDefinition.ToDisplayString();
            if (calleeCallSite.IsDynamicDispatch)
            {
                if (siteContext.Assessment.Assess(
                        calleeCallSite.CallSite,
                        calleeCallSite.UsingDisposeGuard,
                        static () => null,
                        out _) == ExceptionSiteDisposition.Escapes)
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
                if (siteContext.Assessment.Assess(
                        calleeCallSite.CallSite,
                        calleeCallSite.UsingDisposeGuard,
                        () => exception.Type,
                        out _) != ExceptionSiteDisposition.Escapes)
                    continue;

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
        if (context.Assessment.Assess(
                site,
                null,
                () => context.SemanticModel.Compilation.GetTypeByMetadataName(exceptionMetadataName),
                out var exceptionType) != ExceptionSiteDisposition.Escapes)
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
