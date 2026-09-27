using System.Collections.Immutable;
using System.Text;
using SharpProof.CompilerArtifact;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

// Runs the Z3 worker on a corpus case so the snapshot records worker verdicts
// next to the analyzer's.
internal static class CorpusWorkerHost
{
    internal static async Task<string> ObserveAsync(
        CorpusCase item,
        CancellationToken cancellationToken)
    {
        var features = item.Mode switch
        {
            "effects" => WorkerFeatureSet.Effects,
            "contracts" => WorkerFeatureSet.Contracts,
            _ => WorkerFeatureSet.All
        };
        var directory = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-corpus-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var compilation = AnalyzerGateHost.CreateCompilation(item.Source);
            var discovery = new ClaimManifestBuilder(compilation, features).Build();
            var artifact = CompilerManifestArtifactProducer.Create(
                compilation,
                directory,
                "net8.0",
                features,
                discovery,
                WorkerBudgets.DefaultMaximumExpressionDepth,
                cancellationToken);
            var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.Serialize(artifact, cancellationToken));
            var path = Path.Combine(directory, "compiler-manifest.json");
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            var request = new WorkerVerifyRequest
            {
                CompilerManifest = new WorkerFileReference
                {
                    Path = path,
                    Sha256 = WorkerProtocolJson.ComputeSha256(bytes)
                },
                Cache = new WorkerCacheOptions { Enabled = false },
                Budgets = new WorkerBudgets()
            };
            using var worker = SharpProofWorker.Create(request.Budgets);
            var response = await worker.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.RunStatus != WorkerRunStatus.Complete)
            {
                return "run-" + response.RunStatus;
            }

            var outcomes = response.ClaimResults
                .Select(static result => result.Outcome == WorkerClaimOutcome.Unknown
                    ? "Unknown(" + result.Reason + ")"
                    : result.Outcome.ToString())
                .ToImmutableArray();
            return outcomes.IsEmpty ? "-" : string.Join(";", outcomes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
