using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using SharpProof.Host;
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

            var requestPath = ResolvePath(RequestPath);
            var resultPath = ResolvePath(ResultPath);
            var manifestPath = ResolvePath(ManifestPath);
            WorkerVerifyResponse? invocationResponse = null;
            if (!string.IsNullOrWhiteSpace(InvocationResultPath))
            {
                var invocationPath = ResolvePath(InvocationResultPath!);
                invocationResponse = WorkerProtocolJson.DeserializeResponse(
                    DecodeUtf8(File.ReadAllBytes(invocationPath)));
                if (invocationResponse == null ||
                    !WorkerProtocolJson.Validate(invocationResponse).IsValid ||
                    invocationResponse.RunStatus != WorkerRunStatus.Complete)
                {
                    throw new InvalidDataException(
                        "the private invocation result does not satisfy the worker protocol");
                }
            }
            var requestBytes = File.ReadAllBytes(requestPath);
            var request = WorkerProtocolJson.DeserializeRequest(
                DecodeUtf8(requestBytes));
            if (request == null || !WorkerProtocolJson.Validate(request).IsValid)
            {
                throw new InvalidDataException(
                    "the published request does not satisfy the worker protocol");
            }
            var response = WorkerProtocolJson.DeserializeResponse(
                DecodeUtf8(File.ReadAllBytes(resultPath)));
            var expectedInputHash = invocationResponse?.InputHash;
            var expectedManifest = invocationResponse?.Manifest;
            if (response == null ||
                !(invocationResponse == null
                    ? WorkerProtocolJson.Validate(response)
                    : WorkerProtocolJson.Validate(
                        response,
                        expectedInputHash!,
                        expectedManifest)).IsValid ||
                response.RunStatus != WorkerRunStatus.Complete)
            {
                throw new InvalidDataException(
                    "the published result does not satisfy the worker protocol");
            }

            var manifestBytes = File.ReadAllBytes(manifestPath);
            var manifestHash = Convert.ToHexString(
                SHA256.HashData(manifestBytes));
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
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or InvalidOperationException or JsonException)
        {
            Log.LogError(
                "SharpProof verification did not publish a valid current result: {0}",
                exception.Message);
        }

        return !Log.HasLoggedErrors;
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        var offset = bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF
            ? 3
            : 0;
        return new UTF8Encoding(false, true).GetString(
            bytes,
            offset,
            bytes.Length - offset);
    }

}
