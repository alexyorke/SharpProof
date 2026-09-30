using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed class InvalidatePublishedResult : CancelableBuildTask
{
    [Required]
    public string ResultPath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string? RequestPath { get; set; }

    public string? ManifestPath { get; set; }

    public string? SarifPath { get; set; }

    public string? InvocationRequestPath { get; set; }

    public string? InvocationResultPath { get; set; }

    public string? InvocationManifestPath { get; set; }

    [Required]
    public string WorkerPath { get; set; } = string.Empty;

    [Required]
    public string LauncherPath { get; set; } = string.Empty;

    [Required]
    public string WorkerProtocolPath { get; set; } = string.Empty;

    public string? CachePath { get; set; }

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "MSBuild task item parameters use ITaskItem arrays.")]
    public ITaskItem[] CompilerOutputPaths { get; set; } = [];

    protected override bool ExecuteCore(CancellationToken cancellationToken)
    {
        ContainerContract.ValidateRequired();
        string ResolvePath(string path)
        {
            return ResolveProjectRelativePath(ProjectDirectory, path);
        }

        var outputPaths = Present(ResultPath, SarifPath).Select(ResolvePath).ToArray();
        var inputPaths = Present(
                RequestPath,
                ManifestPath,
                InvocationRequestPath,
                InvocationResultPath,
                InvocationManifestPath,
                WorkerPath,
                LauncherPath,
                WorkerProtocolPath)
            .Select(ResolvePath)
            .ToArray();
        var compilerOutputPaths = CompilerOutputPaths
            .Where(static item => !string.IsNullOrWhiteSpace(item.ItemSpec))
            .Select(item => ResolvePath(item.ItemSpec))
            .ToArray();
        var workerFile = string.IsNullOrWhiteSpace(WorkerPath) ? null : ResolvePath(WorkerPath);
        var workerDirectory = workerFile != null && File.Exists(workerFile)
            ? Path.GetDirectoryName(workerFile)
            : null;
        var cachePath = string.IsNullOrWhiteSpace(CachePath) ? null : ResolvePath(CachePath!);
        var publicationPaths = Present(RequestPath, ResultPath, ManifestPath, SarifPath)
            .Select(ResolvePath)
            .ToArray();

        if (outputPaths.Distinct(StringComparer.Ordinal).Count() != outputPaths.Length)
        {
            Log.LogError("SharpProof output paths must be distinct.");
        }
        if (outputPaths.Intersect(inputPaths, StringComparer.Ordinal).Any())
        {
            Log.LogError("SharpProof output paths must not alias input paths.");
        }
        if (workerDirectory != null &&
            publicationPaths.Any(path => IsWithin(path, workerDirectory)))
        {
            Log.LogError("SharpProof output paths must not be inside the worker runtime.");
        }
        if (cachePath != null &&
            (publicationPaths.Any(path => IsWithin(path, cachePath) || IsWithin(cachePath, path)) ||
             workerDirectory != null && IsWithin(cachePath, workerDirectory)))
        {
            Log.LogError("SharpProof output, input, cache, and worker paths must be distinct.");
        }
        if (publicationPaths.Intersect(compilerOutputPaths, StringComparer.Ordinal).Any())
        {
            Log.LogError(
                "SharpProof publication paths must not alias compiler-owned outputs.");
        }
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        foreach (var path in outputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        return true;
    }

    private static bool IsWithin(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return string.Equals(path, directory, StringComparison.Ordinal) ||
            path.StartsWith(root, StringComparison.Ordinal);
    }
}
