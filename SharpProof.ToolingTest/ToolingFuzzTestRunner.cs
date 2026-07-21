using System.Collections.Immutable;
using SharpProof.Tools.Fuzz;

namespace SharpProof.Test;

internal static class ToolingFuzzTestRunner {
    internal static Task<FuzzRunSummary> RunCasesAsync(
        IEnumerable<FuzzCase> fuzzCases,
        FuzzOptions options,
        CancellationToken cancellationToken = default) {
        var cases = fuzzCases.ToImmutableArray();
        var startedUtc = DateTimeOffset.UtcNow;
        return FuzzRunner.RunCoreAsync(
            options,
            startedUtc,
            index => cases[index],
            "explicit_cases",
            cases.Length,
            null,
            cancellationToken);
    }

    internal static Task<ImmutableArray<FuzzCaseAnalysis>> AnalyzeCasesAsync(
        IEnumerable<FuzzCase> fuzzCases,
        bool repeatAnalyzer = true,
        int? parallelism = null,
        CancellationToken cancellationToken = default) {
        var cases = fuzzCases.ToImmutableArray();
        var degree = parallelism is > 0 ? parallelism.Value : FuzzOptions.DefaultParallelism;
        return FuzzRunner.AnalyzeCasesCoreAsync(cases, repeatAnalyzer, degree, cancellationToken);
    }
}
