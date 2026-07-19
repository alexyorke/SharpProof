using SharpProof.Tools.Baseline;
using SharpProof.Tools.Shared;

return await ToolCommandHost.RunAsync(
    () => RunAsync(args),
    argumentErrorExitCode: 64,
    Console.Error,
    static _ => WriteUsage());

static async Task<int> RunAsync(string[] args)
{
    var options = BaselineCommandOptions.Parse(args);
    if (options.ShowHelp)
    {
        WriteUsage();
        return 0;
    }

    if (!options.IsValid(out var error))
    {
        Console.Error.WriteLine(error);
        WriteUsage();
        return 64;
    }

    switch (options.Command)
    {
        case "generate":
            {
                var current = await LoadCurrentDiagnosticsAsync(options.Inputs);
                var json = SharpProofBaseline.ToJson(current);
                if (options.OutputPath == null)
                    Console.Write(json);
                else
                    await File.WriteAllTextAsync(options.OutputPath, json);

                return 0;
            }

        case "explain":
            {
                var baseline = SharpProofBaseline.ParseBaselineJson(await File.ReadAllTextAsync(options.BaselinePath!));
                var current = await LoadCurrentDiagnosticsAsync(options.Inputs);
                foreach (var explanation in SharpProofBaseline.Explain(baseline, current))
                    Console.WriteLine(FormatExplanation(explanation));

                return 0;
            }

        case "prune":
            {
                var baseline = SharpProofBaseline.ParseBaselineJson(await File.ReadAllTextAsync(options.BaselinePath!));
                var current = await LoadCurrentDiagnosticsAsync(options.Inputs);
                var result = SharpProofBaseline.Prune(baseline, current);
                var outputPath = options.OutputPath ?? options.BaselinePath!;
                await File.WriteAllTextAsync(outputPath, SharpProofBaseline.ToJson(result.Baseline));
                Console.Error.WriteLine("Kept " + result.Kept + " baseline entries; pruned " + result.Pruned + ".");
                return 0;
            }

        case "migrate":
            {
                var baseline = SharpProofBaseline.ParseBaselineJson(await File.ReadAllTextAsync(options.BaselinePath!));
                var outputPath = options.OutputPath ?? options.BaselinePath!;
                await File.WriteAllTextAsync(outputPath, SharpProofBaseline.ToJson(baseline));
                Console.Error.WriteLine("Migrated baseline evidence to schema v2.");
                return 0;
            }

        default:
            Console.Error.WriteLine("Unknown command '" + options.Command + "'.");
            WriteUsage();
            return 64;
    }
}

static async Task<BaselineDocument> LoadCurrentDiagnosticsAsync(IReadOnlyCollection<string> inputs)
{
    using var materializedInputs = await DotnetSarifBuildRunner.MaterializeAsync(inputs);
    var documents = new List<BaselineDocument>();
    foreach (var input in materializedInputs.Inputs)
        documents.Add(SharpProofBaseline.GenerateFromSarifJson(await File.ReadAllTextAsync(input.SarifPath)));

    return SharpProofBaseline.Merge(documents);
}

static string FormatExplanation(BaselineExplanation explanation)
{
    var state = explanation.Matched ? "matched" : "stale";
    return state + " " +
           explanation.Entry.Id + " " +
           explanation.Entry.Symbol + " " +
           explanation.Entry.Path + ": " +
           explanation.Reason;
}

static void WriteUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine(
        "  SharpProof.Baseline generate [--output SharpProof.Baseline.json] <project|solution|sarif> [...]");
    Console.Error.WriteLine(
        "  SharpProof.Baseline explain --baseline SharpProof.Baseline.json <project|solution|sarif> [...]");
    Console.Error.WriteLine(
        "  SharpProof.Baseline prune --baseline SharpProof.Baseline.json [--output SharpProof.Baseline.json] <project|solution|sarif> [...]");
    Console.Error.WriteLine(
        "  SharpProof.Baseline migrate --baseline SharpProof.Baseline.json [--output SharpProof.Baseline.json]");
}

internal sealed class BaselineCommandOptions
{
    public string Command { get; private set; } = string.Empty;
    public List<string> Inputs { get; } = new();
    public string? BaselinePath { get; private set; }
    public string? OutputPath { get; private set; }
    public bool ShowHelp { get; private set; }

    private static readonly ToolOptionSet<BaselineCommandOptions> OptionSet =
        new ToolOptionSet<BaselineCommandOptions>()
            .Add(static (o, _, _) => o.ShowHelp = true, "--help", "-h")
            .Add(static (o, r, a) => o.BaselinePath = r.RequiredValue(a), "--baseline", "-b")
            .Add(static (o, r, a) => o.OutputPath = r.RequiredValue(a), "--output", "-o")
            .Add(static (o, r, a) => o.Inputs.Add(r.RequiredValue(a)), "--sarif", "--input");

    public static BaselineCommandOptions Parse(string[] args)
    {
        var options = new BaselineCommandOptions();
        if (args.Length == 0)
        {
            options.ShowHelp = true;
            return options;
        }

        if (args[0] is "--help" or "-h")
        {
            options.ShowHelp = true;
            return options;
        }

        options.Command = args[0].Trim().ToLowerInvariant();
        OptionSet.Parse(args, options, 1, static (o, value) => o.Inputs.Add(value));

        return options;
    }

    public bool IsValid(out string error)
    {
        if (Command is not ("generate" or "explain" or "prune" or "migrate"))
        {
            error = "Expected command generate, explain, prune, or migrate.";
            return false;
        }

        if (Command != "migrate" && Inputs.Count == 0)
        {
            error = "At least one project, solution, or SARIF input is required.";
            return false;
        }

        if ((Command == "explain" || Command == "prune" || Command == "migrate") &&
            string.IsNullOrWhiteSpace(BaselinePath))
        {
            error = "--baseline is required for " + Command + ".";
            return false;
        }

        error = string.Empty;
        return true;
    }

}
