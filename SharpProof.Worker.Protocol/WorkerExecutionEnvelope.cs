namespace SharpProof.Worker.Protocol;

public static class WorkerExecutionEnvelope
{
    public const long MaximumProducerElapsedMilliseconds = 922337203685477L;
    public const int CleanupReserveMilliseconds = 100;

    public static long MaximumElapsedMilliseconds(
        WorkerVerifyRequest request,
        int terminationGraceMilliseconds)
    {
        return CalculateMaximumElapsedMilliseconds(
            request,
            terminationGraceMilliseconds,
            validateRequest: true);
    }

    internal static long MaximumElapsedMillisecondsAfterValidation(
        WorkerVerifyRequest request,
        int terminationGraceMilliseconds)
    {
        return CalculateMaximumElapsedMilliseconds(
            request,
            terminationGraceMilliseconds,
            validateRequest: false);
    }

    private static long CalculateMaximumElapsedMilliseconds(
        WorkerVerifyRequest request,
        int terminationGraceMilliseconds,
        bool validateRequest)
    {
        _ = ArgumentNullGuard.NotNull(request, nameof(request));
        if (terminationGraceMilliseconds <= 0 ||
            terminationGraceMilliseconds > WorkerLauncherDefaults.MaximumTerminationGraceMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(terminationGraceMilliseconds));
        }

        if (validateRequest && !WorkerProtocolJson.Validate(request).IsValid)
        {
            throw new ArgumentException("The request authority is invalid.", nameof(request));
        }

        return checked((long)request.Budgets.ProjectWallTimeMilliseconds +
            Math.Max(1, terminationGraceMilliseconds - CleanupReserveMilliseconds));
    }
}
