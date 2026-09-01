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
        using var cancellation = new CancellationTokenSource();
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
            return Execute(cancellation.Token);
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

    private bool Execute(CancellationToken cancellationToken)
    {
        try
        {
            var projectDirectory = Path.GetFullPath(
                string.IsNullOrWhiteSpace(ProjectDirectory)
                    ? Environment.CurrentDirectory
                    : ProjectDirectory);
            string ResolvePath(string path)
            {
                return LinuxPathIdentity.RequireLocalPath(
                    Path.IsPathRooted(path)
                        ? path
                        : Path.Combine(projectDirectory, path));
            }

            LinuxPathIdentity.ResetPublicationSet(
                Present(RequestPath, ResultPath, ManifestPath, SarifPath)
                    .Select(ResolvePath),
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

    public void Cancel()
    {
        lock (_synchronization)
        {
            _canceled = true;
            _cancelExecution?.Invoke();
        }
    }

    private static IEnumerable<string> Present(params string?[] paths)
    {
        return paths.Where(static path => !string.IsNullOrWhiteSpace(path))!;
    }
}
