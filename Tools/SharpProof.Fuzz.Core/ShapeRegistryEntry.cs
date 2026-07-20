namespace SharpProof.Tools.Fuzz;

public enum Sp0002ExpectationKind
{
    MustNotEmit,
    MustEmit,
    MayEmitConservatively
}

public enum Sp0010ExpectationKind
{
    Ignore,
    MustNotEmit,
    MustEmit,
    MayEmitConservatively
}

public sealed record FuzzExpectation(
    Sp0002ExpectationKind Sp0002,
    Sp0010ExpectationKind Sp0010,
    ImmutableArray<string> RequiredSp0002Properties,
    ImmutableArray<string> RequiredSp0010Properties,
    ImmutableArray<string> RequiredAnySp0010Properties)
{
    public const string ConservativeBucket = "conservative";
    public const string DefinitelyImpureBucket = "definitely_impure";
    public const string DefinitelyPureBucket = "definitely_pure";

    private static readonly ImmutableArray<string> DefaultSp0002Properties = ImmutableArray.Create(
        DiagnosticPropertyNames.ImpurityCategoryProperty,
        DiagnosticPropertyNames.ImpurityRuleProperty,
        DiagnosticPropertyNames.ImpurityOperationKindProperty);

    private static readonly ImmutableArray<string> DefaultSp0010Properties = ImmutableArray.Create(
        DiagnosticPropertyNames.ExceptionTypesProperty,
        DiagnosticPropertyNames.ExceptionCategoriesProperty,
        DiagnosticPropertyNames.ExceptionSourcesProperty);

    public string Bucket =>
        Sp0002 == Sp0002ExpectationKind.MayEmitConservatively ||
        Sp0010 == Sp0010ExpectationKind.MayEmitConservatively
            ? ConservativeBucket
            : Sp0002 == Sp0002ExpectationKind.MustNotEmit &&
              Sp0010 is Sp0010ExpectationKind.Ignore or Sp0010ExpectationKind.MustNotEmit
                ? DefinitelyPureBucket
                : DefinitelyImpureBucket;

    public bool IsConservative => Bucket == ConservativeBucket;

    public static FuzzExpectation DefinitelyPure() => Create(
        Sp0002ExpectationKind.MustNotEmit, Sp0010ExpectationKind.Ignore);

    public static FuzzExpectation Conservative() => Create(
        Sp0002ExpectationKind.MayEmitConservatively, Sp0010ExpectationKind.Ignore);

    internal static FuzzExpectation Create(
        Sp0002ExpectationKind sp0002,
        Sp0010ExpectationKind sp0010) =>
        new(
            sp0002,
            sp0010,
            DefaultSp0002Properties,
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
