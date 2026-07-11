using System.Diagnostics;
using SharpProof.Tools.Baseline;

BaselineCommandOptions options;
try
{
    options = BaselineCommandOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    WriteUsage();
    return 64;
}

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

var temporaryFiles = new List<string>();
try
{
    switch (options.Command)
    {
        case "generate":
            {
                var current = await LoadCurrentDiagnosticsAsync(options.Inputs, temporaryFiles);
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
                var current = await LoadCurrentDiagnosticsAsync(options.Inputs, temporaryFiles);
                foreach (var explanation in SharpProofBaseline.Explain(baseline, current))
                    Console.WriteLine(FormatExplanation(explanation));

                return 0;
            }

        case "prune":
            {
                var baseline = SharpProofBaseline.ParseBaselineJson(await File.ReadAllTextAsync(options.BaselinePath!));
                var current = await LoadCurrentDiagnosticsAsync(options.Inputs, temporaryFiles);
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
finally
{
    foreach (var temporaryFile in temporaryFiles) TryDelete(temporaryFile);
}

static async Task<BaselineDocument> LoadCurrentDiagnosticsAsync(
    IReadOnlyCollection<string> inputs,
    List<string> temporaryFiles)
{
    var documents = new List<BaselineDocument>();
    foreach (var input in inputs)
    {
        var extension = Path.GetExtension(input);
        if (string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var sarifPath = Path.Combine(Path.GetTempPath(),
                "sharpproof-baseline-" + Guid.NewGuid().ToString("N") + ".sarif");
            temporaryFiles.Add(sarifPath);
            await RunBuildAsync(input, sarifPath);
            documents.Add(SharpProofBaseline.GenerateFromSarifJson(await File.ReadAllTextAsync(sarifPath)));
        }
        else
        {
            documents.Add(SharpProofBaseline.GenerateFromSarifJson(await File.ReadAllTextAsync(input)));
        }
    }

    return SharpProofBaseline.Merge(documents);
}

static async Task RunBuildAsync(string input, string sarifPath)
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

    using var process = Process.Start(startInfo) ??
                        throw new InvalidOperationException("Failed to start dotnet build.");
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var output = await outputTask;
    var error = await errorTask;

    if (!File.Exists(sarifPath))
        throw new InvalidOperationException("dotnet build did not produce a SARIF error log." + Environment.NewLine +
                                            output + Environment.NewLine + error);
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
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;

                case "--baseline":
                case "-b":
                    options.BaselinePath = ReadValue(args, ref i, arg);
                    break;

                case "--output":
                case "-o":
                    options.OutputPath = ReadValue(args, ref i, arg);
                    break;

                case "--sarif":
                case "--input":
                    options.Inputs.Add(ReadValue(args, ref i, arg));
                    break;

                default:
                    options.Inputs.Add(arg);
                    break;
            }
        }

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

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length) throw new ArgumentException(option + " requires a value.");

        return args[++index];
    }
}
