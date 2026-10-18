namespace SharpProof.Ir;

internal static class IrProgramCallHostExecution
{
    internal static IrEvaluationResult Execute(
        IrFactory factory,
        IrCallInstruction call,
        Func<IrTerm, IrEvaluationResult> evaluate,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? callHost)
    {
        evaluate = RepositoryParameterGuard.NotNull(evaluate, nameof(evaluate));
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

        var arguments = ImmutableList<IrValue>.Empty;
        foreach (var argument in call.Arguments)
        {
            var argumentResult = evaluate(argument);
            if (argumentResult.Status != IrEvaluationStatus.Value)
            {
                return argumentResult;
            }

            arguments = arguments.Add(argumentResult.Value ?? throw new InvalidOperationException(
                "A value evaluation result must carry a value."));
        }

        if (receiverValue?.Kind == IrValueKind.Null)
        {
            return IrEvaluationResult.FromException(
                IrExceptionKind.NullReference,
                "The call receiver is null.");
        }

        if (call.Target is not { } target ||
            callHost?.Invoke(call, receiverValue, arguments.ToImmutableArray())
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
