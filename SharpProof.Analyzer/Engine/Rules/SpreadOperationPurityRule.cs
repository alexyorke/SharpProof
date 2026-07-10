using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class SpreadOperationPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Spread);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not ISpreadOperation spreadOperation) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (spreadOperation.Operand == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var operandResult = PurityAnalysisEngine.CheckSingleOperation(spreadOperation.Operand, context, currentState);
        if (!operandResult.IsPure)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                operandResult.ImpureSyntaxNode ?? spreadOperation.Syntax,
                operandResult.Evidence);

        var enumerationResult = LoopPurityRule.CheckForEachEnumeratorPurity(spreadOperation.Operand, context);
        if (!enumerationResult.IsPure)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                enumerationResult.ImpureSyntaxNode ?? spreadOperation.Syntax,
                enumerationResult.Evidence);

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}