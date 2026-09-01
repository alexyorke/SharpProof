using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SharpProof.Gates.Corpus;
using SharpProof.Gates.Performance;
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
                    JsonSerializer.Serialize(
                        CreateStandaloneEnvelope(
                            root,
                            command,
                            result.Passed,
                            result),
                        JsonDefaults.Indented));
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
                    JsonSerializer.Serialize(
                        CreateStandaloneEnvelope(
                            root,
                            command,
                            result.Passed,
                            result),
                        JsonDefaults.Indented));
                return result.Passed ? 0 : 1;
            }
            if (command == "performance-smoke")
            {
                var result = await PerformanceGate.RunSmokeAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine(
                    JsonSerializer.Serialize(result, JsonDefaults.Indented));
                return result.Passed ? 0 : 1;
            }
            Console.Error.WriteLine(
                "Usage: SharpProof.Gates " +
                "[all|corpus|corpus-print|corpus-update|performance|" +
                "performance-smoke]");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static object CreateStandaloneEnvelope(
        string repositoryRoot,
        string gate,
        bool passed,
        object result)
    {
        var assembly = typeof(Program).Assembly;
        var sourceCommit = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(static attribute =>
                attribute.Key == "SharpProofSourceCommit")
            ?.Value;
        if (sourceCommit is null)
        {
            // Interactive corpus/performance commands remain useful without
            // producing certifiable evidence. The evidence writer always
            // rebuilds with this metadata and rejects an unwrapped result.
            return result;
        }
        if (sourceCommit.Length != 40 ||
            sourceCommit.Any(static character =>
                character is not (>= '0' and <= '9') and
                    not (>= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                "The standalone gate executable is not source-bound.");
        }

        var executablePath = assembly.Location;
        var pdbPath = Path.ChangeExtension(executablePath, ".pdb");
        if (!File.Exists(executablePath) || !File.Exists(pdbPath))
        {
            throw new InvalidOperationException(
                "The standalone gate build identity is incomplete.");
        }

        var contractPath = Path.Combine(
            repositoryRoot,
            "eng",
            "acceptance",
            "contract.json");
        return new
        {
            SchemaVersion = 1,
            Gate = gate,
            Passed = passed,
            SourceCommit = sourceCommit,
            AcceptanceContractSha256 = Sha256(contractPath),
            Executable = new
            {
                Sha256 = Sha256(executablePath),
                Mvid = assembly.ManifestModule.ModuleVersionId.ToString("D"),
                PdbSha256 = Sha256(pdbPath)
            },
            Result = result
        };
    }

    private static string Sha256(string path)
    {
        return HashEncoding.ComputeSha256Hex(File.ReadAllBytes(path));
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
