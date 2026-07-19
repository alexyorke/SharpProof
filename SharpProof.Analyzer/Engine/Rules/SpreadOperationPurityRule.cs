namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class SpreadOperationPurityRule : PurityRuleBase<ISpreadOperation>
{
    protected override OperationKind Kind => OperationKind.Spread;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        ISpreadOperation spreadOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
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