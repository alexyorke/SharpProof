namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine
{
    private static IEnumerable<ExceptionFlowSite> CollectUncaughtExceptionSiteEntries(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        EffectSummaryCatalog exceptionSummaryCatalog,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        ImmutableArray<SymbolicRuntimeHazard> runtimeHazards)
    {
        var assessment = new ExceptionSiteAssessment(methodNode, semanticModel, cancellationToken, smtAnalysis);
        var provenRuntimeHazardSites = ProjectProvenRuntimeHazardSites(methodNode, runtimeHazards);

        foreach (var entry in CreateProvenExceptionSiteEntries(
                     provenRuntimeHazardSites.Where(static site => site.BeforeCallees),
                     assessment,
                     semanticModel,
                     methodSymbol))
            yield return entry;

        foreach (var calleeCallSite in ExceptionFlowAnalyzer.GetCalleeCallSites(methodNode, semanticModel,
                     cancellationToken))
        {
            if (assessment.Assess(
                    calleeCallSite.CallSite,
                    calleeCallSite.UsingDisposeGuard,
                    static () => null,
                    out _) is not ExceptionSiteDisposition.Escapes)
                continue;

            var calleeDisplay = calleeCallSite.Method.OriginalDefinition.ToDisplayString();
            if (calleeCallSite.IsDynamicDispatch)
            {
                if (assessment.Assess(
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
                        calleeDisplay,
                        ImmutableArray<ExceptionFlowEdge>.Empty);
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
                if (assessment.Assess(
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
                     assessment,
                     semanticModel,
                     methodSymbol))
            yield return entry;
    }

    private static IEnumerable<ExceptionFlowSite> CreateProvenExceptionSiteEntries(
        IEnumerable<ProvenRuntimeHazardSite> sites,
        ExceptionSiteAssessment assessment,
        SemanticModel semanticModel,
        IMethodSymbol methodSymbol)
    {
        foreach (var site in sites)
        {
            if (assessment.Assess(
                    site.Site,
                    null,
                    () => semanticModel.Compilation.GetTypeByMetadataName(site.Hazard.ExceptionType),
                    out var exceptionType) != ExceptionSiteDisposition.Escapes)
                continue;
            yield return new ExceptionFlowSite(
                site.Site,
                methodSymbol,
                exceptionType,
                site.Hazard.ExceptionType,
                site.Category,
                site.Source,
                null,
                ImmutableArray<ExceptionFlowEdge>.Empty);
        }
    }
}
