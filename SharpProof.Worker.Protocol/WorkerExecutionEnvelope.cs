namespace SharpProof.Worker.Protocol;

public static class WorkerExecutionEnvelope
{
    public const long MaximumProducerElapsedMilliseconds = 922337203685477L;
    public const int CleanupReserveMilliseconds = 100;

    public static long MaximumElapsedMilliseconds(
        WorkerVerifyRequest request,
        int terminationGraceMilliseconds)
    {
        _ = request ?? throw new ArgumentNullException(nameof(request));
        if (terminationGraceMilliseconds <= CleanupReserveMilliseconds ||
            terminationGraceMilliseconds > WorkerLauncherDefaults.MaximumTerminationGraceMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(terminationGraceMilliseconds));
        }

        if (!WorkerProtocolJson.Validate(request).IsValid)
        {
            throw new ArgumentException("The request authority is invalid.", nameof(request));
        }

        return checked((long)request.Budgets.ProjectWallTimeMilliseconds +
            Math.Max(1, terminationGraceMilliseconds - CleanupReserveMilliseconds));
    }
}
