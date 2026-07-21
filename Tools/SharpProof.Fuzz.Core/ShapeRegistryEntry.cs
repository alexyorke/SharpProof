namespace SharpProof.Tools.Fuzz;

public enum Sp0010ExpectationKind {
    Ignore,
    MustNotEmit,
    MustEmit,
    MayEmitConservatively
}

public sealed record FuzzExpectation(
    SharpProofVerdict PurityVerdict,
    ImmutableArray<SharpProofEffect> RequiredEffects,
    ImmutableArray<string> RequiredUnknownCategories,
    Sp0010ExpectationKind Sp0010,
    ImmutableArray<string> RequiredSp0010Properties,
    ImmutableArray<string> RequiredAnySp0010Properties) {
    public const string ConservativeBucket = "conservative";
    public const string DisprovenBucket = "disproven";
    public const string ProvenBucket = "proven";

    private static readonly ImmutableArray<string> DefaultSp0010Properties = ImmutableArray.Create(
        DiagnosticPropertyNames.ExceptionTypesProperty,
        DiagnosticPropertyNames.ExceptionCategoriesProperty,
        DiagnosticPropertyNames.ExceptionSourcesProperty);

    public string Bucket =>
        PurityVerdict == SharpProofVerdict.Unknown ||
        Sp0010 == Sp0010ExpectationKind.MayEmitConservatively
            ? ConservativeBucket
            : PurityVerdict == SharpProofVerdict.Proven &&
              Sp0010 is Sp0010ExpectationKind.Ignore or Sp0010ExpectationKind.MustNotEmit
                ? ProvenBucket
                : DisprovenBucket;

    public bool IsConservative => Bucket == ConservativeBucket;

    public static FuzzExpectation DefinitelyPure() => Create(
        SharpProofVerdict.Proven, Sp0010ExpectationKind.Ignore);

    public static FuzzExpectation Conservative() => Create(
        SharpProofVerdict.Unknown, Sp0010ExpectationKind.Ignore);

    internal static FuzzExpectation Create(
        SharpProofVerdict purityVerdict,
        Sp0010ExpectationKind sp0010) =>
        new(
            purityVerdict,
            ImmutableArray<SharpProofEffect>.Empty,
            ImmutableArray<string>.Empty,
            sp0010,
            DefaultSp0010Properties,
            ImmutableArray<string>.Empty);

}

public sealed record ShapeRegistryEntry(
    string Id,
    ImmutableArray<string> PrimaryShapeIds,
    ImmutableArray<string> ExpectedOperationKinds,
    ImmutableArray<string> ExpectedSyntaxKinds,
    FuzzExpectation Expectation,
    bool AllowUnsafe,
    bool AllowEffectPreservingWrappers,
    Func<int, Random, string, string> Build);
