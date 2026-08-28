using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed class WriteInvocationRunLease : Microsoft.Build.Utilities.Task
{
    [Required]
    public string InvocationDirectory { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            ContainerContract.ValidateRequired();
            InvocationRunLeaseStore.Write(InvocationDirectory);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            VerifierBuildDiagnosticCodes.LogError(
                Log,
                VerifierBuildDiagnosticCodes.PublishedEvidence,
                "SharpProof could not write the invocation run lease: {0}",
                exception.Message);
            return false;
        }
    }
}
