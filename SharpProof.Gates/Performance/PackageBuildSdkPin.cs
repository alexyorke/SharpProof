using System.Diagnostics;
using System.Text.Json;

namespace SharpProof.Gates.Performance;

internal sealed record PackageBuildSdkIdentity(
    string ConfiguredVersion,
    string RollForward,
    string ResolvedVersion);

internal static class PackageBuildSdkPin
{
    internal static async Task<PackageBuildSdkIdentity> PinAndValidateAsync(
        string repositoryRoot,
        string probeRoot,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(repositoryRoot, "global.json");
        if (!File.Exists(sourcePath))
        {
            throw new InvalidDataException(
                "The repository global.json is required by the performance gate.");
        }

        var bytes = await File.ReadAllBytesAsync(
                sourcePath,
                cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        if (!document.RootElement.TryGetProperty("sdk", out var sdk) ||
            !sdk.TryGetProperty("version", out var configuredVersionElement))
        {
            throw new InvalidDataException(
                "The repository global.json must declare sdk.version.");
        }

        var configuredVersion = configuredVersionElement.GetString();
        var rollForward = sdk.TryGetProperty(
            "rollForward",
            out var rollForwardElement)
            ? rollForwardElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(configuredVersion) ||
            string.IsNullOrWhiteSpace(rollForward))
        {
            throw new InvalidDataException(
                "The repository global.json must declare non-empty " +
                "sdk.version and sdk.rollForward values.");
        }

        var targetPath = Path.Combine(probeRoot, "global.json");
        await File.WriteAllBytesAsync(
                targetPath,
                bytes,
                cancellationToken)
            .ConfigureAwait(false);
        var repositoryVersion = await ResolveSdkVersionAsync(
                repositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);
        var probeVersion = await ResolveSdkVersionAsync(
                probeRoot,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                repositoryVersion,
                probeVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The repository selected .NET SDK '{repositoryVersion}', " +
                $"but the pinned performance probe selected '{probeVersion}'.");
        }

        return new PackageBuildSdkIdentity(
            configuredVersion,
            rollForward,
            probeVersion);
    }

    private static async Task<string> ResolveSdkVersionAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = GateProcess.CreateCaptured(
            "dotnet",
            workingDirectory);
        startInfo.ArgumentList.Add("--version");
        var result = await GateProcess.RunCapturedAsync(
                startInfo,
                cancellationToken)
            .ConfigureAwait(false);
        var output = result.Output.Trim();
        if (result.ExitCode != 0 || output.Length == 0)
        {
            throw new InvalidOperationException(
                "The .NET SDK identity probe failed with exit code " +
                $"{result.ExitCode}: {result.Error.Trim()}");
        }

        return output;
    }
}
