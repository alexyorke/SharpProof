using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "MSBuild loads task types by their public names.")]
public sealed class ReclaimInvocationRuns : Microsoft.Build.Utilities.Task
{
    [Required]
    public string RunsDirectory { get; set; } = string.Empty;

    public string? CurrentInvocationId { get; set; }

    public override bool Execute()
    {
        try
        {
            ContainerContract.ValidateRequired();
            _ = InvocationRunLeaseStore.Reclaim(
                RunsDirectory,
                CurrentInvocationId);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            VerifierBuildDiagnosticCodes.LogError(
                Log,
                VerifierBuildDiagnosticCodes.PublishedEvidence,
                "SharpProof could not reclaim stale invocation runs: {0}",
                exception.Message);
            return false;
        }
    }
}
