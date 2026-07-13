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

var sarifInputs = new List<SarifCorpusInput>();
var temporaryFiles = new List<string>();
try
{
    foreach (var input in options.Inputs)
    {
        var extension = Path.GetExtension(input);
        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var sarifPath = Path.Combine(Path.GetTempPath(), "sharpproof-" + Guid.NewGuid().ToString("N") + ".sarif");
            temporaryFiles.Add(sarifPath);
            await DotnetSarifBuildRunner.RunAsync(input, sarifPath);
            sarifInputs.Add(new SarifCorpusInput(input, sarifPath));
        }
        else
        {
            sarifInputs.Add(new SarifCorpusInput(input, input));
        }
    }

    var report = SarifCorpusReport.CreateFromSarifFiles(sarifInputs);
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    if (options.OutputPath is null)
        Console.WriteLine(json);
    else
        File.WriteAllText(options.OutputPath, json);

    return 0;
}
finally
{
    foreach (var temporaryFile in temporaryFiles) DotnetSarifBuildRunner.TryDelete(temporaryFile);
}

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
