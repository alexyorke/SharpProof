using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SharpProof.Gates.Corpus;
using SharpProof.Gates.Performance;

namespace SharpProof.Gates;

internal static class Program
{
    internal const int GateSuccessExitCode = 0;
    internal const int GateThresholdFailureExitCode = 1;
    internal const int GateUsageExitCode = 2;
    internal const int GateInfrastructureFailureExitCode = 3;
    internal const int GatePartialFailureExitCode = 4;

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
                return await RunAllAsync(root).ConfigureAwait(false);
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
                return result.Passed
                    ? GateSuccessExitCode
                    : GateThresholdFailureExitCode;
            }
            if (command == "corpus-print")
            {
                Console.Write(
                    await CorpusGate.RenderActualSnapshotAsync()
                        .ConfigureAwait(false));
                return GateSuccessExitCode;
            }
            if (command == "corpus-update")
            {
                await CorpusGate.WriteActualSnapshotAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine("Updated the canonical corpus snapshot.");
                return GateSuccessExitCode;
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
                return result.Passed
                    ? GateSuccessExitCode
                    : GateThresholdFailureExitCode;
            }
            if (command == "performance-smoke")
            {
                var result = await PerformanceGate.RunSmokeAsync(root)
                    .ConfigureAwait(false);
                Console.WriteLine(
                    JsonSerializer.Serialize(result, JsonDefaults.Indented));
                return result.Passed
                    ? GateSuccessExitCode
                    : GateThresholdFailureExitCode;
            }
            Console.Error.WriteLine(
                "Usage: SharpProof.Gates " +
                "[all|corpus|corpus-print|corpus-update|performance|" +
                "performance-smoke]");
            return GateUsageExitCode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(exception);
            return GateInfrastructureFailureExitCode;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Each gate phase is an executable boundary with a stable failure code and partial result.")]
    internal static Task<int> RunAllAsync(string repositoryRoot)
    {
        return RunAllAsync(
            repositoryRoot,
            static root => CorpusGate.RunAsync(root),
            static root => PerformanceGate.RunAsync(root));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Each gate phase is an executable boundary with a stable failure code and partial result.")]
    internal static async Task<int> RunAllAsync(
        string repositoryRoot,
        Func<string, Task<CorpusGateResult>> runCorpus,
        Func<string, Task<PerformanceGateResult>> runPerformance)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(runCorpus);
        ArgumentNullException.ThrowIfNull(runPerformance);

        CorpusGateResult corpus;
        try
        {
            corpus = await runCorpus(repositoryRoot).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteGateFailure("corpus", exception);
            WritePartialResult(null, null, "corpus", exception);
            return GateInfrastructureFailureExitCode;
        }

        PerformanceGateResult performance;
        try
        {
            performance = await runPerformance(repositoryRoot).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            WriteGateFailure("performance", exception);
            WritePartialResult(corpus, null, "performance", exception);
            return GatePartialFailureExitCode;
        }

        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    corpus,
                    performance
                },
                JsonDefaults.Indented));
        return corpus.Passed && performance.Passed
            ? GateSuccessExitCode
            : GateThresholdFailureExitCode;
    }

    private static void WriteGateFailure(string phase, Exception exception)
    {
        Console.Error.WriteLine(
            "SharpProof.Gates " + phase + " phase failed: " + exception);
    }

    private static void WritePartialResult(
        CorpusGateResult? corpus,
        PerformanceGateResult? performance,
        string failedPhase,
        Exception exception)
    {
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    corpus,
                    performance,
                    failure = new
                    {
                        phase = failedPhase,
                        type = exception.GetType().Name,
                        message = exception.Message
                    }
                },
                JsonDefaults.Indented));
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
        return string.Concat(
            SHA256.HashData(File.ReadAllBytes(path)).Select(static value =>
                value.ToString("x2", CultureInfo.InvariantCulture)));
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
