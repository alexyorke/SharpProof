namespace SharpProof.Tools.Fuzz;


public static class Program {
    public static async Task<int> Main(string[] args) => await ToolCommandHost.RunAsync(
            () => RunAsync(args),
            argumentErrorExitCode: 64,
            Console.Error,
            static error => error.WriteLine(FuzzOptions.Usage));

    private static async Task<int> RunAsync(string[] args) {
        if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal)) {
            Console.WriteLine(FuzzOptions.Usage);
            return 0;
        }

        var options = FuzzOptions.Parse(args);
        var summary = await FuzzRunner.RunAsync(options);

        if (!options.Quiet) {
            Console.WriteLine(
                $"SharpProof fuzz run complete: {summary.CasesAnalyzed} cases, {summary.FindingCount} findings ({summary.UniqueFindingCount} unique), {summary.AnalyzerExceptionCount} analyzer exceptions.");
            Console.WriteLine($"Artifacts: {summary.OutputDirectory}");
        }

        return options.FailOnFindings && summary.FindingCount > 0 ? 2 : 0;
    }
}
