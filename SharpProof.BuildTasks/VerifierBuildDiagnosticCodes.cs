using Microsoft.Build.Utilities;

namespace SharpProof.BuildTasks;

/// <summary>
/// Stable MSBuild diagnostic identifiers for failures raised by the verifier
/// integration layer. These are intentionally separate from Roslyn analyzer
/// descriptors: the build tasks run after compilation and do not load the
/// analyzer assembly.
/// </summary>
internal static class VerifierBuildDiagnosticCodes
{
    internal const string ExecutionFailure = "SP0051";
    internal const string PublicationTopology = "SP0052";
    internal const string PublishedEvidence = "SP0053";
    internal const string RuntimeConfiguration = "SP0054";

    internal static void LogError(
        TaskLoggingHelper logger,
        string code,
        string message,
        params object[] messageArguments)
    {
        logger.LogError(
            string.Empty,
            code,
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            0,
            message,
            messageArguments);
    }
}
