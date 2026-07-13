using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class EventReferencePurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.EventReference);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not IEventReferenceOperation eventReference)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        return PurityAnalysisEngine.ImpureResult(
            eventReference,
            "mutable_state_read",
            nameof(EventReferencePurityRule),
            eventReference.Event);
    }
}
