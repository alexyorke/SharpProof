namespace SharpProof.Fuzz;

internal static class FuzzExecutionDefaults
{
    // Replay is a bounded independent oracle, separate from solver resources.
    internal const int MaximumReplaySteps = 10_000;
}
