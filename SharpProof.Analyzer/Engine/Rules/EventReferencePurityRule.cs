namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class EventReferencePurityRule : PurityRuleBase<IEventReferenceOperation>
{
    protected override OperationKind Kind => OperationKind.EventReference;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IEventReferenceOperation eventReference,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        return PurityAnalysisEngine.ImpureResult(
            eventReference,
            "mutable_state_read",
            nameof(EventReferencePurityRule),
            eventReference.Event);
    }
}
