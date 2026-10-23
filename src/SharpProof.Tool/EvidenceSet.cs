using System.Text;
using SharpProof.CompilerArtifact;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Tool;

internal sealed class EvidenceSet
{
    internal EvidenceSet(WorkerVerifyResponse response, WorkerVerifyRequest request, CompilerManifestArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(artifact);
        Response = response;
        Request = request;
        Artifact = artifact;
    }

    internal WorkerVerifyResponse Response { get; }
    internal WorkerVerifyRequest Request { get; }
    internal CompilerManifestArtifact Artifact { get; }
    internal static EvidenceSet Load(string resultPath, string? requestPath, string? manifestPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (manifestPath != null && string.IsNullOrWhiteSpace(manifestPath))
        { throw new ArgumentException("The manifest path must not be blank.", nameof(manifestPath)); }
        if (requestPath != null && string.IsNullOrWhiteSpace(requestPath))
        { throw new ArgumentException("The request path must not be blank.", nameof(requestPath)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(resultPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(resultPath))!;
        requestPath ??= Path.Combine(directory, "request.json");
        manifestPath ??= Path.Combine(directory, "compiler-manifest.json");
        var response = WorkerProtocolJson.DeserializeResponse(ReadBounded(resultPath, WorkerProtocolJson.MaximumJsonBytes, cancellationToken)) ?? throw new InvalidDataException("The result is empty.");
        var request = WorkerProtocolJson.DeserializeRequest(ReadBounded(requestPath, WorkerProtocolJson.MaximumJsonBytes, cancellationToken)) ?? throw new InvalidDataException("The request is empty.");
        RequireValid(WorkerProtocolJson.Validate(request));
        RequireValid(WorkerProtocolJson.Validate(response));
        var bytes = CompilerManifestArtifactFile.ReadAllBytes(manifestPath, CompilerManifestArtifactFile.MaximumBytes, cancellationToken);
        var digest = ArtifactDigest.Compute(bytes);
        if (!string.Equals(digest, request.CompilerManifest.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The compiler manifest does not match the request digest.");
        }

        var artifact = CompilerManifestArtifactJson.Deserialize(new UTF8Encoding(false, true).GetString(bytes.AsSpan()), cancellationToken);
        if (artifact.MaximumExpressionDepth != request.Budgets.MaximumExpressionDepth &&
            !(response.RunStatus == WorkerRunStatus.Failed && response.FailureReason == WorkerRunFailureReason.CompilerManifestMismatch &&
                response.ClaimResults.All(static result =>
                {
                    ArgumentNullException.ThrowIfNull(result);
                    return result.Outcome == WorkerClaimOutcome.Unknown;
                })))
        {
            throw new InvalidDataException("The compiler manifest expression-depth limit does not match the request.");
        }
        var versions = response.Summary.Versions;
        var inputHash = CompilerArtifactInputHash.Compute(request, digest, "SharpProof.Worker", versions.WorkerVersion, versions.WorkerBinarySha256, ApiSpecTable.DefaultTableIdentity, versions.ApiSpecVersion, versions.ApiSpecContentSha256);
        RequireValid(WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(request), inputHash, artifact.Manifest, request, versions));
        ValidateDescriptions(response, artifact, cancellationToken);
        return new(response, request, artifact);
    }

    internal static string ReadBounded(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget != null || info.Length is <= 0 || info.Length > maximumBytes)
        {
            throw new InvalidDataException("Required evidence must be a nonempty regular file within the size limit: " + Path.GetFileName(path));
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != info.Length)
        {
            throw new InvalidDataException("Evidence changed while opening it.");
        }

        var bytes = new byte[checked((int)info.Length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
            if (read == 0)
            {
                throw new InvalidDataException("Evidence changed while reading it.");
            }
            offset += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("Evidence changed while reading it.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new UTF8Encoding(false, true).GetString(bytes.AsSpan());
    }

    private static void ValidateDescriptions(WorkerVerifyResponse response, CompilerManifestArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(artifact);
        var claims = artifact.Manifest.Claims.ToDictionary(static claim =>
        {
            ArgumentNullException.ThrowIfNull(claim);
            return claim.ClaimId;
        }, StringComparer.Ordinal);
        var callables = artifact.Callables.ToDictionary(static callable =>
        {
            ArgumentNullException.ThrowIfNull(callable);
            return callable.CallableId;
        }, StringComparer.Ordinal);
        foreach (var result in response.ClaimResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(result);
            var callable = callables[claims[result.ClaimId].CallableId];
            if (result.Presentation is { } presentation &&
                (callable.Presentation is not { } captured || presentation.Name != captured.Name ||
                    !presentation.ParameterNames.SequenceEqual(captured.ParameterNames, StringComparer.Ordinal)))
            {
                throw new InvalidDataException("The result presentation does not match the compiler manifest.");
            }
            if (result.Diagnostic is { } diagnostic && diagnostic != callable.Diagnostic)
            {
                throw new InvalidDataException("The result diagnostic context does not match the compiler manifest.");
            }
        }
    }

    private static void RequireValid(WorkerProtocolValidationResult validation)
    {
        if (!validation.IsValid)
        {
            throw new InvalidDataException("Evidence validation failed: " + string.Join(", ", validation.Errors.Select(static error =>
            {
                global::SharpProof.Internal.InputValidation.Require(error, nameof(error));
                return error.Code;
            })));
        }
    }
}
