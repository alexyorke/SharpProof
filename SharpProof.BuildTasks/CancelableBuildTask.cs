using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

[SuppressMessage(
    "Maintainability",
    "CA1515:Consider making public types internal",
    Justification = "Public MSBuild tasks require a public base type.")]
public abstract class CancelableBuildTask : Microsoft.Build.Utilities.Task,
    ICancelableTask
{
    private readonly object _gate = new();
    private Action? _cancelExecution;
    private bool _canceled;

    public sealed override bool Execute()
    {
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        lock (_gate)
        {
            if (_canceled)
            {
                return false;
            }
            _cancelExecution = cancel;
        }
        try
        {
            return ExecuteCore(cancellation.Token);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_cancelExecution, cancel))
                {
                    _cancelExecution = null;
                }
            }
        }
    }

    protected abstract bool ExecuteCore(CancellationToken cancellationToken);

    public void Cancel()
    {
        lock (_gate)
        {
            _canceled = true;
            // Invoke while Execute still owns the linked source. Copying the
            // delegate and invoking after releasing the lock races disposal.
            _cancelExecution?.Invoke();
        }
    }

    protected static IEnumerable<string> Present(params string?[] paths)
    {
        return paths.Where(static path => !string.IsNullOrWhiteSpace(path))!;
    }

    internal static string ResolveProjectRelativePath(
        string? projectDirectory,
        string path)
    {
        var root = Path.GetFullPath(
            string.IsNullOrWhiteSpace(projectDirectory)
                ? Environment.CurrentDirectory
                : projectDirectory);
        return LinuxPathIdentity.RequireLocalPath(
            Path.IsPathRooted(path) ? path : Path.Combine(root, path));
    }
}
