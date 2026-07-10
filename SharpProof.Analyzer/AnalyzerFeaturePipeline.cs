using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static class AnalyzerFeaturePipeline
{
    internal static void AnalyzeCallable(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        using (session.GeneratedPurityCatalog == null
                   ? null
                   : GeneratedPurityCatalog.UseCurrent(session.GeneratedPurityCatalog))
        using (ImpurityCatalog.UseConfiguredOverrides(session.Configuration))
        using (SymbolicAnalysisLimitContext.Push(session.Configuration.AnalysisLimits, context.Node))
        {
            var features = session.Features;
            if (features.Includes(AnalyzerFeatures.Purity))
                MethodPurityAnalyzer.AnalyzeSymbolForPurity(
                    context,
                    session.PurityService,
                    session.Configuration.MissingPuritySuggestions,
                    session.Configuration.EmitExplanations,
                    session.Configuration.ReportBclFallbackGuesses,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Allocation))
                MethodAllocationAnalyzer.AnalyzeSymbolForZeroAllocations(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Capability))
                MethodCapabilityAnalyzer.AnalyzeSymbolForCapabilities(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Requires))
                MethodRequiresAnalyzer.AnalyzeSymbolForRequires(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Ensures))
                MethodEnsuresAnalyzer.AnalyzeSymbolForEnsures(
                    context,
                    session.PurityService,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Complexity))
                MethodExpectedComplexityAnalyzer.AnalyzeSymbolForExpectedComplexity(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Exceptions))
                ExceptionFlowAnalyzer.AnalyzeSymbolForExceptions(
                    context,
                    session.Configuration,
                    session.ExceptionSummaryCatalog,
                    session.PurityService,
                    session.Baseline,
                    session.AttributePolicy);
        }
    }
}
