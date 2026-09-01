using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed class ResetPublishedVerification : CancelableBuildTask
{
    [Required]
    public string RequestPath { get; set; } = string.Empty;

    [Required]
    public string ResultPath { get; set; } = string.Empty;

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    public string? SarifPath { get; set; }

    public string? ProjectDirectory { get; set; }

    protected override bool ExecuteCore(CancellationToken cancellationToken)
    {
        try
        {
            LinuxPathIdentity.ResetPublicationSet(
                Present(RequestPath, ResultPath, ManifestPath, SarifPath)
                    .Select(path => ResolveProjectRelativePath(
                        ProjectDirectory,
                        path)),
                TimeSpan.FromSeconds(30), cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
            Log.LogErrorFromException(exception, showStackTrace: false);
            return false;
        }
    }

}
