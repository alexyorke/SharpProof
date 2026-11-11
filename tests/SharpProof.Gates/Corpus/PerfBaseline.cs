using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

internal sealed record PerfBaselineReport
{
    public int SchemaVersion { get; init; } = 1;
    public string Input { get; init; } = "oss-corpus";
    public string Runtime { get; init; } = Environment.Version.ToString();
    public int ProcessorCount { get; init; } = Environment.ProcessorCount;
    public int Callables { get; init; }
    public long TotalRlimit { get; init; }
    public long TotalElapsedMilliseconds { get; init; }
    public long ProjectElapsedMilliseconds { get; init; }
    public ImmutableArray<WorkerCallableCost> CallableCosts { get; init; } = [];
}

// Measures verification cost on the pinned open-source corpus. Solver work
// (Z3 rlimit units) is the comparable figure: one lane verifies callables in a
// fixed order, so it is deterministic for a fixed solver build and input. Wall
// time is reported for context only; shared runners make it noisy.
internal static class PerfBaseline
{
    internal const string ReferencePath = "eng/acceptance/perf-baseline.json";
    private static readonly JsonSerializerOptions s_reportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static async Task<PerfBaselineReport> MeasureAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var document = OpenSourceCorpusCatalog.Load(repositoryRoot);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, cancellationToken, purity: true);
        WorkerVerifyResponse? observed = null;
        _ = await NativeCorpusVerifier.VerifyAsync(compilation, cancellationToken,
            new WorkerBudgets { MaxParallelism = 1 }, response => observed = response).ConfigureAwait(false);
        var response = observed ?? throw new InvalidDataException("The corpus produced no worker response.");
        var costs = response.Summary.Costs?.Callables ?? [];
        return new PerfBaselineReport
        {
            Callables = costs.Length,
            TotalRlimit = costs.Sum(static cost => cost.ConsumedRlimit),
            TotalElapsedMilliseconds = costs.Sum(static cost => cost.ElapsedMilliseconds),
            ProjectElapsedMilliseconds = response.Summary.ElapsedMilliseconds,
            CallableCosts = costs
        };
    }

    // Compares total solver work with the reviewed reference. Returns the
    // verdict line and whether it is a failure that the reference enforces.
    internal static (string Verdict, bool Failed) Compare(string repositoryRoot, PerfBaselineReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(report);
        var path = Path.Combine(repositoryRoot, ReferencePath);
        if (!File.Exists(path))
        {
            return ("No reference at " + ReferencePath + "; nothing to compare.", false);
        }
        var reference = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var expected = reference["totalRlimit"]!.GetValue<long>();
        var allowed = reference["maximumIncreasePercent"]!.GetValue<int>();
        var enforced = reference["enforced"]!.GetValue<bool>();
        var change = expected == 0 ? 0 : (report.TotalRlimit - expected) * 100.0 / expected;
        var regressed = change > allowed;
        var outcome = regressed ? enforced ? "regression." : "regression (advisory)." : "within the limit.";
        var verdict = string.Create(CultureInfo.InvariantCulture,
            $"Total solver work {report.TotalRlimit:N0} vs reference {expected:N0} ({change:+0.0;-0.0}%, limit +{allowed}%): {outcome}");
        return (verdict, regressed && enforced);
    }

    internal static string Summarize(PerfBaselineReport report, string verdict)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(verdict);
        var byRlimit = report.CallableCosts.OrderByDescending(static cost => cost.ConsumedRlimit).ToImmutableArray();
        var rlimits = report.CallableCosts.Select(static cost => cost.ConsumedRlimit).Order().ToImmutableArray();
        var times = report.CallableCosts.Select(static cost => cost.ElapsedMilliseconds).Order().ToImmutableArray();
        var text = new StringBuilder();
        text.AppendLine("## Verification performance (open-source corpus)").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{verdict}").AppendLine();
        text.AppendLine("| Measure | Callables | Total | p50 | p95 | Max |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| Solver work (rlimit) | {report.Callables} | {report.TotalRlimit:N0} | {Percentile(rlimits, 50):N0} | {Percentile(rlimits, 95):N0} | {Percentile(rlimits, 100):N0} |");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"| Time per callable (ms) | {report.Callables} | {report.TotalElapsedMilliseconds:N0} | {Percentile(times, 50):N0} | {Percentile(times, 95):N0} | {Percentile(times, 100):N0} |");
        text.AppendLine().AppendLine(CultureInfo.InvariantCulture,
            $"Project wall time: {report.ProjectElapsedMilliseconds:N0} ms on {report.ProcessorCount} processors, .NET {report.Runtime}.").AppendLine();
        text.AppendLine("| Costliest callables | rlimit | ms |").AppendLine("| --- | ---: | ---: |");
        foreach (var cost in byRlimit.Take(10))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| `{cost.CallableId}` | {cost.ConsumedRlimit:N0} | {cost.ElapsedMilliseconds:N0} |");
        }
        return text.ToString();
    }

    internal static async Task WriteAsync(string outputDirectory, PerfBaselineReport report, string summary, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(summary);
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "baseline.json"),
            JsonSerializer.Serialize(report, s_reportOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary.md"), summary, cancellationToken).ConfigureAwait(false);
    }

    private static long Percentile(ImmutableArray<long> sorted, int /* SPINT001: all values valid - the index is clamped */ percent)
    {
        if (sorted.IsDefaultOrEmpty)
        {
            return 0;
        }
        var index = (int)Math.Ceiling(percent / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
