using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
namespace SharpProof.Host;

internal static class TrustedChildEnvironment
{
    private static readonly string[] AllowedVariables =
    [
        "PATH",
        "HOME",
        "TMPDIR",
        "SHARPPROOF_CONTAINER",
        "SHARPPROOF_CONTAINER_CONTRACT",
        "SHARPPROOF_NATIVE_ROOT",
        "DOTNET_CLI_TELEMETRY_OPTOUT"
    ];

    private static readonly string[] UnsafeRuntimeVariables =
    [
        "DOTNET_STARTUP_HOOKS",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE",
        "DOTNET_ROLL_FORWARD",
        "DOTNET_ROLL_FORWARD_TO_PRERELEASE",
        "DOTNET_ROOT_X64",
        "DOTNET_ROOT_X86",
        "DOTNET_MULTILEVEL_LOOKUP",
        "LD_PRELOAD",
        "LD_LIBRARY_PATH",
        "LD_AUDIT"
    ];

    internal static void Apply(
        ProcessStartInfo startInfo,
        string? validatedDotNetHostPath,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        readEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var inheritedValues = AllowedVariables
            .Select(name => (Name: name, Value: readEnvironmentVariable(name)))
            .Where(static item => item.Value is not null)
            .ToArray();
        string? dotNetRoot = null;
        if (validatedDotNetHostPath is not null)
        {
            if (!Path.IsPathFullyQualified(validatedDotNetHostPath) ||
                !string.Equals(
                    Path.GetFileName(validatedDotNetHostPath),
                    "dotnet",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SharpProof child runtime must use a validated absolute dotnet muxer.");
            }
            dotNetRoot = Path.GetDirectoryName(validatedDotNetHostPath);
            if (string.IsNullOrWhiteSpace(dotNetRoot))
            {
                throw new InvalidOperationException(
                    "SharpProof child runtime has no validated installation root.");
            }
        }

        startInfo.Environment.Clear();
        foreach (var item in inheritedValues)
        {
            startInfo.Environment[item.Name] = item.Value;
        }
        if (dotNetRoot is not null)
        {
            startInfo.Environment["DOTNET_ROOT"] = dotNetRoot;
        }
    }

    internal static string? FindUnsafeRuntimeVariable(
        Func<string, string?>? readEnvironmentVariable = null)
    {
        readEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        foreach (var name in UnsafeRuntimeVariables)
        {
            if (!string.IsNullOrWhiteSpace(readEnvironmentVariable(name)))
            {
                return name;
            }
        }
        var dotNetRoot = readEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotNetRoot))
        {
            var processPath = Environment.ProcessPath;
            var processRoot = processPath is null
                ? null
                : Path.GetDirectoryName(processPath);
            if (!Path.IsPathFullyQualified(dotNetRoot) ||
                processRoot is null ||
                !string.Equals(
                    Path.GetFullPath(dotNetRoot),
                    Path.GetFullPath(processRoot),
                    StringComparison.Ordinal))
            {
                return "DOTNET_ROOT";
            }
        }
        return null;
    }
}

internal static class LinuxProcessControlConstants
{
    // Match GNU timeout's conventional status codes at process boundaries.
    internal const int TimeoutExitCode = 124;
}
