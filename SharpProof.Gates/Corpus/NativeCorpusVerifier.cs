using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

internal sealed record NativeCorpusVerdict(SyntaxNode? Declaration, IMethodSymbol Method, WorkerClaimOutcome Outcome);

// Z3 decides the corpus's claims as it does in a build: the compiler collector
// declares them and the worker verifies the artifact in process.
internal static class NativeCorpusVerifier
{
    internal static async Task<ImmutableArray<NativeCorpusVerdict>> VerifyAsync(CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        if (discovery.Targets.IsEmpty)
        { return []; }
        var directory = Directory.CreateTempSubdirectory("sharpproof-corpus-");
        try
        {
            var artifact = CompilerManifestArtifactProducer.Create(compilation, directory.FullName, "net9.0", WorkerFeatureSet.All,
                discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellationToken);
            var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken));
            var path = Path.Combine(directory.FullName, "compiler-manifest.json");
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            var request = new WorkerVerifyRequest
            {
                CompilerManifest = new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) },
                Cache = new WorkerCacheOptions { Enabled = false, Directory = Path.Combine(directory.FullName, "cache") },
                Budgets = new WorkerBudgets()
            };
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            using var worker = SharpProofWorker.Create(request.Budgets);
            var response = await worker.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.RunStatus != WorkerRunStatus.Complete)
            {
                throw new InvalidOperationException("The worker did not complete the corpus artifact (" + response.RunStatus + ", " +
                    response.FailureReason + "): " +
                    string.Join("; ", response.Errors.Select(static error => error.Code + ": " + error.Message)));
            }
            var results = response.ClaimResults.ToDictionary(static result => result.ClaimId, StringComparer.Ordinal);
            var claimsByCallable = response.Manifest.Claims.ToLookup(static claim => claim.CallableId, StringComparer.Ordinal);
            return [.. discovery.Targets.Values
                .Where(target => claimsByCallable[target.Entry.CallableId].Any())
                .Select(target => new NativeCorpusVerdict(target.Declaration, target.Method,
                    Combine(claimsByCallable[target.Entry.CallableId].Select(claim => results[claim.ClaimId].Outcome))))];
        }
        finally
        { directory.Delete(recursive: true); }
    }

    // A callable is refuted by any refuted claim and proven only when every
    // claim is proven.
    private static WorkerClaimOutcome Combine(IEnumerable<WorkerClaimOutcome> outcomes)
    {
        var all = outcomes.ToArray();
        return all.Contains(WorkerClaimOutcome.Refuted) ? WorkerClaimOutcome.Refuted
            : all.All(static outcome => outcome == WorkerClaimOutcome.Proven) ? WorkerClaimOutcome.Proven
            : WorkerClaimOutcome.Unknown;
    }
}
