using System.Text.Json;
using SharpProof.Tools.CorpusReport;
using SharpProof.Tools.Shared;

CorpusReportOptions options;
try
{
    options = CorpusReportOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    WriteUsage();
    return 64;
}

if (options.ShowHelp || options.Inputs.Count == 0)
{
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

static void WriteUsage()
{
    Console.Error.WriteLine(
        "Usage: SharpProof.CorpusReport [--output report.json] <project-or-sarif> [more inputs...]");
}

internal sealed class CorpusReportOptions
{
    public List<string> Inputs { get; } = new();
    public string? OutputPath { get; private set; }
    public bool ShowHelp { get; private set; }

    public static CorpusReportOptions Parse(string[] args)
    {
        var options = new CorpusReportOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--help" || arg == "-h")
            {
                options.ShowHelp = true;
            }
            else if (arg == "--output" || arg == "-o")
            {
                if (i + 1 >= args.Length) throw new ArgumentException("--output requires a path.");

                options.OutputPath = args[++i];
            }
            else
            {
                options.Inputs.Add(arg);
            }
        }

        return options;
    }
}
