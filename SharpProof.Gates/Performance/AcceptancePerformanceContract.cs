using System.Text.Json;

namespace SharpProof.Gates.Performance;

internal sealed record AcceptancePerformanceContract(
    int Warmups,
    int Samples,
    int SmokeWarmups,
    int SmokeSamples,
    double SmokeMaximumRatio,
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
    double ForcedTerminationMilliseconds)
{
    public static AcceptancePerformanceContract Load(string repositoryRoot)
    {
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
            performance.GetProperty("smokeWarmups").GetInt32(),
            performance.GetProperty("smokeSamples").GetInt32(),
            GetPositiveFiniteDouble(
                performance,
                "performance",
                "smokeMaximumRatio"),
            GetPositiveFiniteDouble(
                performance,
                "performance",
                "maximumMedianRatio"),
            GetPositiveFiniteDouble(
                performance,
                "performance",
                "maximumP95Ratio"),
            GetPositiveFiniteDouble(
                performance,
                "performance",
                "maximumRetainedMemoryRatio"),
            performance.GetProperty("maximumRetainedMemoryIncreaseMiB").GetInt32(),
            performance.GetProperty("maximumEnabledRetainedCompilations").GetInt32(),
            performance.GetProperty("maximumEnabledRetainedMemoryIncreaseMiB").GetInt32(),
            performance.GetProperty("ideEdits").GetInt32(),
            GetPositiveFiniteDouble(
                performance,
                "performance",
                "ideEditP95Milliseconds"),
            GetPositiveFiniteDouble(
                performance,
                "performance",
                "ideEditMaximumMilliseconds"),
            GetPositiveFiniteDouble(
                worker,
                "worker",
                "cancellationP95Milliseconds"),
            GetPositiveFiniteDouble(
                worker,
                "worker",
                "forcedTerminationMilliseconds"));
    }

    private static double GetPositiveFiniteDouble(
        JsonElement section,
        string sectionName,
        string propertyName)
    {
        var value = section.GetProperty(propertyName).GetDouble();
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new InvalidDataException(
                $"The performance limit '{sectionName}.{propertyName}' " +
                "must be a finite positive number.");
        }

        return value;
    }
}
