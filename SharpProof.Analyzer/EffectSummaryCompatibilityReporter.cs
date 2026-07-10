using System.Collections.Concurrent;
using System.Collections.Immutable;
using SharpProof.Analyzer.Configuration;

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
        var issue = new AnalyzerAdditionalFileIssue(
            displayPath,
            $"effect-summary entry '{displaySymbol}' was ignored because {compatibility.Reason}",
            compatibility.ReasonCode);
        _issues.TryAdd(issue, 0);
    }

    internal ImmutableArray<AnalyzerAdditionalFileIssue> GetIssues()
    {
        return _issues.Keys
            .OrderBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.ReasonCode, StringComparer.Ordinal)
            .ThenBy(issue => issue.Reason, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}

internal readonly record struct EffectSummaryCompatibility(
    bool IsCompatible,
    string ReasonCode,
    string Reason)
{
    internal static EffectSummaryCompatibility Compatible { get; } = new(true, string.Empty, string.Empty);

    internal static EffectSummaryCompatibility Incompatible(string reasonCode, string reason)
    {
        return new EffectSummaryCompatibility(false, reasonCode, reason);
    }
}
