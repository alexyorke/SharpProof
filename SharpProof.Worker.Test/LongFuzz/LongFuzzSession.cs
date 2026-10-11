using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

// Long-running fuzz campaigns are explicit, environment-driven NUnit tests.
// They never fail on a finding: each finding is appended to hits.jsonl,
// written as a standalone .cs file and echoed as a GitHub workflow command
// so it is visible in the live log while the campaign keeps going.
//
//   SHARPPROOF_LONGFUZZ_SEED_START  first case seed (default 1)
//   SHARPPROOF_LONGFUZZ_SEED_END    exclusive last case seed (default int.MaxValue)
//   SHARPPROOF_LONGFUZZ_MINUTES     wall-clock budget for the whole test run (default 5)
//   SHARPPROOF_LONGFUZZ_OUTPUT      output directory (default artifacts/long-fuzz under
//                                   the repository root; relative paths resolve there too)
//
// Output: hits.jsonl (one JSON object per finding), hits/*.cs (one
// standalone source per finding), stats-<harness>.json (refreshed every 50
// cases) and events.log (the live progress and workflow-command lines).
internal sealed class LongFuzzSession
{
    internal const string Category = "LongFuzz";
    internal const string SeedStartVariable = "SHARPPROOF_LONGFUZZ_SEED_START";
    internal const string SeedEndVariable = "SHARPPROOF_LONGFUZZ_SEED_END";
    internal const string MinutesVariable = "SHARPPROOF_LONGFUZZ_MINUTES";
    internal const string OutputVariable = "SHARPPROOF_LONGFUZZ_OUTPUT";
    private const int StatsInterval = 50;

    private static readonly object s_sync = new();
    private static DateTime? s_runDeadline;

    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _hitKinds = new(StringComparer.Ordinal);
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly DateTime _deadline;
    private int _cases;
    private int _lastSeed;

    // A harness shares the remaining run budget with the harnesses that are
    // ordered after it, so a selection of both harnesses ends on time.
    internal LongFuzzSession(string harness, int harnessIndex, int harnessCount)
    {
        Harness = harness;
        SeedStart = ReadInt(SeedStartVariable, 1);
        SeedEnd = ReadInt(SeedEndVariable, int.MaxValue);
        Output = ResolveOutput();
        Directory.CreateDirectory(Path.Combine(Output, "hits"));
        var now = DateTime.UtcNow;
        DateTime runDeadline;
        lock (s_sync)
        {
            s_runDeadline ??= now + TimeSpan.FromMinutes(ReadDouble(MinutesVariable, 5));
            runDeadline = s_runDeadline.Value;
        }
        var remaining = runDeadline > now ? runDeadline - now : TimeSpan.Zero;
        _deadline = now + (remaining / Math.Max(1, harnessCount - harnessIndex));
        _lastSeed = SeedStart - 1;
        Progress("LongFuzz " + harness + ": seeds from " + SeedStart + " until " +
            _deadline.ToString("u", CultureInfo.InvariantCulture) + ", output " + Output);
    }

    internal string Harness { get; }
    internal int SeedStart { get; }
    internal int SeedEnd { get; }
    internal string Output { get; }
    internal int Cases => _cases;
    internal int Hits => _hitKinds.Values.Sum();

    internal IEnumerable<int> Seeds()
    {
        for (var seed = SeedStart; seed < SeedEnd && DateTime.UtcNow < _deadline; seed++)
        {
            if (_cases > 0 && _cases % StatsInterval == 0)
            {
                WriteStats();
            }
            _lastSeed = seed;
            _cases++;
            yield return seed;
        }
    }

    internal void Count(string key)
    {
        _counts[key] = _counts.GetValueOrDefault(key) + 1;
    }

    internal void RecordHit(LongFuzzHit hit)
    {
        _hitKinds[hit.Kind] = _hitKinds.GetValueOrDefault(hit.Kind) + 1;
        var name = Harness + "-seed" + hit.Seed.ToString(CultureInfo.InvariantCulture) + "-" + hit.Kind + ".cs";
        var sourcePath = Path.Combine(Output, "hits", name);
        var header = new StringBuilder();
        header.Append("// LongFuzz ").Append(Harness).Append(" seed ").Append(hit.Seed).Append(" kind ").AppendLine(hit.Kind);
        header.Append("// claim: ").AppendLine(hit.Claim);
        header.Append("// worker: ").Append(hit.WorkerOutcome).Append(' ').AppendLine(hit.WorkerReason);
        header.Append("// runtime: ").AppendLine(OneLine(hit.Runtime));
        foreach (var line in hit.Details.Split('\n'))
        {
            header.Append("// ").AppendLine(line.TrimEnd('\r'));
        }
        lock (s_sync)
        {
            File.WriteAllText(sourcePath, header + hit.Source + "\n");
            var record = new LongFuzzHitRecord(
                Harness, hit.Kind, hit.Severity, hit.Seed, hit.Claim, hit.WorkerOutcome, hit.WorkerReason, hit.Runtime,
                hit.Details, "hits/" + name, hit.Source, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            File.AppendAllText(Path.Combine(Output, "hits.jsonl"), JsonSerializer.Serialize(record) + "\n");
        }
        Progress("::" + hit.Severity + "::" + Escape("LongFuzz " + Harness + " seed " + hit.Seed + " " + hit.Kind + " [" +
            hit.Claim + "] worker=" + hit.WorkerOutcome + " " + hit.WorkerReason + " runtime=" + OneLine(hit.Runtime) +
            " :: " + OneLine(hit.Details) + " -> hits/" + name));
    }

    internal void WriteStats()
    {
        var stats = new LongFuzzStats(
            Harness, SeedStart, _lastSeed, _cases, Hits, Math.Round(_elapsed.Elapsed.TotalMinutes, 2),
            new SortedDictionary<string, int>(_hitKinds, StringComparer.Ordinal),
            new SortedDictionary<string, int>(_counts, StringComparer.Ordinal));
        lock (s_sync)
        {
            File.WriteAllText(Path.Combine(Output, "stats-" + Harness + ".json"), JsonSerializer.Serialize(stats) + "\n");
        }
    }

    internal string Finish()
    {
        WriteStats();
        var summary = "LongFuzz " + Harness + ": " + _cases + " cases, seeds " + SeedStart + ".." + _lastSeed + ", " + Hits +
            " hits (" + string.Join(", ", _hitKinds.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value)) + ") in " +
            _elapsed.Elapsed.TotalMinutes.ToString("F1", CultureInfo.InvariantCulture) + " min";
        Progress(summary);
        return summary;
    }

    // vstest does not stream test-host output at its default verbosity, so
    // every progress line is also appended to events.log for `tail -F`.
    internal void Progress(string line)
    {
        TestContext.Progress.WriteLine(line);
        lock (s_sync)
        {
            File.AppendAllText(Path.Combine(Output, "events.log"), line + "\n");
        }
    }

    private static string OneLine(string text)
    {
        var single = text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", " | ", StringComparison.Ordinal);
        return single.Length > 600 ? single[..600] + "..." : single;
    }

    // GitHub workflow command data must stay on one line.
    private static string Escape(string text)
    {
        return text.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
    }

    private static string ResolveOutput()
    {
        var configured = Environment.GetEnvironmentVariable(OutputVariable);
        var root = TestRepository.FindRoot();
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(root, "artifacts", "long-fuzz");
        }
        return Path.IsPathRooted(configured) ? configured : Path.Combine(root, configured);
    }

    private static int ReadInt(string name, int fallback)
    {
        var text = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(text) ? fallback : int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static double ReadDouble(string name, double fallback)
    {
        var text = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(text) ? fallback : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

internal sealed record LongFuzzHit(
    string Kind, string Severity, int Seed, string Claim, string WorkerOutcome, string WorkerReason, string Runtime,
    string Details, string Source)
{
    internal const string Error = "error";
    internal const string Warning = "warning";
}

internal sealed record LongFuzzHitRecord(
    string Harness, string Kind, string Severity, int Seed, string Claim, string WorkerOutcome, string WorkerReason,
    string Runtime, string Details, string SourceFile, string Source, string FoundUtc);

internal sealed record LongFuzzStats(
    string Harness, int SeedStart, int LastSeed, int Cases, int Hits, double ElapsedMinutes,
    SortedDictionary<string, int> HitKinds, SortedDictionary<string, int> Counts);
