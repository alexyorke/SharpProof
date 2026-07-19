namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class DeclarativePureOperationRule : IPurityRule
{
    private readonly ImmutableArray<OperationKind> _applicableOperationKinds;

    public DeclarativePureOperationRule(IEnumerable<OperationKind> operationKinds)
    {
        _applicableOperationKinds = operationKinds.ToImmutableArray();
    }

    public IEnumerable<OperationKind> ApplicableOperationKinds => _applicableOperationKinds;

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

}
