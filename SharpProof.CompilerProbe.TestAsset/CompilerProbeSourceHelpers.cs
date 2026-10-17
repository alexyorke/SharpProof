using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.CompilerProbe.TestAsset;

internal static class CompilerProbeSourceHelpers
{
    internal static string GetOption(AnalyzerConfigOptions options, string key)
    {
        if (key is null)
        {
            throw new System.ArgumentNullException(nameof(key));
        }
        if (options is null)
        {
            throw new System.ArgumentNullException(nameof(options));
        }
        return options.TryGetValue(key, out var value) ? value : string.Empty;
    }

    internal static string NormalizePath(string path)
    {
        if (path is null)
        {
            throw new System.ArgumentNullException(nameof(path));
        }
        return path.Replace('\\', '/');
    }
}
