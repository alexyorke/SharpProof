using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.BuildTasks;

internal sealed class VerificationPublication
{
    private readonly Dictionary<string, (string Stable, string Prepared)> _paths;
    private readonly string _invocationRequest;
    private readonly string _invocationResult;

    private VerificationPublication(Dictionary<string, (string Stable, string Prepared)> paths, string invocationRequest, string invocationResult)
    {
        _paths = paths;
        _invocationRequest = invocationRequest;
        _invocationResult = invocationResult;
    }

    internal static VerificationPublication? Prepare(string[] arguments, string workingDirectory)
    {
        var options = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index] is "--request" or "--result" or "--publish-request" or "--publish-result" or
                "--publish-compiler-manifest" or "--publish-sarif" or "--compiler-manifest" or "--worker")
            {
                options.Add(arguments[index], index + 1);
            }
        }
        if (!options.Keys.Any(key => key.StartsWith("--publish-", StringComparison.Ordinal)))
        { return null; }
        foreach (var required in new[] { "--request", "--result", "--publish-request", "--publish-result", "--publish-compiler-manifest" })
        {
            if (!options.ContainsKey(required))
            { throw new ArgumentException("Verification publication options must be complete.", nameof(arguments)); }
        }
        string Resolve(string path)
        {
            return CancelableBuildTask.ResolveProjectRelativePathFromRoot(workingDirectory, path);
        }
        var request = Resolve(arguments[options["--request"]]);
        var result = Resolve(arguments[options["--result"]]);
        var privateDirectory = Path.Combine(Path.GetDirectoryName(request)!, "publication-" + Guid.NewGuid().ToString("N"));
        var paths = new Dictionary<string, (string Stable, string Prepared)>(StringComparer.Ordinal);
        foreach (var option in options.Where(option => option.Key.StartsWith("--publish-", StringComparison.Ordinal)))
        {
            var stable = Resolve(arguments[option.Value]);
            var prepared = Path.Combine(privateDirectory, option.Key[2..] + ".json");
            paths.Add(option.Key, (stable, prepared));
        }
        var members = PublicationLease.ValidateMembers(paths.Values.Select(path => path.Stable));
        var inputs = new[] { request, result }.Concat(options.TryGetValue("--compiler-manifest", out var inputManifest)
            ? new[] { Resolve(arguments[inputManifest]) } : []).Select(PublicationLease.CanonicalMember);
        if (members.Intersect(inputs, StringComparer.Ordinal).Any())
        {
            throw new ArgumentException("Stable verification outputs must be distinct from private invocation outputs.", nameof(arguments));
        }
        PublicationPaths.ValidateWorkerRuntime(members.Concat([request, result]),
            options.TryGetValue("--worker", out var workerIndex) ? Resolve(arguments[workerIndex]) : null);
        if (arguments[0] != "verify" && !arguments[0].StartsWith("--", StringComparison.Ordinal))
        {
            PublicationPaths.ValidateWorkerRuntime(members.Concat([request, result]), Resolve(arguments[0]));
        }
        foreach (var option in options.Where(option => option.Key.StartsWith("--publish-", StringComparison.Ordinal)))
        {
            arguments[option.Value] = paths[option.Key].Prepared;
        }
        return new(paths, request, result);
    }

    internal void Publish(CancellationToken cancellationToken, Action? beforeValidation = null)
    {
        var requestPaths = _paths["--publish-request"];
        var resultPaths = _paths["--publish-result"];
        var manifestPaths = _paths["--publish-compiler-manifest"];
        var sarifPaths = _paths.GetValueOrDefault("--publish-sarif");
        ValidatePublishedVerificationResult.ValidateFiles(requestPaths.Prepared, resultPaths.Prepared, manifestPaths.Prepared,
            sarifPaths.Prepared, _invocationResult);
        var request = WorkerProtocolJson.DeserializeRequest(WorkerProtocolJson.ReadUtf8File(requestPaths.Prepared))!;
        var response = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.ReadUtf8File(resultPaths.Prepared))!;
        var invocationRequest = WorkerProtocolJson.DeserializeRequest(WorkerProtocolJson.ReadUtf8File(_invocationRequest));
        var invocationResponse = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.ReadUtf8File(_invocationResult))!;
        if (invocationRequest == null || !WorkerProtocolJson.Validate(invocationRequest).IsValid ||
            !WorkerProtocolJson.ValidateForRequest(invocationResponse, WorkerProtocolJson.ComputeRequestHash(invocationRequest),
                invocationResponse.InputHash, invocationResponse.Manifest, invocationRequest, invocationResponse.Summary.Versions).IsValid)
        {
            throw new InvalidDataException("The private result is not bound to its invocation request.");
        }
        var preparedManifestPath = request.CompilerManifest.Path;
        request.CompilerManifest.Path = invocationRequest.CompilerManifest.Path;
        var matchesInvocation = WorkerProtocolJson.SerializeRequest(request) == WorkerProtocolJson.SerializeRequest(invocationRequest);
        request.CompilerManifest.Path = preparedManifestPath;
        if (!matchesInvocation || !WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(request),
                invocationResponse.InputHash, invocationResponse.Manifest, request, invocationResponse.Summary.Versions).IsValid)
        {
            throw new InvalidDataException("The prepared publication does not belong to this invocation request.");
        }
        request.CompilerManifest.Path = manifestPaths.Stable;
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        using var lease = PublicationLease.Acquire(_paths.Values.Select(path => path.Stable), cancellationToken);
        PublicationFile.Copy(manifestPaths.Prepared, manifestPaths.Stable, cancellationToken);
        PublicationFile.WriteUtf8(requestPaths.Stable, WorkerProtocolJson.SerializeRequest(request), cancellationToken);
        if (sarifPaths.Prepared != null)
        {
            PublicationFile.Copy(sarifPaths.Prepared, sarifPaths.Stable, cancellationToken);
        }
        PublicationFile.WriteUtf8(resultPaths.Stable, WorkerProtocolJson.SerializeCanonicalResponse(response), cancellationToken);
        beforeValidation?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePublishedVerificationResult.ValidateFiles(requestPaths.Stable, resultPaths.Stable, manifestPaths.Stable,
            sarifPaths.Stable, _invocationResult);
    }
}
