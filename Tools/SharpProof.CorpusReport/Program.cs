using System.Text.Json;
using SharpProof.Tools.CorpusReport;
using SharpProof.Tools.Shared;

return await ToolCommandHost.RunAsync(
    () => RunAsync(args),
    argumentErrorExitCode: 64,
    Console.Error,
    static _ => WriteUsage());

static async Task<int> RunAsync(string[] args) {
    var options = CorpusReportOptions.Parse(args);
    if (options.ShowHelp || options.Inputs.Count == 0) {
        WriteUsage();
        return options.ShowHelp ? 0 : 1;
    }

    using var materializedInputs = await DotnetSarifBuildRunner.MaterializeAsync(options.Inputs);
    var sarifInputs = materializedInputs.Inputs
        .Select(static input => new SarifCorpusInput(input.InputName, input.SarifPath))
        .ToArray();

    var report = SarifCorpusReport.CreateFromSarifFiles(sarifInputs);
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    if (options.OutputPath is null)
        Console.WriteLine(json);
    else
        File.WriteAllText(options.OutputPath, json);

    return 0;
}

static void WriteUsage() => Console.Error.WriteLine(
        "Usage: SharpProof.CorpusReport [--output report.json] <project-or-sarif> [more inputs...]");

internal sealed class CorpusReportOptions {
    public List<string> Inputs { get; } = new();
    public string? OutputPath { get; private set; }
    public bool ShowHelp { get; private set; }

    private static readonly ToolOptionSet<CorpusReportOptions> OptionSet =
        new ToolOptionSet<CorpusReportOptions>()
            .Add(static (o, _, _) => o.ShowHelp = true, "--help", "-h")
            .Add(static (o, r, _) => o.OutputPath =
                r.RequiredValue("--output", "--output requires a path."), "--output", "-o");

    public static CorpusReportOptions Parse(string[] args) {
        var options = new CorpusReportOptions();
        OptionSet.Parse(args, options, positional: static (o, value) => o.Inputs.Add(value));

        return options;
    }
}
