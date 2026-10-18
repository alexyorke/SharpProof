using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.CompilerProbe.TestAsset;

internal static class CompilerProbeSourceHelpers
{
    internal static string GetOption(AnalyzerConfigOptions options, string key)
    {
        key = RepositoryParameterGuard.NotNull(key, nameof(key));
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        return options.TryGetValue(key, out var value) ? value : string.Empty;
    }

    internal static string NormalizePath(string path)
    {
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        return path.Replace('\\', '/');
    }
}
