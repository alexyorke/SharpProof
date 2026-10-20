using System.Collections.Immutable;
using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed class ShadowTestProject : IDisposable
{
    internal const string IdentitySource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x, int unused) {
            Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
            return x;
        } }
        """;

    private readonly TempDirectory _directory = new("sharpproof-shadow-");
    internal CompilerManifestArtifact Artifact { get; }
    internal WorkerVerifyRequest Request { get; }
    internal WorkerInputSnapshot Snapshot { get; }
    internal ShadowTestProject(string source, bool cacheEnabled = false)
        : this(CompilerTotalCallableArtifactTests.CreateArtifact(source), cacheEnabled)
    {
        System.ArgumentNullException.ThrowIfNull(source, nameof(source));
    }
    internal ShadowTestProject(CompilerManifestArtifact artifact, bool cacheEnabled = false)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
        try
        {
            Artifact = artifact with { Compilation = artifact.Compilation with { ProjectDirectory = _directory.FullName } };
            artifact = Artifact;
            var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)).ToImmutableArray();
            var path = Path.Combine(_directory.FullName, "compiler-manifest.json");
            File.WriteAllBytes(path, bytes.AsSpan());
            Request = new WorkerVerifyRequest
            {
                CompilerManifest = new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) },
                Cache = new WorkerCacheOptions { Enabled = cacheEnabled, Directory = Path.Combine(_directory.FullName, "cache") },
                Budgets = new WorkerBudgets()
            };
            Snapshot = WorkerInputSnapshot.Load(Request, WorkerCacheIdentity.Current, TestContext.CurrentContext.CancellationToken);
            // Native verification must reuse the prepared
            // immutable artifact, including a later validated cache hit.
            File.Delete(path);
        }
        catch
        { _directory.Dispose(); throw; }
    }
    public void Dispose()
    { _directory.Dispose(); }

    internal WorkerInputSnapshot Bind()
    {
        return ArtifactValidator.Bind(Request,
            new ValidatedArtifact(Snapshot.CompilerManifest, Snapshot.Callables, Snapshot.ArtifactDigest), WorkerCacheIdentity.Current);
    }
}
