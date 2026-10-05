using System.Text.Json;
using Microsoft.Build.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.BuildTasks;

public sealed class ValidatePublishedVerificationResult : Microsoft.Build.Utilities.Task
{
    [Required]
    public string RequestPath { get; set; } = string.Empty;

    [Required]
    public string ResultPath { get; set; } = string.Empty;

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    public string? SarifPath { get; set; }

    public string? ProjectDirectory { get; set; }

    public string? InvocationResultPath { get; set; }

    public override bool Execute()
    {
        try
        {
            var root = Path.GetFullPath(string.IsNullOrWhiteSpace(ProjectDirectory) ? Environment.CurrentDirectory : ProjectDirectory);
            string Resolve(string path)
            {
                return CancelableBuildTask.ResolveProjectRelativePathFromRoot(root, path);
            }
            ValidateFiles(Resolve(RequestPath), Resolve(ResultPath), Resolve(ManifestPath),
                string.IsNullOrWhiteSpace(SarifPath) ? null : Resolve(SarifPath),
                string.IsNullOrWhiteSpace(InvocationResultPath) ? null : Resolve(InvocationResultPath), root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or InvalidOperationException or JsonException)
        {
            Log.LogError("SharpProof verification did not publish a valid current result: {0}", exception.Message);
        }
        return !Log.HasLoggedErrors;
    }

    internal static void ValidateFiles(string requestPath, string resultPath, string manifestPath,
        string? sarifPath, string? invocationResultPath, string? projectRoot = null)
    {
        static bool IsPublishableResponse(WorkerVerifyResponse response)
        {
            return response.RunStatus == WorkerRunStatus.Complete ||
                response.RunStatus == WorkerRunStatus.TimedOut && response.FailureReason == WorkerRunFailureReason.None;
        }
        string ResolvePath(string path)
        {
            return CancelableBuildTask.ResolveProjectRelativePathFromRoot(projectRoot ?? Environment.CurrentDirectory, path);
        }
        WorkerVerifyResponse? invocationResponse = null;
        if (!string.IsNullOrWhiteSpace(invocationResultPath))
        {
            var invocationPath =
                ResolvePath(invocationResultPath!);
            invocationResponse = WorkerProtocolJson.DeserializeResponse(
                WorkerProtocolJson.ReadUtf8File(invocationPath));
            if (invocationResponse == null ||
                !WorkerProtocolJson.Validate(invocationResponse).IsValid ||
                !IsPublishableResponse(invocationResponse))
            {
                throw new InvalidDataException(
                    "the private invocation result does not satisfy the worker protocol");
            }
        }
        var request = WorkerProtocolJson.DeserializeRequest(
            WorkerProtocolJson.ReadUtf8File(requestPath));
        if (request == null || !WorkerProtocolJson.Validate(request).IsValid)
        {
            throw new InvalidDataException(
                "the published request does not satisfy the worker protocol");
        }
        var response = WorkerProtocolJson.DeserializeResponse(
            WorkerProtocolJson.ReadUtf8File(resultPath));
        var expectedInputHash = invocationResponse?.InputHash;
        var expectedManifest = invocationResponse?.Manifest;
        if (response == null ||
            !(invocationResponse == null
                ? WorkerProtocolJson.Validate(response)
                : WorkerProtocolJson.Validate(
                    response,
                    expectedInputHash!,
                    expectedManifest)).IsValid ||
            !IsPublishableResponse(response))
        {
            throw new InvalidDataException(
                "the published result does not satisfy the worker protocol");
        }

        var manifestHash = WorkerProtocolJson.ComputeFileSha256(
            manifestPath);
        if (!string.Equals(
                ResolvePath(request.CompilerManifest.Path),
                manifestPath,
                StringComparison.Ordinal) ||
            !string.Equals(
                request.CompilerManifest.Sha256,
                manifestHash,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                response.RequestHash,
                WorkerProtocolJson.ComputeRequestHash(request),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "the result is not bound to the exact published request and compiler manifest");
        }
        if (invocationResponse != null)
        {
            var invocationRequestHash = invocationResponse.RequestHash;
            invocationResponse.RequestHash = response.RequestHash;
            var invocationJson = WorkerProtocolJson.SerializeResponse(
                invocationResponse);
            invocationResponse.RequestHash = invocationRequestHash;
            if (!string.Equals(
                    invocationJson,
                    WorkerProtocolJson.SerializeResponse(response),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "the published result does not belong to this invocation");
            }
        }
        if (sarifPath != null)
        {
            using var sarif = JsonDocument.Parse(
                WorkerProtocolJson.ReadUtf8File(sarifPath));
            var root = sarif.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                version.GetString() != "2.1.0" ||
                !root.TryGetProperty("runs", out var runs) ||
                runs.ValueKind != JsonValueKind.Array || runs.GetArrayLength() == 0)
            {
                throw new InvalidDataException(
                    "the published SARIF does not satisfy SARIF 2.1.0");
            }
        }
    }
}
