using System.Collections.Immutable;
using SharpProof.Tools.Fuzz;
namespace SharpProof.Test;
internal static class ToolingFuzzTestRunner {
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
