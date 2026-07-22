namespace SharpProof.Tools.Fuzz;
public sealed record FuzzExpectation(
    SharpProofVerdict PurityVerdict,
    ImmutableArray<SharpProofEffect> RequiredEffects,
    ImmutableArray<SharpProofEffect> ForbiddenEffects,
    ImmutableArray<string> RequiredUnknownCategories,
    ImmutableArray<string> RequiredDiagnosticIds,
    string? ProofCondition,
    string? ProofStatus,
    bool RequireCounterexample) {
    public const string ConservativeBucket = "conservative";
    public const string DisprovenBucket = "disproven";
    public const string ProvenBucket = "proven";
    public string Bucket => PurityVerdict switch {
        SharpProofVerdict.Unknown => ConservativeBucket,
        SharpProofVerdict.Proven => ProvenBucket,
        _ => DisprovenBucket
    };
    public bool IsConservative => Bucket == ConservativeBucket;
    public static FuzzExpectation DefinitelyPure() => Create(SharpProofVerdict.Proven);
    public static FuzzExpectation Conservative() => Create(SharpProofVerdict.Unknown);
    internal static FuzzExpectation Create(SharpProofVerdict purityVerdict) => new(
        purityVerdict,
        [],
        [],
        [],
        [],
        null,
        null,
        false);
}
public sealed record ShapeRegistryEntry(
    string Id,
    ImmutableArray<string> PrimaryShapeIds,
    ImmutableArray<string> ExpectedOperationKinds,
    ImmutableArray<string> ExpectedSyntaxKinds,
    FuzzExpectation Expectation,
    bool AllowUnsafe,
    Func<int, Random, string, string> Build);
