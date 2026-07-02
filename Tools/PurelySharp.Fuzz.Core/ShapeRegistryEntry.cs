using System.Collections.Immutable;
using PurelySharp.Analyzer;

namespace PurelySharp.Tools.Fuzz;

public enum Ps0002ExpectationKind
{
    MustNotEmit,
    MustEmit,
    MayEmitConservatively,
}

public enum Ps0010ExpectationKind
{
    Ignore,
    MustNotEmit,
    MustEmit,
    MayEmitConservatively,
}

public sealed record FuzzExpectation(
    Ps0002ExpectationKind Ps0002,
    Ps0010ExpectationKind Ps0010,
    ImmutableArray<string> RequiredPs0002Properties,
    ImmutableArray<string> RequiredPs0010Properties,
    ImmutableArray<string> RequiredAnyPs0010Properties)
{
    private static readonly ImmutableArray<string> DefaultPs0002Properties = ImmutableArray.Create(
        PurelySharpDiagnostics.ImpurityCategoryProperty,
        PurelySharpDiagnostics.ImpurityRuleProperty,
        PurelySharpDiagnostics.ImpurityOperationKindProperty);

    private static readonly ImmutableArray<string> DefaultPs0010Properties = ImmutableArray.Create(
        PurelySharpDiagnostics.ExceptionTypesProperty,
        PurelySharpDiagnostics.ExceptionCategoriesProperty,
        PurelySharpDiagnostics.ExceptionSourcesProperty);

    public static FuzzExpectation DefinitelyPure()
    {
        return new FuzzExpectation(
            Ps0002ExpectationKind.MustNotEmit,
            Ps0010ExpectationKind.Ignore,
            DefaultPs0002Properties,
            DefaultPs0010Properties,
            ImmutableArray<string>.Empty);
    }

    public static FuzzExpectation DefinitelyImpure()
    {
        return new FuzzExpectation(
            Ps0002ExpectationKind.MustEmit,
            Ps0010ExpectationKind.Ignore,
            DefaultPs0002Properties,
            DefaultPs0010Properties,
            ImmutableArray<string>.Empty);
    }

    public static FuzzExpectation Conservative()
    {
        return new FuzzExpectation(
            Ps0002ExpectationKind.MayEmitConservatively,
            Ps0010ExpectationKind.Ignore,
            DefaultPs0002Properties,
            DefaultPs0010Properties,
            ImmutableArray<string>.Empty);
    }

    public FuzzExpectation RequireAnyPs0010Properties(params string[] propertyNames)
    {
        return this with
        {
            RequiredAnyPs0010Properties = ImmutableArray.Create(propertyNames)
        };
    }

    public FuzzExpectation RequireExceptionEdgesOnAnyPs0010()
    {
        return this with
        {
            RequiredAnyPs0010Properties = ImmutableArray.Create(PurelySharpDiagnostics.ExceptionEdgesProperty)
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
