using System.Collections.Immutable;
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

    public string? CompilerCaptureRequestPath { get; set; }

    public string? CompilerCaptureNonce { get; set; }

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

    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "MSBuild task item parameters use ITaskItem arrays.")]
    public ITaskItem[] CompilerInputPaths { get; set; } = [];

    protected override bool ExecuteCore(CancellationToken cancellationToken)
    {
        string ResolvePath(string path)
        {
            System.ArgumentNullException.ThrowIfNull(path);
            return PublicationLease.CanonicalMember(ResolveProjectRelativePath(ProjectDirectory, path));
        }

        ImmutableArray<string> ResolveCompilerInputPaths(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            var member = ResolvePath(path);
            var input = new FileInfo(member);
            var target = input.LinkTarget == null ? null : input.ResolveLinkTarget(returnFinalTarget: true);
            // Reads follow the final link, while atomic publication replaces
            // its directory entry. Protect both names of a compiler input.
            return target == null ? [member] : [member, PublicationLease.CanonicalMember(target.FullName)];
        }

        var outputPaths = Present([ResultPath, SarifPath]).Select(ResolvePath).ToImmutableArray();
        var inputPaths = Present([
                RequestPath,
                ManifestPath,
                InvocationRequestPath,
                InvocationResultPath,
                InvocationManifestPath,
                WorkerPath,
                LauncherPath,
                WorkerProtocolPath])
            .Select(ResolvePath)
            .ToImmutableArray();
        var compilerOutputPaths = CompilerOutputPaths
            .Where(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (!string.IsNullOrWhiteSpace(item.ItemSpec)))
            .Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (ResolvePath(item.ItemSpec)))
            .ToImmutableArray();
        var compilerInputPaths = CompilerInputPaths
            .Where(static item => item is null ? throw new ArgumentNullException(nameof(item)) : !string.IsNullOrWhiteSpace(item.ItemSpec))
            .SelectMany(item => item is null ? throw new ArgumentNullException(nameof(item)) : ResolveCompilerInputPaths(item.ItemSpec))
            .Distinct(PublicationPaths.Comparer).ToImmutableArray();
        var workerFile = string.IsNullOrWhiteSpace(WorkerPath) ? null : ResolvePath(WorkerPath);
        var workerDirectory = workerFile != null && File.Exists(workerFile)
            ? Path.GetDirectoryName(workerFile)
            : null;
        var cachePath = string.IsNullOrWhiteSpace(CachePath) ? null : ResolvePath(CachePath!);
        var publicationPaths = Present([RequestPath, ResultPath, ManifestPath, SarifPath])
            .Select(ResolvePath)
            .ToImmutableArray();
        ImmutableArray<string> capturePaths = string.IsNullOrWhiteSpace(CompilerCaptureRequestPath)
            ? []
            : [ResolvePath(CompilerCaptureRequestPath), ResolvePath(CompilerCaptureRequestPath + ".completed")];
        var captureManifestPaths = Present([InvocationManifestPath]).Select(ResolvePath).ToImmutableArray();
        var writtenPaths = publicationPaths.Concat(capturePaths).Concat(captureManifestPaths).ToImmutableArray();

        if (outputPaths.Distinct(PublicationPaths.Comparer).Count() != outputPaths.Length)
        {
            Log.LogError("SharpProof output paths must be distinct.");
        }
        if (outputPaths.Intersect(inputPaths, PublicationPaths.Comparer).Any())
        {
            Log.LogError("SharpProof output paths must not alias input paths.");
        }
        try
        {
            PublicationPaths.ValidateWorkerRuntime(writtenPaths, workerFile);
        }
        catch (ArgumentException exception)
        {
            Log.LogError("{0}", exception.Message);
        }
        if (cachePath != null &&
            (writtenPaths.Any(path => path is null ? throw new System.ArgumentNullException(nameof(path)) : (PublicationPaths.IsWithin(path, cachePath, PublicationPaths.Comparison) || PublicationPaths.IsWithin(cachePath, path, PublicationPaths.Comparison))) ||
             workerDirectory != null && PublicationPaths.IsWithin(cachePath, workerDirectory, PublicationPaths.Comparison)))
        {
            Log.LogError("SharpProof output, input, cache, and worker paths must be distinct.");
        }
        if (publicationPaths.Concat(captureManifestPaths).Intersect(compilerOutputPaths, PublicationPaths.Comparer).Any())
        {
            Log.LogError(
                "SharpProof publication paths must not alias compiler-owned outputs.");
        }
        if (captureManifestPaths.Intersect(publicationPaths.Concat(capturePaths)
                .Concat(Present([InvocationRequestPath, InvocationResultPath]).Select(ResolvePath)), PublicationPaths.Comparer).Any())
        {
            Log.LogError("SharpProof compiler capture must not alias publication or invocation paths.");
        }
        if (writtenPaths.Intersect(compilerInputPaths, PublicationPaths.Comparer).Any())
        {
            Log.LogError("SharpProof publication and capture paths must not alias compiler inputs.");
        }
        if (!string.IsNullOrWhiteSpace(CompilerCaptureRequestPath) &&
            (!Guid.TryParseExact(CompilerCaptureNonce, "N", out var nonce) ||
             nonce.ToString("N") != CompilerCaptureNonce))
        {
            Log.LogError("SharpProof compiler capture nonce must be a fresh GUID.");
        }
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        using var lease = PublicationLease.Acquire(publicationPaths, cancellationToken);
        foreach (var path in outputPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        if (!string.IsNullOrWhiteSpace(CompilerCaptureRequestPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capturePath = ResolvePath(CompilerCaptureRequestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(capturePath)!);
            // Token contents stay outside compiler inputs so skipped compilations
            // can reuse their capture. Write only after ownership checks succeed.
            PublicationFile.WriteUtf8(capturePath, CompilerCaptureNonce!, cancellationToken);
        }
        return true;
    }

}
