using System.Diagnostics.CodeAnalysis;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed class InvalidatePublishedResult : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private readonly object _synchronization = new();
    private Action? _cancelExecution;
    private bool _canceled;

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
        ContainerContract.ValidateRequired();
        var lexicalProjectDirectory = Path.GetFullPath(ProjectDirectory);
        string ResolveLexicalPath(string path)
        {
            return Path.GetFullPath(Path.IsPathRooted(path)
                ? path
                : Path.Combine(lexicalProjectDirectory, path));
        }
        string ResolvePath(string path)
        {
            return LinuxPathIdentity.RequireLocalPath(ResolveLexicalPath(path));
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
            .Select(LinuxPathIdentity.PublicationMarkerPath)
            .ToArray();
        var publicationMutationPaths = outputPaths
            .Concat(publicationMarkerPaths)
            .ToArray();
        var compilerOutputPaths = CompilerOutputPaths
            .Where(static item => !string.IsNullOrWhiteSpace(item.ItemSpec))
            .Select(static item => item.ItemSpec)
            .Select(ResolvePath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var aliasesOutput = outputPaths
            .Distinct(StringComparer.Ordinal)
            .Count() != outputPaths.Length ||
            Pairs(outputPaths).Any(static pair =>
                LinuxPathIdentity.AreSameExistingFile(pair[0], pair[1]));
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
        var inputPaths = Present(
                RequestPath,
                ManifestPath,
                InvocationRequestPath,
                InvocationResultPath,
                InvocationManifestPath)
            .Select(ResolvePath)
            .ToArray();
        var resolvedToolPaths = toolPaths
            .Append(WorkerProtocolPath)
            .Select(ResolvePath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var protectedPaths = inputPaths
            .Concat(resolvedToolPaths)
            .ToArray();
        var resolvedCachePath = string.IsNullOrWhiteSpace(CachePath)
            ? null
            : ResolvePath(CachePath!);
        var aliasesFileIdentity = publicationMutationPaths.Any(output =>
            protectedPaths.Any(input => !string.Equals(
                    output,
                    input,
                    StringComparison.Ordinal) &&
                LinuxPathIdentity.AreSameExistingFile(output, input)));
        var aliasesInput = aliasesFileIdentity ||
            Pairs(inputPaths).Any(static pair =>
                LinuxPathIdentity.PathsConflict(pair[0], pair[1])) ||
            publicationMutationPaths.Any(output => protectedPaths.Any(input =>
                LinuxPathIdentity.PathsConflict(output, input)));
        var aliasesWorkerTree = workerTreeExists &&
            !string.IsNullOrWhiteSpace(workerDirectory) &&
            publicationPaths
                .Concat(publicationMarkerPaths)
                .Concat(inputPaths)
                .Any(path => LinuxPathIdentity.PathsConflict(
                    path,
                    workerDirectory));
        var aliasesCache = resolvedCachePath != null &&
            (publicationPaths
                 .Concat(publicationMarkerPaths)
                 .Concat(inputPaths)
                 .Any(path => LinuxPathIdentity.PathsConflict(
                     path,
                     resolvedCachePath)) ||
             workerTreeExists &&
             !string.IsNullOrWhiteSpace(workerDirectory) &&
             LinuxPathIdentity.PathsConflict(
                 resolvedCachePath,
                 workerDirectory));
        var aliasesCompilerOutput = publicationPaths
            .Concat(inputPaths)
            .Concat(publicationMarkerPaths)
            .Any(publication => compilerOutputPaths.Any(compilerOutput =>
                string.Equals(
                    publication,
                    compilerOutput,
                    StringComparison.Ordinal) ||
                LinuxPathIdentity.AreSameExistingFile(
                    publication,
                    compilerOutput)));

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
        if (aliasesCompilerOutput)
        {
            Log.LogError(
                "SharpProof publication paths must not alias compiler-owned outputs.");
        }
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        try
        {
            using (LinuxPathIdentity.AcquirePublicationSet(
                       publicationPaths,
                       TimeSpan.FromSeconds(30),
                       cancellationToken))
            {
                foreach (var path in outputPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LinuxPathIdentity.DeleteIfUnprotected(path, protectedPaths);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        return !Log.HasLoggedErrors;
    }

    public void Cancel()
    {
        lock (_synchronization)
        {
            _canceled = true;
            // Invoke while Execute still owns the linked source. Copying the
            // delegate and invoking after releasing the lock races the
            // Execute finally block and can call Cancel on a disposed source.
            _cancelExecution?.Invoke();
        }
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
            LauncherRuntimeCompanionInventory.FileNames.Select(
                file => Path.Combine(directory, file));
    }
}
