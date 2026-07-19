using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

internal static class PurityAssignmentStateTransfer
{
    internal static PurityAnalysisState UpdateDelegateMapForOperation(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        operation = operation is IExpressionStatementOperation expressionStatement
            ? expressionStatement.Operation
            : operation;
        if (operation is IAwaitOperation awaitOperation)
            return UpdateDelegateMapForOperation(awaitOperation.Operation, context, currentState);

        var nextState = operation is IInvocationOperation invocation
            ? PurityResourceStateFacts.AddDisposeInvocationFacts(currentState, invocation, currentState)
            : currentState;
        if (PurityAssignmentEnvelope.TryCreate(operation, currentState, context, out var envelope))
            return PurityAssignmentTransition.Apply(envelope, nextState, context);

        if (operation is IReturnOperation returnOperation)
            return PurityResourceStateFacts.AddReturnedOwnedResourceFacts(nextState, returnOperation, currentState);
        if (operation is IUsingOperation usingOperation)
            return PurityResourceStateFacts.AddUsingStatementDisposeFacts(nextState, usingOperation, currentState);
        if (operation is not IFlowCaptureOperation flowCapture) return nextState;

        nextState = nextState.ResetFlowCaptureFacts(flowCapture.Id, flowCapture.Syntax);
        if (TryResolveTrackedSymbol(flowCapture.Value, currentState) is { } capturedSymbol)
            nextState = nextState.WithFlowCaptureSymbol(flowCapture.Id, capturedSymbol);

        var targets = ResolvePotentialTargets(flowCapture.Value, currentState, context.CancellationToken);
        if (targets != null) nextState = nextState.WithFlowCaptureTarget(flowCapture.Id, targets.Value);
        if (PurityConcreteReceiverResolver.TryResolveKnownConcreteType(
                flowCapture.Value,
                currentState,
                context.SemanticModel.Compilation,
                out var concreteType))
            nextState = nextState.WithFlowCaptureConcreteType(
                flowCapture.Id,
                concreteType,
                flowCapture.Syntax);
        if (PurityKnownBclSemantics.IsOwnedLocalArrayValue(
                flowCapture.Value,
                currentState,
                context.SemanticModel.Compilation))
            nextState = nextState.WithOwnedArrayFlowCapture(flowCapture.Id, flowCapture.Syntax);
        return nextState;
    }
}
