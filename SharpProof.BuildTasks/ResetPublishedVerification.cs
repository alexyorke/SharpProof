using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed class ResetPublishedVerification : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private readonly object _synchronization = new();
    private Action? _cancelExecution;
    private bool _canceled;
    [Required]
    public string RequestPath { get; set; } = string.Empty;

    [Required]
    public string ResultPath { get; set; } = string.Empty;

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    public string? SarifPath { get; set; }

    public string? ProjectDirectory { get; set; }

    public override bool Execute()
    {
        using var cancellation = new TaskExecutionCancellation();
        Action cancel = cancellation.Cancel;
        lock (_synchronization)
        {
            if (_canceled)
            {
                return false;
            }
            _cancelExecution = cancel;
        }
        try
        {
            var projectDirectory = string.IsNullOrWhiteSpace(ProjectDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(ProjectDirectory);
            var paths = Present(RequestPath, ResultPath, ManifestPath, SarifPath)
                .Select(path => Path.GetFullPath(Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectDirectory, path)));
            LinuxPathIdentity.ResetPublicationSet(
                paths,
                TimeSpan.FromSeconds(30),
                cancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (
            cancellation.Token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            InvalidOperationException)
        {
            VerifierBuildDiagnosticCodes.LogError(
                Log,
                VerifierBuildDiagnosticCodes.PublishedEvidence,
                "SharpProof could not reset the previous publication: {0}",
                exception.Message);
            return false;
        }
        finally
        {
            lock (_synchronization)
            {
                if (ReferenceEquals(_cancelExecution, cancel))
                {
                    _cancelExecution = null;
                }
            }
        }
    }

    public void Cancel()
    {
        Action? cancel;
        lock (_synchronization)
        {
            _canceled = true;
            cancel = _cancelExecution;
        }
        cancel?.Invoke();
    }

    private static IEnumerable<string> Present(params string?[] paths)
    {
        return paths.Where(static path => !string.IsNullOrWhiteSpace(path))!;
    }
}
