using System.Text.Json;

namespace SharpProof.Gates.Performance;

public sealed record AcceptancePerformanceContract(
    int Warmups,
    int Samples,
    double MaximumMedianRatio,
    double MaximumP95Ratio,
    double MaximumRetainedMemoryRatio,
    int MaximumRetainedMemoryIncreaseMiB,
    int MaximumEnabledRetainedCompilations,
    int MaximumEnabledRetainedMemoryIncreaseMiB,
    int IdeEdits,
    double IdeEditP95Milliseconds,
    double IdeEditMaximumMilliseconds,
    double CancellationP95Milliseconds,
    double ForcedTerminationMilliseconds) {
    public static AcceptancePerformanceContract Load(string repositoryRoot) {
        var path = Path.Combine(
            repositoryRoot,
            "eng",
            "acceptance",
            "contract.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var performance = document.RootElement.GetProperty("performance");
        var worker = document.RootElement.GetProperty("worker");
        return new AcceptancePerformanceContract(
            performance.GetProperty("warmups").GetInt32(),
            performance.GetProperty("samples").GetInt32(),
            performance.GetProperty("maximumMedianRatio").GetDouble(),
            performance.GetProperty("maximumP95Ratio").GetDouble(),
            performance.GetProperty("maximumRetainedMemoryRatio").GetDouble(),
            performance.GetProperty("maximumRetainedMemoryIncreaseMiB").GetInt32(),
            performance.GetProperty("maximumEnabledRetainedCompilations").GetInt32(),
            performance.GetProperty("maximumEnabledRetainedMemoryIncreaseMiB").GetInt32(),
            performance.GetProperty("ideEdits").GetInt32(),
            performance.GetProperty("ideEditP95Milliseconds").GetDouble(),
            performance.GetProperty("ideEditMaximumMilliseconds").GetDouble(),
            worker.GetProperty("cancellationP95Milliseconds").GetDouble(),
            worker.GetProperty("forcedTerminationMilliseconds").GetDouble());
    }
}
