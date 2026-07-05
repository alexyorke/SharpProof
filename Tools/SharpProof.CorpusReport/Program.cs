using System.Diagnostics;
using System.Text.Json;
using SharpProof.Tools.CorpusReport;

var options = CorpusReportOptions.Parse(args);
if (options.ShowHelp || options.Inputs.Count == 0)
{
    Console.Error.WriteLine("Usage: SharpProof.CorpusReport [--output report.json] <project-or-sarif> [more inputs...]");
    return options.Inputs.Count == 0 ? 1 : 0;
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
            RunBuild(input, sarifPath);
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
    {
        Console.WriteLine(json);
    }
    else
    {
        File.WriteAllText(options.OutputPath, json);
    }

    return 0;
}
finally
{
    foreach (var temporaryFile in temporaryFiles)
    {
        TryDelete(temporaryFile);
    }
}

static void RunBuild(string input, string sarifPath)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false
    };

    startInfo.ArgumentList.Add("build");
    startInfo.ArgumentList.Add(input);
    startInfo.ArgumentList.Add("--nologo");
    startInfo.ArgumentList.Add("/p:ErrorLog=" + sarifPath);

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet build.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    var output = outputTask.GetAwaiter().GetResult();
    var error = errorTask.GetAwaiter().GetResult();

    if (!File.Exists(sarifPath))
    {
        throw new InvalidOperationException("dotnet build did not produce a SARIF error log." + Environment.NewLine + output + Environment.NewLine + error);
    }
}

static void TryDelete(string path)
{
    try
    {
        File.Delete(path);
    }
    catch
    {
    }
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
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("--output requires a path.");
                }

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
