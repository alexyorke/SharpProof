#nullable enable
namespace SharpProof.Worker;

internal sealed partial record WorkerInputSnapshot(
    CompilerManifestArtifact CompilerManifest,
    ImmutableArray<CompilerCallablePreparation> Callables,
    string ArtifactDigest,
    string InputHash)
{
    public CompilerManifestArtifact CompilerManifest { get; init; } = CompilerManifest ?? throw new ArgumentNullException(nameof(CompilerManifest));
    public ImmutableArray<CompilerCallablePreparation> Callables { get; init; } = Callables.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Callables)) : Callables;
    public string ArtifactDigest { get; init; } = ArtifactDigest ?? throw new ArgumentNullException(nameof(ArtifactDigest));
    public string InputHash { get; init; } = InputHash ?? throw new ArgumentNullException(nameof(InputHash));
}

internal sealed partial record CallableEntryFeasibility(
    CallableEntryFeasibilityKind Kind,
    WorkerClaimReason Reason,
    ImmutableArray<string> ProofCore,
    ImmutableHashSet<string> UsedAssumptionIds
)
{
    public ImmutableArray<string> ProofCore { get; init; } = ProofCore.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(ProofCore)) : ProofCore;
    public ImmutableHashSet<string> UsedAssumptionIds { get; init; } = UsedAssumptionIds ?? throw new ArgumentNullException(nameof(UsedAssumptionIds));
}

internal sealed partial record CallableVerificationResult(
    WorkerCallableResult Callable,
    ImmutableArray<WorkerClaimResult> Claims
)
{
    public WorkerCallableResult Callable { get; init; } = Callable ?? throw new ArgumentNullException(nameof(Callable));
    public ImmutableArray<WorkerClaimResult> Claims { get; init; } = Claims.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Claims)) : Claims;
}

internal sealed partial class VerificationCache
{
    private sealed partial record CacheEntry(
        int SchemaVersion,
        string InputHash,
        string ManifestHash,
        ImmutableArray<WorkerCallableResult> CallableResults,
        ImmutableArray<WorkerClaimResult> ClaimResults
    )
    {
        public string InputHash { get; init; } = InputHash ?? throw new ArgumentNullException(nameof(InputHash));
        public string ManifestHash { get; init; } = ManifestHash ?? throw new ArgumentNullException(nameof(ManifestHash));
        [System.Text.Json.Serialization.JsonConverter(typeof(ProtocolArrayJsonConverter))]
        public ImmutableArray<WorkerCallableResult> CallableResults { get; init; } = CallableResults.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(CallableResults)) : CallableResults;
        [System.Text.Json.Serialization.JsonConverter(typeof(ProtocolArrayJsonConverter))]
        public ImmutableArray<WorkerClaimResult> ClaimResults { get; init; } = ClaimResults.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(ClaimResults)) : ClaimResults;
    }
}
