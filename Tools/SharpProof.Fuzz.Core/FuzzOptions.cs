namespace SharpProof.Tools.Fuzz;

public sealed record FuzzOptions
{
    public const string Usage = """
                                Usage: SharpProof.Fuzz [options]

                                Options:
                                  --iterations <n>         Number of generated cases. Use 0 for duration-only runs. Default: 100.
                                  --seconds <n>            Run duration in seconds.
                                  --minutes <n>            Run duration in minutes.
                                  --hours <n>              Run duration in hours.
                                  --seed <n>               Deterministic random seed. Default: 12345.
                                  --out <path>             Output directory. Default: artifacts/fuzz/<timestamp>.
                                  --max-interesting <n>    Maximum source files saved for findings. Default: 100.
                                  --max-interesting-per-family <n>
                                                           Maximum saved interesting cases per family. Default: 10.
                                  --checkpoint-every <n>   Write summary.partial.json and coverage.partial.json every N analyzed cases. Default: 100. Use 0 to disable.
                                  --parallelism <n>        Maximum concurrent analyzer tasks. Default: 4 or processor count if lower.
                                  --quiet                  Suppress progress output.
                                  --fail-on-findings       Exit with code 2 when findings are found.
                                  --no-repeat              Do not run repeated analyzer determinism checks.
                                """;

    public int? Iterations { get; set; } = 100;

    public TimeSpan? Duration { get; set; }

    public int Seed { get; set; } = 12345;

    public string OutputDirectory { get; set; } = DefaultOutputDirectory();

    public int MaxInterestingCases { get; set; } = 100;

    public int MaxInterestingCasesPerFamily { get; set; } = 10;

    public int CheckpointEvery { get; set; } = 100;

    public int Parallelism { get; set; } = DefaultParallelism;

    internal static int DefaultParallelism => Math.Max(1, Math.Min(Environment.ProcessorCount, 4));

    public bool Quiet { get; set; }

    public bool FailOnFindings { get; set; }

    public bool RepeatAnalyzer { get; set; } = true;

    private static readonly ToolOptionSet<FuzzOptions> OptionSet = new ToolOptionSet<FuzzOptions>()
        .Add(static (o, r, a) => o.Iterations = ReadInt(r, a), "--iterations")
        .Add(static (o, r, a) => o.Duration = ReadDuration(r, a, TimeSpan.FromSeconds), "--seconds")
        .Add(static (o, r, a) => o.Duration = ReadDuration(r, a, TimeSpan.FromMinutes), "--minutes")
        .Add(static (o, r, a) => o.Duration = ReadDuration(r, a, TimeSpan.FromHours), "--hours")
        .Add(static (o, r, a) => o.Seed = ReadInt(r, a), "--seed")
        .Add(static (o, r, a) => o.OutputDirectory = r.RequiredValue(a, $"{a} expects a value."), "--out")
        .Add(static (o, r, a) => o.MaxInterestingCases = ReadInt(r, a), "--max-interesting")
        .Add(static (o, r, a) => o.MaxInterestingCasesPerFamily = ReadInt(r, a), "--max-interesting-per-family")
        .Add(static (o, r, a) => o.CheckpointEvery = ReadInt(r, a), "--checkpoint-every")
        .Add(static (o, r, a) => o.Parallelism = ReadInt(r, a), "--parallelism")
        .Add(static (o, _, _) => o.Quiet = true, "--quiet")
        .Add(static (o, _, _) => o.FailOnFindings = true, "--fail-on-findings")
        .Add(static (o, _, _) => o.RepeatAnalyzer = false, "--no-repeat");

    public static FuzzOptions Parse(string[] args)
    {
        var options = new FuzzOptions();
        OptionSet.Parse(args, options);

        if (options.Iterations < 0) throw new ArgumentException("--iterations must be non-negative.");

        if (options.MaxInterestingCases < 0) throw new ArgumentException("--max-interesting must be non-negative.");

        if (options.MaxInterestingCasesPerFamily < 0)
            throw new ArgumentException("--max-interesting-per-family must be non-negative.");

        if (options.CheckpointEvery < 0) throw new ArgumentException("--checkpoint-every must be non-negative.");

        if (options.Parallelism <= 0) throw new ArgumentException("--parallelism must be positive.");

        if (options.Iterations == 0 && options.Duration is null)
            throw new ArgumentException(
                "Duration-only runs need --seconds, --minutes, or --hours when --iterations is 0.");

        return options;
    }

    private static int ReadInt(ToolArgumentReader reader, string option)
    {
        var value = reader.RequiredValue(option, $"{option} expects a value.");
        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"{option} expects an integer.");
    }

    private static double ReadDouble(ToolArgumentReader reader, string option)
    {
        var value = reader.RequiredValue(option, $"{option} expects a value.");
        return double.TryParse(value, out var parsed) && double.IsFinite(parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException($"{option} expects a finite non-negative number.");
    }

    private static TimeSpan ReadDuration(
        ToolArgumentReader reader,
        string option,
        Func<double, TimeSpan> createDuration)
    {
        var value = ReadDouble(reader, option);
        try
        {
            return createDuration(value);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException($"{option} expects a duration within TimeSpan range.", ex);
        }
    }

    private static string DefaultOutputDirectory() =>
        Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "fuzz",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));

}
