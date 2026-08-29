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

    public string? PublicationTopologyPath { get; set; }

    public string? CompilerManifestSourcePath { get; set; }

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
            var currentPaths = Present(RequestPath, ResultPath, ManifestPath, SarifPath)
                .Select(path => Path.GetFullPath(Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(projectDirectory, path)))
                .ToArray();
            var topologyPath = string.IsNullOrWhiteSpace(PublicationTopologyPath)
                ? null
                : Path.GetFullPath(Path.IsPathRooted(PublicationTopologyPath)
                    ? PublicationTopologyPath
                    : Path.Combine(projectDirectory, PublicationTopologyPath));
            var persistedPaths = topologyPath == null
                ? null
                : PublicationTopologyStore.Read(topologyPath);
            if (persistedPaths is not null)
            {
                LinuxPathIdentity.ResetPublicationSet(
                    persistedPaths,
                    TimeSpan.FromSeconds(30),
                    cancellation.Token);
            }
            if (persistedPaths is null ||
                !SamePaths(persistedPaths, currentPaths))
            {
                LinuxPathIdentity.ResetPublicationSet(
                    currentPaths,
                    TimeSpan.FromSeconds(30),
                    cancellation.Token);
            }
            if (topologyPath != null)
            {
                PublicationTopologyStore.Delete(topologyPath);
            }
            if (!string.IsNullOrWhiteSpace(CompilerManifestSourcePath))
            {
                var sourcePath = Path.GetFullPath(Path.IsPathRooted(
                        CompilerManifestSourcePath)
                    ? CompilerManifestSourcePath
                    : Path.Combine(projectDirectory, CompilerManifestSourcePath));
                // File.Delete throws when the intermediate directory has not
                // been created yet. A first clean/build is a valid no-op for
                // this derived compiler output, so only delete it when it is
                // present.
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
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

    private static bool SamePaths(
        string[] left,
        string[] right)
    {
        return left.Length == right.Length &&
            left.All(path => right.Contains(path, StringComparer.Ordinal));
    }
}
