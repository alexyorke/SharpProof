using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.CompilerProbe.TestAsset;

internal static class CompilerProbeSourceHelpers
{
    internal static string GetOption(AnalyzerConfigOptions options, string key)
    {
        return options.TryGetValue(key, out var value) ? value : string.Empty;
    }

    internal static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
