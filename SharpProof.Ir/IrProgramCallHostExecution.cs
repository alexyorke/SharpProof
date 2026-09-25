namespace SharpProof.Ir;

internal static class IrProgramCallHostExecution
{
    internal static IrEvaluationResult Execute(
        IrFactory factory,
        IrInterpreter terms,
        IrCallInstruction call,
        IReadOnlyDictionary<IrVarId, IrValue> values,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? callHost,
        CancellationToken cancellationToken)
    {
        IrValue? receiverValue = null;
        if (call.Receiver is { } receiver)
        {
            var receiverResult = terms.Evaluate(
                receiver, values, cancellationToken);
            if (receiverResult.Status != IrEvaluationStatus.Value)
            {
                return receiverResult;
            }

            receiverValue = receiverResult.Value;
        }

        var arguments = ImmutableArray.CreateBuilder<IrValue>(
            call.Arguments.Length);
        foreach (var argument in call.Arguments)
        {
            var argumentResult = terms.Evaluate(
                argument, values, cancellationToken);
            if (argumentResult.Status != IrEvaluationStatus.Value)
            {
                return argumentResult;
            }

            arguments.Add(argumentResult.Value!);
        }

        if (receiverValue?.Kind == IrValueKind.Null)
        {
            return IrEvaluationResult.FromException(
                IrExceptionKind.NullReference,
                "The call receiver is null.");
        }

        if (call.Target is not { } target ||
            callHost?.Invoke(call, receiverValue, arguments.ToImmutable())
                is not { } callResult ||
            callResult.Type != factory.GetVariableInfo(target).Type)
        {
            return IrEvaluationResult.FromUnsupported(
                IrUnsupportedReason.UnsupportedOperation,
                "Concrete execution requires a supported call host.");
        }

        return IrEvaluationResult.FromValue(callResult);
    }
}
