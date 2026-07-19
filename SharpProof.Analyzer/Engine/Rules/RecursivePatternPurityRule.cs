namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class RecursivePatternPurityRule : PurityRuleBase<IRecursivePatternOperation>
{
    protected override OperationKind Kind => OperationKind.RecursivePattern;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        IRecursivePatternOperation recursivePatternOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (recursivePatternOperation.DeconstructSymbol is IMethodSymbol deconstructMethod)
        {
            var deconstructResult = PurityCalleeResolver.GetCanonicalCalleePurityAtUse(
                deconstructMethod,
                recursivePatternOperation.Syntax,
                context);
            if (!deconstructResult.IsPure)
                return deconstructResult;
        }

        return ChildOperationsPurityRule.CheckChildOperationsArePure(recursivePatternOperation, context, currentState);
    }
}
