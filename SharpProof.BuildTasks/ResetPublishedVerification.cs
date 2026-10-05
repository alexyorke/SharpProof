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
            var paths = Present(RequestPath, ResultPath, ManifestPath, SarifPath)
                .Select(path => ResolveProjectRelativePath(ProjectDirectory, path)).ToArray();
            using var lease = PublicationLease.Acquire(paths, cancellationToken);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
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
