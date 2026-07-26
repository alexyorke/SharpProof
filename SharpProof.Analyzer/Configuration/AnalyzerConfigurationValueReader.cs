namespace SharpProof.Analyzer.Configuration;

internal static class AnalyzerConfigurationValueReader {
    internal static bool TryGetGlobalOption(
        AnalyzerOptions options,
        string key,
        out string value) {
        try {
            var global = options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (TryGetNonEmpty(global, key, out value) ||
                TryGetNonEmpty(global, "build_property." + key, out value))
                return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
        }
        value = string.Empty;
        return false;
    }

    internal static bool TryGetNonEmpty(
        AnalyzerConfigOptions options,
        string key,
        out string value) {
        if (options.TryGetValue(key, out var found) &&
            !string.IsNullOrWhiteSpace(found)) {
            value = found;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
