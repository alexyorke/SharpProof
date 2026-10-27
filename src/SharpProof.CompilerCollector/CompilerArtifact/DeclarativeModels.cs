#nullable enable
namespace SharpProof.CompilerCollector;

internal sealed partial record ClaimManifestBuildResult(
    WorkerClaimManifest Manifest,
    ImmutableDictionary<IMethodSymbol, ManifestCallableTarget> Targets
)
{
    public WorkerClaimManifest Manifest { get; init; } = Manifest ?? throw new ArgumentNullException(nameof(Manifest));
    public ImmutableDictionary<IMethodSymbol, ManifestCallableTarget> Targets { get; init; } = Targets ?? throw new ArgumentNullException(nameof(Targets));
    // Null means the optional source census was not requested.
    internal CompilerPotentialCallInventory? PotentialCalls { get; init; }
}

// The first source census covers outer static scalar methods. It is separate
// from mandatory claim membership and does not authorize a native proof.
internal sealed record CompilerPotentialCallInventory(
    ImmutableArray<CompilerPotentialCallOwner> Owners,
    ImmutableArray<CompilerPotentialCallGap> Gaps)
{
    public ImmutableArray<CompilerPotentialCallOwner> Owners { get; init; } = Owners.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Owners)) : Owners;
    public ImmutableArray<CompilerPotentialCallGap> Gaps { get; init; } = Gaps.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Gaps)) : Gaps;
}

internal sealed record CompilerPotentialCallOwner(
    IMethodSymbol Method,
    MethodDeclarationSyntax Declaration,
    SemanticModel SemanticModel,
    IMethodBodyOperation BodyOperation,
    string CallableId,
    ImmutableArray<SharpProof.Compiler.Semantics.PotentialRequiresCallSite> Calls,
    bool DiscoveryComplete)
{
    public IMethodSymbol Method { get; init; } = Method ?? throw new ArgumentNullException(nameof(Method));
    public MethodDeclarationSyntax Declaration { get; init; } = Declaration ?? throw new ArgumentNullException(nameof(Declaration));
    public SemanticModel SemanticModel { get; init; } = SemanticModel ?? throw new ArgumentNullException(nameof(SemanticModel));
    public IMethodBodyOperation BodyOperation { get; init; } = BodyOperation ?? throw new ArgumentNullException(nameof(BodyOperation));
    public string CallableId { get; init; } = CallableId ?? throw new ArgumentNullException(nameof(CallableId));
    public ImmutableArray<SharpProof.Compiler.Semantics.PotentialRequiresCallSite> Calls { get; init; } = Calls.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Calls)) : Calls;
}

internal sealed record CompilerPotentialCallGap(int TreeOrdinal, int Start, int Length, string Reason,
    string? ReferenceAssemblyName = null)
{
    public string Reason { get; init; } = Reason ?? throw new ArgumentNullException(nameof(Reason));
}

// Compiler-owned preparation only; it cannot enter mandatory worker routing.
internal sealed record CompilerShadowPreparation(CompilerTotalCallablePreparation Body)
{
    public CompilerTotalCallablePreparation Body { get; init; } = Body ?? throw new ArgumentNullException(nameof(Body));
    internal string OwnerId => Body.CallableId;
}

internal sealed record CompilerShadowPreparationBatch(
    ImmutableArray<CompilerShadowPreparation> Callers,
    ImmutableArray<CompilerPotentialCallGap> Gaps)
{
    public ImmutableArray<CompilerShadowPreparation> Callers { get; init; } = Callers.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Callers)) : Callers;
    public ImmutableArray<CompilerPotentialCallGap> Gaps { get; init; } = Gaps.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Gaps)) : Gaps;
}

internal sealed partial record ManifestCallableTarget(
    IMethodSymbol Method,
    SyntaxNode? Declaration,
    SemanticModel? SemanticModel,
    WorkerCallableManifestEntry Entry,
    ImmutableArray<ManifestClaim> Claims,
    ImmutableArray<ManifestEffectClaim> EffectClaims,
    bool IsVerifierSupported
)
{
    public IMethodSymbol Method { get; init; } = Method ?? throw new ArgumentNullException(nameof(Method));
    public WorkerCallableManifestEntry Entry { get; init; } = Entry ?? throw new ArgumentNullException(nameof(Entry));
    public ImmutableArray<ManifestClaim> Claims { get; init; } = Claims.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Claims)) : Claims;
    public ImmutableArray<ManifestEffectClaim> EffectClaims { get; init; } = EffectClaims.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(EffectClaims)) : EffectClaims;
}

internal sealed partial record ManifestClaim(
    WorkerClaimManifestEntry Entry,
    IInvocationOperation? SourceOperation,
    AttributeData? SourceAttribute,
    ContractClausePlacement? Placement
)
{
    public WorkerClaimManifestEntry Entry { get; init; } = Entry ?? throw new ArgumentNullException(nameof(Entry));
}

internal sealed partial record ManifestEffectClaim(
    WorkerClaimManifestEntry Entry,
    CompilerEffectClaimArtifact Evidence,
    bool HasValidConstraint
)
{
    public WorkerClaimManifestEntry Entry { get; init; } = Entry ?? throw new ArgumentNullException(nameof(Entry));
    public CompilerEffectClaimArtifact Evidence { get; init; } = Evidence ?? throw new ArgumentNullException(nameof(Evidence));
}

internal sealed partial class ClaimManifestBuilder
{
    private readonly partial record struct ClaimCandidate(
        string Fingerprint,
        Location Location,
        WorkerClaimEvidence Evidence,
        IInvocationOperation? Operation,
        AttributeData? Attribute,
        ContractClausePlacement? Placement
    )
    {
        public string Fingerprint { get; init; } = Fingerprint ?? throw new ArgumentNullException(nameof(Fingerprint));
        public Location Location { get; init; } = Location ?? throw new ArgumentNullException(nameof(Location));
    }
    private readonly partial record struct AssumptionCandidate(WorkerAssumptionKind Kind, string Fingerprint)
    {
        public string Fingerprint { get; init; } = Fingerprint ?? throw new ArgumentNullException(nameof(Fingerprint));
    }
    private readonly partial record struct CallableSeed(
        IMethodSymbol Method,
        SyntaxNode? Declaration,
        SemanticModel? Model
    )
    {
        public IMethodSymbol Method { get; init; } = Method ?? throw new ArgumentNullException(nameof(Method));
    }
}

internal static partial class SemanticClaimIdentity
{
    private readonly partial record struct ClaimIdentityContext(
        IMethodSymbol Target,
        IMethodSymbol Source,
        bool UsesCompanion
    )
    {
        public IMethodSymbol Target { get; init; } = Target ?? throw new ArgumentNullException(nameof(Target));
        public IMethodSymbol Source { get; init; } = Source ?? throw new ArgumentNullException(nameof(Source));
        internal ContractApiSymbols? Intrinsics { get; init; }
        internal SemanticModel? Model { get; init; }
    }
}
