using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Analysis;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Test.Smt;

internal enum AnalyzerAnalysisHazardKind {
    EffectViolationReachability,
    NullDereference,
    DivideByZero
}
internal sealed record AnalyzerPurityEvidence(AnalyzerAnalysisHazardKind Kind, IReadOnlyList<ExpressionSyntax> PathConditions,
    ExpressionSyntax TriggerCondition);

internal static class AnalyzerEvidenceToProofCoreLowering {
    public static bool TryLower(AnalyzerPurityEvidence evidence, SemanticModel semanticModel, CancellationToken cancellationToken,
        out AnalysisProofQuery? query) {
        var pathConditions = new List<SmtFormula>(evidence.PathConditions.Count);
        foreach (var pathCondition in evidence.PathConditions) {
            if (!CSharpConditionToFormula.TryTranslate(pathCondition, semanticModel, cancellationToken, out var pathFormula)) {
                query = null;
                return false;
            }
            pathConditions.Add(pathFormula);
        }
        if (!CSharpConditionToFormula.TryTranslate(evidence.TriggerCondition, semanticModel, cancellationToken, out var triggerFormula)) {
            query = null;
            return false;
        }
        query = new AnalysisProofQuery(pathConditions, new AnalysisHazard(MapKind(evidence.Kind), triggerFormula));
        return true;
    }
    private static AnalysisHazardKind MapKind(AnalyzerAnalysisHazardKind kind) => kind switch {
        AnalyzerAnalysisHazardKind.EffectViolationReachability => AnalysisHazardKind.EffectViolationReachability,
        AnalyzerAnalysisHazardKind.NullDereference => AnalysisHazardKind.NullDereference,
        AnalyzerAnalysisHazardKind.DivideByZero => AnalysisHazardKind.DivideByZero,
        _ => throw new InvalidOperationException("Unsupported analyzer hazard kind.")
    };
}
