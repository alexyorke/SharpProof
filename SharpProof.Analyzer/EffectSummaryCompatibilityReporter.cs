namespace SharpProof.Analyzer;

internal sealed class EffectSummaryCompatibilityReporter
{
    private readonly ConcurrentDictionary<AnalyzerAdditionalFileIssue, byte> _issues = new();

    internal void Report(
        string path,
        string symbol,
        EffectSummaryCompatibility compatibility)
    {
        if (compatibility.IsCompatible || string.IsNullOrWhiteSpace(compatibility.ReasonCode)) return;

        var displayPath = string.IsNullOrWhiteSpace(path) ? "<unknown>" : path;
        var displaySymbol = string.IsNullOrWhiteSpace(symbol) ? "<unknown>" : symbol;
        Report(
            displayPath,
            $"effect-summary entry '{displaySymbol}' was ignored because {compatibility.Reason}",
            compatibility.ReasonCode);
    }

    internal void Report(
        string path,
        string reason,
        string reasonCode = "invalid_additional_file") =>
        _issues.TryAdd(new AnalyzerAdditionalFileIssue(path ?? string.Empty, reason, reasonCode), 0);

    internal ImmutableArray<AnalyzerAdditionalFileIssue> GetIssues()
    {
        return _issues.Keys
            .OrderBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.ReasonCode, StringComparer.Ordinal)
            .ThenBy(issue => issue.Reason, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}

internal static class AnalyzerAdditionalFileText
{
    internal static bool TryRead(
        AdditionalText additionalFile,
        CancellationToken cancellationToken,
        EffectSummaryCompatibilityReporter reporter,
        out string text)
    {
        try
        {
            text = additionalFile.GetText(cancellationToken)?.ToString() ?? string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            text = string.Empty;
            reporter.Report(additionalFile.Path, "file contents could not be read");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(text)) return true;

        reporter.Report(additionalFile.Path, "file is empty");
        return false;
    }
}

internal readonly record struct AnalyzerAdditionalFileIssue(
    string Path,
    string Reason,
    string ReasonCode = "invalid_additional_file");

internal readonly record struct EffectSummaryCompatibility(
    bool IsCompatible,
    string ReasonCode,
    string Reason)
{
    internal static EffectSummaryCompatibility Compatible { get; } = new(true, string.Empty, string.Empty);

    internal static EffectSummaryCompatibility Incompatible(string reasonCode, string reason) =>
        new EffectSummaryCompatibility(false, reasonCode, reason);
}
