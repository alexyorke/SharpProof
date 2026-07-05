using System.Collections.Immutable;
using SharpProof.Analyzer;

namespace SharpProof.Tools.Fuzz;

public enum Sp0002ExpectationKind
{
    MustNotEmit,
    MustEmit,
    MayEmitConservatively,
}

public enum Sp0010ExpectationKind
{
    Ignore,
    MustNotEmit,
    MustEmit,
    MayEmitConservatively,
}

public sealed record FuzzExpectation(
    Sp0002ExpectationKind Sp0002,
    Sp0010ExpectationKind Sp0010,
    ImmutableArray<string> RequiredSp0002Properties,
    ImmutableArray<string> RequiredSp0010Properties,
    ImmutableArray<string> RequiredAnySp0010Properties)
{
    private static readonly ImmutableArray<string> DefaultSp0002Properties = ImmutableArray.Create(
        SharpProofDiagnostics.ImpurityCategoryProperty,
        SharpProofDiagnostics.ImpurityRuleProperty,
        SharpProofDiagnostics.ImpurityOperationKindProperty);

    private static readonly ImmutableArray<string> DefaultSp0010Properties = ImmutableArray.Create(
        SharpProofDiagnostics.ExceptionTypesProperty,
        SharpProofDiagnostics.ExceptionCategoriesProperty,
        SharpProofDiagnostics.ExceptionSourcesProperty);

    public static FuzzExpectation DefinitelyPure()
    {
        return new FuzzExpectation(
            Sp0002ExpectationKind.MustNotEmit,
            Sp0010ExpectationKind.Ignore,
            DefaultSp0002Properties,
            DefaultSp0010Properties,
            ImmutableArray<string>.Empty);
    }

    public static FuzzExpectation DefinitelyImpure()
    {
        return new FuzzExpectation(
            Sp0002ExpectationKind.MustEmit,
            Sp0010ExpectationKind.Ignore,
            DefaultSp0002Properties,
            DefaultSp0010Properties,
            ImmutableArray<string>.Empty);
    }

    public static FuzzExpectation Conservative()
    {
        return new FuzzExpectation(
            Sp0002ExpectationKind.MayEmitConservatively,
            Sp0010ExpectationKind.Ignore,
            DefaultSp0002Properties,
            DefaultSp0010Properties,
            ImmutableArray<string>.Empty);
    }

    public FuzzExpectation RequireAnySp0010Properties(params string[] propertyNames)
    {
        return this with
        {
            RequiredAnySp0010Properties = ImmutableArray.Create(propertyNames)
        };
    }

    public FuzzExpectation RequireExceptionEdgesOnAnySp0010()
    {
        return this with
        {
            RequiredAnySp0010Properties = ImmutableArray.Create(SharpProofDiagnostics.ExceptionEdgesProperty)
        };
    }
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
