namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class EventAssignmentPurityRule : PurityRuleBase<IEventAssignmentOperation>
{
    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IEventAssignmentOperation eventAssignment,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
    {


        // Subscribing or unsubscribing to an event mutates the event's invocation list (stateful) => impure.
        return PurityAnalysisEngine.ImpureResult(
            eventAssignment,
            "mutable_state_write",
            nameof(EventAssignmentPurityRule),
            (eventAssignment.EventReference as IEventReferenceOperation)?.Event,
            "event_subscription");
    }
}
