using SharpProof.Worker;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SharpProof.Gates.Corpus;
using SharpProof.Ir;
using SharpProof.Host;

namespace SharpProof.Gates;

internal static class Program
{
    private static RepositoryLogger Log => new(Console.Out, Console.Error);
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The executable boundary converts unexpected gate failures into a stable exit code and diagnostic.")]
    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This developer-facing repository gate is not localized.")]
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The gate writes short logger diagnostics synchronously before returning its process exit code.")]
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new ConsoleCancellationScope();

        try
        {
            var root = RepositoryLayout.FindRoot();
            var command = args.Length == 0 ? "corpus" : args[0];
            if (command is "exception-oracle" or "allocation-oracle" or "purity-oracle" or "capability-oracle" or "effectcontract-oracle")
            {
                // --source widens one pinned source to every method it declares.
                var wholeSource = args.Length == 3 && args[1] == "--source" ? args[2] : null;
                var maximumMethods = args.Length == 1 || wholeSource != null ? 0 : args.Length == 3 && args[1] == "--limit"
                    ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)
                    : throw new ArgumentException("Use exception-oracle, allocation-oracle, purity-oracle, capability-oracle or " +
                        "effectcontract-oracle [--limit <method-count> | --source <source-id>].", nameof(args));
                var result = await NativeEffectOracleGate.RunAsync(root, cancellation.Token, maximumMethods, allocations: command == "allocation-oracle",
                    purity: command == "purity-oracle", capabilities: command == "capability-oracle",
                    summary: command == "effectcontract-oracle", wholeSource: wholeSource).ConfigureAwait(false);
                if (wholeSource != null)
                {
                    // Thousands of rows would bury the summary.
                    result = result with { Rows = [.. result.Rows.Where(static row => row.RuntimeOracle == "Contradiction")] };
                }
                Log.Info(JsonSerializer.Serialize(result, SharpProofJsonDefaults.Indented));
                return result.Passed ? 0 : 1;
            }
            if (command == "corpus")
            {
                var wallTime = System.Diagnostics.Stopwatch.StartNew();
                var result = await CorpusGate.RunAsync(root, cancellation.Token).ConfigureAwait(false);
                Log.Error($"Corpus gate wall time: {wallTime.Elapsed.TotalSeconds:F1}s.");
                foreach (var coverage in result.OpenSourceCoverage)
                {
                    Log.Error($"Corpus source {coverage.SourceId}: {coverage.Methods} methods, " +
                        $"{coverage.Supported} supported, {coverage.Proven} Proven, " +
                        $"{coverage.Refuted} Refuted, {coverage.Unknown} Unknown.");
                }
                Log.Info(
                    JsonSerializer.Serialize(result, SharpProofJsonDefaults.Indented));
                return result.Passed ? 0 : 1;
            }
            if (command == "perf-baseline")
            {
                var output = args.Length == 3 && args[1] == "--output" ? args[2]
                    : args.Length == 1 ? Path.Combine(root, "artifacts", "perf")
                    : throw new ArgumentException("Use perf-baseline [--output <directory>].", nameof(args));
                var report = await PerfBaseline.MeasureAsync(root, cancellation.Token).ConfigureAwait(false);
                var (verdict, failed) = PerfBaseline.Compare(root, report);
                await PerfBaseline.WriteAsync(output, report, PerfBaseline.Summarize(report, verdict), cancellation.Token).ConfigureAwait(false);
                Log.Info(verdict);
                return failed ? 1 : 0;
            }
            if (command == "perf-self")
            {
                var perf = args.Length == 3 && args[1] == "--perf" ? args[2]
                    : args.Length == 1 ? Path.Combine(root, "artifacts", "perf")
                    : throw new ArgumentException("Use perf-self [--perf <directory>].", nameof(args));
                var projects = PerfSelf.Load(Path.Combine(perf, "self"), Path.Combine(perf, "self-builds.json"));
                var summary = PerfSelf.Summarize(projects);
                await File.WriteAllTextAsync(Path.Combine(perf, "self-summary.md"), summary, cancellation.Token).ConfigureAwait(false);
                Log.Info(summary);
                // Outcomes never fail this advisory step; no result at all means the lane itself is broken.
                if (projects.All(static project => project.Response == null))
                {
                    Log.Error("perf-self found no published worker result; the self-verification lane is broken.");
                    return 1;
                }
                return 0;
            }
            if (command == "corpus-update")
            {
                await CorpusGate.WriteActualSnapshotAsync(root, cancellation.Token)
                    .ConfigureAwait(false);
                Log.Info("Updated the canonical corpus snapshot.");
                return 0;
            }
            Log.Error("Usage: SharpProof.Gates [corpus|corpus-update|perf-baseline|perf-self|exception-oracle|allocation-oracle|purity-oracle|" +
                "capability-oracle|effectcontract-oracle] [--limit <method-count> | --source <source-id>]");
            return 2;
        }
        catch (Exception exception)
        {
            Log.Error(exception);
            return 1;
        }

    }
}
