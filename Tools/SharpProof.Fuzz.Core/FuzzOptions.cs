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

    public int? Iterations { get; init; } = 100;

    public TimeSpan? Duration { get; init; }

    public int Seed { get; init; } = 12345;

    public string OutputDirectory { get; init; } = DefaultOutputDirectory();

    public int MaxInterestingCases { get; init; } = 100;

    public int MaxInterestingCasesPerFamily { get; init; } = 10;

    public int CheckpointEvery { get; init; } = 100;

    public int Parallelism { get; init; } = DefaultParallelism();

    public bool Quiet { get; init; }

    public bool FailOnFindings { get; init; }

    public bool RepeatAnalyzer { get; init; } = true;

    public static FuzzOptions Parse(string[] args)
    {
        var options = new FuzzOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--iterations":
                    options = options with { Iterations = ReadInt(args, ref i, arg) };
                    break;
                case "--seconds":
                    options = options with { Duration = ReadDuration(args, ref i, arg, TimeSpan.FromSeconds) };
                    break;
                case "--minutes":
                    options = options with { Duration = ReadDuration(args, ref i, arg, TimeSpan.FromMinutes) };
                    break;
                case "--hours":
                    options = options with { Duration = ReadDuration(args, ref i, arg, TimeSpan.FromHours) };
                    break;
                case "--seed":
                    options = options with { Seed = ReadInt(args, ref i, arg) };
                    break;
                case "--out":
                    options = options with { OutputDirectory = ReadString(args, ref i, arg) };
                    break;
                case "--max-interesting":
                    options = options with { MaxInterestingCases = ReadInt(args, ref i, arg) };
                    break;
                case "--max-interesting-per-family":
                    options = options with { MaxInterestingCasesPerFamily = ReadInt(args, ref i, arg) };
                    break;
                case "--checkpoint-every":
                    options = options with { CheckpointEvery = ReadInt(args, ref i, arg) };
                    break;
                case "--parallelism":
                    options = options with { Parallelism = ReadInt(args, ref i, arg) };
                    break;
                case "--quiet":
                    options = options with { Quiet = true };
                    break;
                case "--fail-on-findings":
                    options = options with { FailOnFindings = true };
                    break;
                case "--no-repeat":
                    options = options with { RepeatAnalyzer = false };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

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

    private static int ReadInt(string[] args, ref int index, string option)
    {
        var value = ReadString(args, ref index, option);
        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"{option} expects an integer.");
    }

    private static double ReadDouble(string[] args, ref int index, string option)
    {
        var value = ReadString(args, ref index, option);
        return double.TryParse(value, out var parsed) && double.IsFinite(parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException($"{option} expects a finite non-negative number.");
    }

    private static TimeSpan ReadDuration(
        string[] args,
        ref int index,
        string option,
        Func<double, TimeSpan> createDuration)
    {
        var value = ReadDouble(args, ref index, option);
        try
        {
            return createDuration(value);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException($"{option} expects a duration within TimeSpan range.", ex);
        }
    }

    private static string ReadString(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length) throw new ArgumentException($"{option} expects a value.");

        index++;
        return args[index];
    }

    private static string DefaultOutputDirectory()
    {
        return Path.Combine(
            Environment.CurrentDirectory,
            "artifacts",
            "fuzz",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
    }

    private static int DefaultParallelism()
    {
        return Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
    }
}
