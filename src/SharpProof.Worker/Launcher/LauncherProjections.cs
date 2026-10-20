using System.Collections.Immutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static partial class LauncherPresentation
{
    internal static string AssumptionsDeclaredMessage(
        string callableId, ImmutableArray<WorkerAssumptionEvidence> assumptions)
    {
        System.ArgumentNullException.ThrowIfNull(callableId);
        RepositoryParameterGuard.ThrowIfMissing(assumptions.IsDefault, nameof(assumptions));
        var user = assumptions
            .Where(static assumption =>
                assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.UserAssume))
            .Select(static assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id))
            .ToImmutableArray();
        var trusted = assumptions
            .Where(static assumption =>
                assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.TrustedBoundary))
            .Select(static assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id))
            .ToImmutableArray();
        var userIds = string.Join(", ", user);
        var trustedIds = string.Join(", ", trusted);
        return FormattableString.Invariant(
            $"User assumption/trusted evidence declared for {callableId}: total={user.Length + trusted.Length}, user={user.Length}, trusted={trusted.Length}; user-ids=[{userIds}], trusted-ids=[{trustedIds}].");
    }

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
