using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpProof.Worker.Protocol;

namespace SharpProof.BuildTasks;

public sealed class InvalidatePublishedResult : Microsoft.Build.Utilities.Task
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

    public override bool Execute()
    {
        var lexicalProjectDirectory = Path.GetFullPath(ProjectDirectory);
        string ResolveLexicalPath(string path)
        {
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(lexicalProjectDirectory, path));
        }
        string ResolvePath(string path)
        {
            return WindowsPathIdentity.RequireLocalPath(ResolveLexicalPath(path));
        }

        var outputPaths = Present(ResultPath, SarifPath)
            .Select(ResolvePath)
            .ToArray();
        var publicationPaths = Present(
                RequestPath,
                ResultPath,
                ManifestPath,
                SarifPath)
            .Select(ResolvePath)
            .ToArray();
        var publicationMarkerPaths = publicationPaths
            .Select(WindowsPathIdentity.PublicationMarkerPath)
            .ToArray();
        var publicationMutationPaths = outputPaths
            .Concat(publicationMarkerPaths)
            .ToArray();
        var aliasesOutput = outputPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != outputPaths.Length ||
            Pairs(outputPaths).Any(static pair =>
                WindowsPathIdentity.AreSameExistingFile(pair[0], pair[1]));
        var resolvedLauncherPath = ResolvePath(LauncherPath);
        var toolPaths = Present(WorkerPath, LauncherPath)
            .SelectMany(static path => new[]
            {
                path,
                Path.ChangeExtension(path, ".deps.json"),
                Path.ChangeExtension(path, ".runtimeconfig.json")
            })
            .Concat(LauncherRuntimePaths(resolvedLauncherPath));
        var resolvedWorkerPath = ResolvePath(WorkerPath);
        var workerDirectory = Path.GetDirectoryName(resolvedWorkerPath);
        var workerTreeExists = File.Exists(resolvedWorkerPath) ||
            File.Exists(Path.ChangeExtension(resolvedWorkerPath, ".deps.json")) ||
            File.Exists(Path.ChangeExtension(
                resolvedWorkerPath,
                ".runtimeconfig.json"));
        var protectedPaths = Present(
                RequestPath,
                ManifestPath,
                InvocationRequestPath,
                InvocationResultPath,
                InvocationManifestPath,
                WorkerProtocolPath)
            .Select(ResolvePath)
            .Concat(toolPaths.Select(ResolvePath))
            .ToArray();
        var resolvedCachePath = string.IsNullOrWhiteSpace(CachePath)
            ? null
            : ResolvePath(CachePath!);
        var aliasesFileIdentity = publicationMutationPaths.Any(output =>
            protectedPaths.Any(input => !string.Equals(
                    output,
                    input,
                    StringComparison.OrdinalIgnoreCase) &&
                WindowsPathIdentity.AreSameExistingFile(output, input)));
        var aliasesInput = aliasesFileIdentity ||
            publicationMutationPaths.Any(output => protectedPaths.Any(input =>
                string.Equals(
                    output,
                    input,
                    StringComparison.OrdinalIgnoreCase)));
        var aliasesWorkerTree = workerTreeExists &&
            !string.IsNullOrWhiteSpace(workerDirectory) &&
            publicationMutationPaths.Any(output =>
                WindowsPathIdentity.IsSameOrDescendant(
                    output,
                    workerDirectory));
        var aliasesCache = resolvedCachePath != null &&
            (publicationMutationPaths.Any(output =>
                 WindowsPathIdentity.IsSameOrDescendant(
                     output,
                     resolvedCachePath)) ||
             protectedPaths.Any(input =>
                 WindowsPathIdentity.IsSameOrDescendant(
                     input,
                     resolvedCachePath)) ||
             workerTreeExists &&
             !string.IsNullOrWhiteSpace(workerDirectory) &&
             WindowsPathIdentity.IsSameOrDescendant(
                 resolvedCachePath,
                 workerDirectory));

        if (aliasesOutput)
        {
            Log.LogError("SharpProof output paths must be distinct.");
        }
        if (aliasesFileIdentity)
        {
            Log.LogError("SharpProof output aliases a protected file identity.");
        }
        if (aliasesInput)
        {
            Log.LogError("SharpProof output paths must not alias input paths.");
        }
        if (aliasesWorkerTree)
        {
            Log.LogError("SharpProof output paths must not be inside the worker runtime.");
        }
        if (aliasesCache)
        {
            Log.LogError("SharpProof output, input, cache, and worker paths must be distinct.");
        }
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        using (WindowsPathIdentity.AcquirePublicationSet(
                   publicationPaths,
                   TimeSpan.FromSeconds(30)))
        {
            foreach (var path in outputPaths)
            {
                WindowsPathIdentity.DeleteIfUnprotected(path, protectedPaths);
            }
        }
        return !Log.HasLoggedErrors;
    }

    private static IEnumerable<string> Present(params string?[] paths)
    {
        return paths.Where(static path => !string.IsNullOrWhiteSpace(path))!;
    }

    private static IEnumerable<string[]> Pairs(string[] paths)
    {
        for (var left = 0; left < paths.Length; left++)
        {
            for (var right = left + 1; right < paths.Length; right++)
            {
                yield return [paths[left], paths[right]];
            }
        }
    }

    private static IEnumerable<string> LauncherRuntimePaths(
        string resolvedLauncherPath)
    {
        var directory = Path.GetDirectoryName(resolvedLauncherPath) ??
            throw new InvalidOperationException(
                "The SharpProof launcher path has no directory.");
        return
        [
            Path.Combine(directory, "SharpProof.CompilerArtifact.dll"),
            Path.Combine(directory, "SharpProof.Ir.dll"),
            Path.Combine(directory, "SharpProof.Specs.dll"),
            Path.Combine(directory, "SharpProof.Worker.Protocol.dll"),
            Path.Combine(directory, "SharpProof.Worker.Launcher.exe"),
            Path.Combine(directory, "System.IO.Pipelines.dll"),
            Path.Combine(directory, "System.Text.Encodings.Web.dll"),
            Path.Combine(directory, "System.Text.Json.dll")
        ];
    }
}
