using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal interface IPurityRule
{
    IEnumerable<OperationKind> ApplicableOperationKinds { get; }

    PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState);
}