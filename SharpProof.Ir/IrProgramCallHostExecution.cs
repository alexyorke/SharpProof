namespace SharpProof.Ir;

internal static class IrProgramCallHostExecution
{
    internal static IrEvaluationResult Execute(
        IrFactory factory,
        IrCallInstruction call,
        Func<IrTerm, IrEvaluationResult> evaluate,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? callHost)
    {
        IrValue? receiverValue = null;
        if (call.Receiver is { } receiver)
        {
            var receiverResult = evaluate(receiver);
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
            var argumentResult = evaluate(argument);
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
