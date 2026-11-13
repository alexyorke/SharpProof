using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.DefensiveCodingAnalyzer;

// Test methods and helpers receive runner-owned or test-authored inputs, so the
// repository rules do not apply to them. Directory.Build.props marks a test
// project with SharpProofTestProject and makes the property compiler-visible.
internal static class TestProjects
{
    internal const string Property = "build_property.SharpProofTestProject";

    internal static bool Contains(AnalyzerOptions options)
    {
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        return options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(Property, out var value) &&
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
