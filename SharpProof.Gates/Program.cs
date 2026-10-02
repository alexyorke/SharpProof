using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SharpProof.Gates.Corpus;
using SharpProof.Ir;

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
            var command = args.Length == 0 ? "corpus" : args[0];
            if (command is "exception-shadow" or "allocation-shadow" or "purity-shadow")
            {
                var maximumMethods = args.Length == 1 ? 0 : args.Length == 3 && args[1] == "--limit"
                    ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)
                    : throw new ArgumentException("Use exception-shadow, allocation-shadow or purity-shadow [--limit <method-count>].", nameof(args));
                var result = await NativeExceptionShadow.RunAsync(root, maximumMethods, allocations: command == "allocation-shadow",
                    purity: command == "purity-shadow").ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(result, SharpProofJsonDefaults.Indented));
                return result.ComparisonPassed ? 0 : 1;
            }
            if (command == "corpus")
            {
                var wallTime = System.Diagnostics.Stopwatch.StartNew();
                var result = await CorpusGate.RunAsync(root).ConfigureAwait(false);
                Console.Error.WriteLine($"Corpus gate wall time: {wallTime.Elapsed.TotalSeconds:F1}s.");
                Console.WriteLine(
                    JsonSerializer.Serialize(result, SharpProofJsonDefaults.Indented));
                return result.Passed ? 0 : 1;
            }
            if (command == "corpus-update")
            {
                await CorpusGate.WriteActualSnapshotAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine("Updated the canonical corpus snapshot.");
                return 0;
            }
            Console.Error.WriteLine("Usage: SharpProof.Gates [corpus|corpus-update|exception-shadow|allocation-shadow|purity-shadow] [--limit <method-count>]");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
