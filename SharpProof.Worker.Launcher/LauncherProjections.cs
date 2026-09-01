using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static partial class LauncherPresentation
{
    // Preserve the launcher's distinct containment-failure exit code when a
    // valid protocol response reports that the worker could not be contained.
    internal static int ExitCode(
        WorkerRunStatus status,
        WorkerRunFailureReason reason)
    {
        return reason == WorkerRunFailureReason.ContainmentFailure
            ? 125
            : ExitCode(status);
    }
}
