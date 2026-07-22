using System.Collections.Immutable;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Test;

internal static class AnalyzerConfigurationTestAccessor {
    internal static AnalyzerConfiguration Read(ImmutableDictionary<string, string> globalOptions)
        => AnalyzerConfiguration.FromOptions(AnalyzerTestHost.CreateAnalyzerOptions(globalOptions));
}
