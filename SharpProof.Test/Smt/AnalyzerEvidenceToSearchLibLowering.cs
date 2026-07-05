using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Purity;
using SearchLib.Smt;

namespace SharpProof.Test.Smt
{
    internal enum AnalyzerPurityHazardKind
    {
        ImpureCallReachability,
        NullDereference,
        DivideByZero,
    }

    internal sealed record AnalyzerPurityEvidence(
        AnalyzerPurityHazardKind Kind,
        IReadOnlyList<ExpressionSyntax> PathConditions,
        ExpressionSyntax TriggerCondition);

    internal static class AnalyzerEvidenceToSearchLibLowering
    {
        public static bool TryLower(
            AnalyzerPurityEvidence evidence,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out PurityProofQuery? query)
        {
            var pathConditions = new List<SmtFormula>(evidence.PathConditions.Count);
            foreach (var pathCondition in evidence.PathConditions)
            {
                if (!CSharpConditionToFormula.TryTranslate(pathCondition, semanticModel, cancellationToken, out var pathFormula))
                {
                    query = null;
                    return false;
                }

                pathConditions.Add(pathFormula);
            }

            if (!CSharpConditionToFormula.TryTranslate(evidence.TriggerCondition, semanticModel, cancellationToken, out var triggerFormula))
            {
                query = null;
                return false;
            }

            query = new PurityProofQuery(
                pathConditions,
                new PurityHazard(MapKind(evidence.Kind), triggerFormula));
            return true;
        }

        private static PurityHazardKind MapKind(AnalyzerPurityHazardKind kind)
        {
            return kind switch
            {
                AnalyzerPurityHazardKind.ImpureCallReachability => PurityHazardKind.ImpureCallReachability,
                AnalyzerPurityHazardKind.NullDereference => PurityHazardKind.NullDereference,
                AnalyzerPurityHazardKind.DivideByZero => PurityHazardKind.DivideByZero,
                _ => throw new InvalidOperationException("Unsupported analyzer hazard kind."),
            };
        }
    }
}
