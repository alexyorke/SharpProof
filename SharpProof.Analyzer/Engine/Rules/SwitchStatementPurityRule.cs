using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class SwitchStatementPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Switch);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is ISwitchOperation switchOperation)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        var valueResult = PurityAnalysisEngine.CheckSingleOperation(switchOperation.Value, context, currentState);
        if (!valueResult.IsPure) return valueResult;


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}