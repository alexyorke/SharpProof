using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SharpProof.Gates.Corpus;
using SharpProof.Gates.Performance;

namespace SharpProof.Gates;

internal static class Program
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The executable boundary converts unexpected gate failures into a stable exit code and diagnostic.")]
    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This developer-facing repository gate is not localized.")]
    [SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The gate writes short console diagnostics synchronously before returning its process exit code.")]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var root = RepositoryLayout.FindRoot();
            var command = args.Length == 0 ? "all" : args[0];
            if (command == "all")
            {
                var corpus = await CorpusGate.RunAsync(root)
                    .ConfigureAwait(false);
                var performance = await PerformanceGate.RunAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        new
                        {
                            corpus,
                            performance
                        },
                        JsonDefaults.Indented));
                return corpus.Passed && performance.Passed ? 0 : 1;
            }
            if (command == "corpus")
            {
                var result = await CorpusGate.RunAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine(
                    JsonSerializer.Serialize(result, JsonDefaults.Indented));
                return result.Passed ? 0 : 1;
            }
            if (command == "corpus-print")
            {
                Console.Write(
                    await CorpusGate.RenderActualSnapshotAsync()
                        .ConfigureAwait(false));
                return 0;
            }
            if (command == "corpus-update")
            {
                await CorpusGate.WriteActualSnapshotAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine("Updated the canonical corpus snapshot.");
                return 0;
            }
            if (command == "performance")
            {
                var result = await PerformanceGate.RunAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine(
                    JsonSerializer.Serialize(result, JsonDefaults.Indented));
                return result.Passed ? 0 : 1;
            }
            Console.Error.WriteLine(
                "Usage: SharpProof.Gates " +
                "[all|corpus|corpus-print|corpus-update|performance]");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

internal static class JsonDefaults
{
    internal static JsonSerializerOptions Indented
    {
        get;
    } =
        new()
        {
            WriteIndented = true
        };
}
