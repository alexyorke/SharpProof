using System.Globalization;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Symbolic;

internal static class AnalyzerConfigurationValueReader
{
    internal static int GetInteger(
        AnalyzerOptions options,
        string key,
        int fallback,
        int minimum)
    {
        return TryGetGlobalOption(options, key, out var value) &&
               TryParseInteger(value, minimum, out var parsed)
            ? parsed
            : fallback;
    }


    internal static bool TryGetGlobalOption(AnalyzerOptions options, string key, out string value)
    {
        try
        {
            var global = options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (TryGetNonEmpty(global, key, out value) ||
                TryGetNonEmpty(global, "build_property." + key, out value))
                return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }

        value = string.Empty;
        return false;
    }

    internal static bool TryParseInteger(string value, int minimum, out int parsed)
    {
        return int.TryParse(
                   value.Trim(),
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out parsed) &&
               parsed >= minimum;
    }

    internal static bool TryGetNonEmpty(AnalyzerConfigOptions options, string key, out string value)
    {
        if (options.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
