using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Build.Framework;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed class ValidatePublishedVerificationResult : Microsoft.Build.Utilities.Task
{
    [Required]
    public string RequestPath { get; set; } = string.Empty;

    [Required]
    public string ResultPath { get; set; } = string.Empty;

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var requestPath = LinuxPathIdentity.RequireLocalPath(RequestPath);
            var resultPath = LinuxPathIdentity.RequireLocalPath(ResultPath);
            var manifestPath = LinuxPathIdentity.RequireLocalPath(ManifestPath);
            var requestBytes = File.ReadAllBytes(requestPath);
            using var request = JsonDocument.Parse(requestBytes);
            using var response = JsonDocument.Parse(File.ReadAllBytes(resultPath));
            var requestRoot = request.RootElement;
            var responseRoot = response.RootElement;
            if (requestRoot.ValueKind != JsonValueKind.Object ||
                responseRoot.ValueKind != JsonValueKind.Object ||
                !requestRoot.TryGetProperty("compilerManifest", out var compilerManifest) ||
                compilerManifest.ValueKind != JsonValueKind.Object ||
                !compilerManifest.TryGetProperty("path", out var manifestPathProperty) ||
                !compilerManifest.TryGetProperty("sha256", out var manifestHashProperty) ||
                !responseRoot.TryGetProperty("requestHash", out var requestHashProperty) ||
                !responseRoot.TryGetProperty("inputHash", out var inputHashProperty) ||
                !responseRoot.TryGetProperty("runStatus", out var runStatusProperty) ||
                !requestRoot.TryGetProperty("protocolVersion", out var requestProtocol) ||
                !responseRoot.TryGetProperty("protocolVersion", out var responseProtocol) ||
                requestProtocol.GetString() != "11" ||
                responseProtocol.GetString() != "11" ||
                inputHashProperty.GetString()?.Length != 64 ||
                runStatusProperty.GetString() != "Complete")
            {
                throw new InvalidDataException(
                    "the request or result does not satisfy the worker protocol");
            }

            var manifestBytes = File.ReadAllBytes(manifestPath);
            var manifestHash = Convert.ToHexString(
                SHA256.HashData(manifestBytes));
            if (!string.Equals(
                    Path.GetFullPath(manifestPathProperty.GetString() ?? string.Empty),
                    manifestPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifestHashProperty.GetString(),
                    manifestHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    requestHashProperty.GetString(),
                    Convert.ToHexString(SHA256.HashData(requestBytes)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "the result is not bound to the exact published request and compiler manifest");
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
}
