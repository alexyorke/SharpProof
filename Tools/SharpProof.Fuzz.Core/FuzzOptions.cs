namespace SharpProof.Tools.Fuzz;

public sealed record FuzzOptions {
    public static string Usage { get; } = LoadResource("SharpProof.Fuzz.Usage.txt");

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

    public static FuzzOptions Parse(string[] args) {
        var options = new FuzzOptions();
        for (var index = 0; index < args.Length; index++) {
            var option = args[index];
            switch (option) {
                case "--iterations": options.Iterations = ReadInt(ReadValue(args, ref index, option), option); break;
                case "--seconds": options.Duration = ReadDuration(ReadValue(args, ref index, option), option, TimeSpan.FromSeconds); break;
                case "--minutes": options.Duration = ReadDuration(ReadValue(args, ref index, option), option, TimeSpan.FromMinutes); break;
                case "--hours": options.Duration = ReadDuration(ReadValue(args, ref index, option), option, TimeSpan.FromHours); break;
                case "--seed": options.Seed = ReadInt(ReadValue(args, ref index, option), option); break;
                case "--out": options.OutputDirectory = ReadValue(args, ref index, option); break;
                case "--max-interesting": options.MaxInterestingCases = ReadInt(ReadValue(args, ref index, option), option); break;
                case "--max-interesting-per-family":
                    options.MaxInterestingCasesPerFamily = ReadInt(ReadValue(args, ref index, option),
                    option); break;
                case "--checkpoint-every": options.CheckpointEvery = ReadInt(ReadValue(args, ref index, option), option); break;
                case "--parallelism": options.Parallelism = ReadInt(ReadValue(args, ref index, option), option); break;
                case "--quiet": options.Quiet = true; break;
                case "--fail-on-findings": options.FailOnFindings = true; break;
                case "--no-repeat": options.RepeatAnalyzer = false; break;
                default: throw new ArgumentException($"Unknown option '{option}'.");
            }
        }
        if (options.Iterations < 0) throw new ArgumentException("--iterations must be non-negative.");

        if (options.MaxInterestingCases < 0) throw new ArgumentException("--max-interesting must be non-negative.");

        if (options.MaxInterestingCasesPerFamily < 0)
            throw new ArgumentException("--max-interesting-per-family must be non-negative.");

        if (options.CheckpointEvery < 0) throw new ArgumentException("--checkpoint-every must be non-negative.");

        if (options.Parallelism <= 0) throw new ArgumentException("--parallelism must be positive.");

        if (options.Iterations == 0 && options.Duration is null)
            throw new ArgumentException("Duration-only runs need --seconds, --minutes, or --hours when --iterations is 0.");

        return options;
    }
    private static string ReadValue(string[] args, ref int index, string option) =>
        ++index < args.Length ? args[index] : throw new ArgumentException($"{option} expects a value.");

    private static int ReadInt(string value, string option) =>
        int.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"{option} expects an integer.");

    private static double ReadDouble(string value, string option) =>
        double.TryParse(value, out var parsed) &&
        double.IsFinite(parsed) && parsed >= 0
            ? parsed
            : throw new ArgumentException($"{option} expects a finite non-negative number.");

    private static TimeSpan ReadDuration(string value, string option, Func<double, TimeSpan> createDuration) {
        var duration = ReadDouble(value, option);
        try {
            return createDuration(duration);
        }
        catch (OverflowException ex) {
            throw new ArgumentException($"{option} expects a duration within TimeSpan range.", ex);
        }
    }
    private static string DefaultOutputDirectory() => Path.Combine(
        Environment.CurrentDirectory, "artifacts", "fuzz", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));

    internal static string LoadResource(string name) {
        using var stream = typeof(FuzzOptions).Assembly.GetManifestResourceStream(name) ??
                           throw new InvalidOperationException($"Embedded resource '{name}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd().TrimEnd('\r', '\n');
    }
}
