using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class EventAssignmentPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.EventAssignment);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not IEventAssignmentOperation eventAssignment)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        // Subscribing or unsubscribing to an event mutates the event's invocation list (stateful) => impure.
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            eventAssignment.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "mutable_state_write",
                nameof(EventAssignmentPurityRule),
                eventAssignment,
                eventAssignment.Syntax,
                (eventAssignment.EventReference as IEventReferenceOperation)?.Event,
                "event_subscription"));
    }
}