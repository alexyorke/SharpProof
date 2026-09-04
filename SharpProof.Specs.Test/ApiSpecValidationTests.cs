using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecValidationTests
{
    private static readonly SpecEvidence Evidence = new(
        SpecEvidenceKind.Documented,
        "spec-validation-test");

    [TestCase(0xD800)]
    [TestCase(0xD801)]
    public void IllFormedUtf16ConstantsAreRejectedBeforeTableDigesting(
        int surrogate)
    {
        var text = new SpecStringDeclaration(
            new string((char)surrogate, 1));
        var declaration = Declaration(
            "ill-formed-" + surrogate,
            resultType: null,
            SpecNullness.NotApplicable,
            SpecCardinality.NotApplicable,
            [Equal(text, text)]);

        var exception = Assert.Throws<ArgumentException>(() =>
            ApiSpecTable.Create([declaration]));

        Assert.That(
            exception!.Message,
            Does.Contain("well-formed UTF-16"));
    }

    [Test]
    public void WellFormedSurrogatePairsRemainValidSpecConstants()
    {
        var text = new SpecStringDeclaration(char.ConvertFromUtf32(0x10000));
        var declaration = Declaration(
            "well-formed-surrogates",
            resultType: null,
            SpecNullness.NotApplicable,
            SpecCardinality.NotApplicable,
            [Equal(text, text)]);

        Assert.That(
            ApiSpecTable.Create([declaration]).Templates,
            Has.Length.EqualTo(1));
    }

    [Test]
    public void CardinalityFacetMustApplyToTheDeclaredResultType()
    {
        var result = new SpecVariableDeclaration(
            SpecVariableRole.Result,
            -1,
            IrTypeKind.String);
        var nonnegativeLength = new SpecBinaryDeclaration(
            IrBinaryOperator.GreaterThanOrEqual,
            new SpecLengthDeclaration(result),
            new SpecIntegerDeclaration(0),
            IrTypeKind.Boolean);
        var declaration = Declaration(
            "string-cardinality",
            IrTypeKind.String,
            SpecNullness.MaybeNull,
            SpecCardinality.Empty,
            [nonnegativeLength]);

        var exception = Assert.Throws<ArgumentException>(() =>
            ApiSpecTable.Create([declaration]));

        Assert.That(
            exception!.Message,
            Does.Contain("cardinality facet"));
    }

    [Test]
    public void PositiveResultFacetsRequireCompatibleResultTypes()
    {
        var inapplicableNullness = Declaration(
            "integer-nullness",
            IrTypeKind.Integer,
            SpecNullness.NonNull,
            SpecCardinality.NotApplicable,
            []);
        var inapplicableCardinality = Declaration(
            "reference-cardinality",
            IrTypeKind.Reference,
            SpecNullness.NonNull,
            SpecCardinality.Empty,
            []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Assert.Throws<ArgumentException>(() =>
                    ApiSpecTable.Create([inapplicableNullness]))!.Message,
                Does.Contain("nullness facet"));
            Assert.That(
                Assert.Throws<ArgumentException>(() =>
                    ApiSpecTable.Create([inapplicableCardinality]))!.Message,
                Does.Contain("cardinality facet"));
        }
    }

    [Test]
    public void CompatibleNonNullSequenceFacetsPermitTotalLength()
    {
        var result = new SpecVariableDeclaration(
            SpecVariableRole.Result,
            -1,
            IrTypeKind.Sequence);
        var declaration = Declaration(
            "sequence-cardinality",
            IrTypeKind.Sequence,
            SpecNullness.NonNull,
            SpecCardinality.Empty,
            [new SpecBinaryDeclaration(
                IrBinaryOperator.Equal,
                new SpecLengthDeclaration(result),
                new SpecIntegerDeclaration(0),
                IrTypeKind.Boolean)]);

        Assert.That(
            ApiSpecTable.Create([declaration]).Templates,
            Has.Length.EqualTo(1));
    }

    [TestCase(SpecEffect.ReadsReceiverState)]
    [TestCase(SpecEffect.WritesReceiverState)]
    [TestCase(SpecEffect.ReadsArgumentState)]
    [TestCase(SpecEffect.WritesArgumentState)]
    public void RegionalEffectsRequireCompatibleTargetShape(
        SpecEffect effect)
    {
        var declaration = Declaration(
            "incompatible-regional-effect-" + effect,
            resultType: null,
            SpecNullness.NotApplicable,
            SpecCardinality.NotApplicable,
            [],
            effects: effect);

        Assert.That(
            () => ApiSpecTable.Create([declaration]),
            Throws.ArgumentException.With.Message.Contains(
                "effect facet does not apply to the declared target"));
    }

    [Test]
    public void RegionalEffectsRemainValidForCompatibleTargetShapes()
    {
        var receiver = Declaration(
            "compatible-receiver-effects",
            resultType: null,
            SpecNullness.NotApplicable,
            SpecCardinality.NotApplicable,
            [],
            effects:
                SpecEffect.ReadsReceiverState |
                SpecEffect.WritesReceiverState,
            isStatic: false);
        var argument = Declaration(
            "compatible-argument-effects",
            resultType: null,
            SpecNullness.NotApplicable,
            SpecCardinality.NotApplicable,
            [],
            effects:
                SpecEffect.ReadsArgumentState |
                SpecEffect.WritesArgumentState,
            parameterTypes: [IrTypeKind.Integer]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ApiSpecTable.Create([receiver]).Templates,
                Has.Length.EqualTo(1));
            Assert.That(
                ApiSpecTable.Create([argument]).Templates,
                Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void StaticallyUnreachablePartialBranchesAreTotal()
    {
        var partial = PartialDivision();
        var declaration = Declaration(
            "unreachable-partial",
            resultType: null,
            SpecNullness.NotApplicable,
            SpecCardinality.NotApplicable,
            [
                new SpecBinaryDeclaration(
                    IrBinaryOperator.AndAlso,
                    new SpecBooleanDeclaration(false),
                    partial,
                    IrTypeKind.Boolean),
                new SpecBinaryDeclaration(
                    IrBinaryOperator.OrElse,
                    new SpecBooleanDeclaration(true),
                    partial,
                    IrTypeKind.Boolean),
                new SpecConditionalDeclaration(
                    new SpecBooleanDeclaration(true),
                    new SpecBooleanDeclaration(true),
                    partial,
                    IrTypeKind.Boolean),
                new SpecConditionalDeclaration(
                    new SpecBooleanDeclaration(false),
                    partial,
                    new SpecBooleanDeclaration(true),
                    IrTypeKind.Boolean)
            ]);

        Assert.That(
            ApiSpecTable.Create([declaration]).Templates,
            Has.Length.EqualTo(1));
    }

    [Test]
    public void StaticallyReachablePartialBranchesRemainNonTotal()
    {
        var partial = PartialDivision();
        var reached = new SpecTermDeclaration[]
        {
            new SpecBinaryDeclaration(
                IrBinaryOperator.AndAlso,
                new SpecBooleanDeclaration(true),
                partial,
                IrTypeKind.Boolean),
            new SpecBinaryDeclaration(
                IrBinaryOperator.OrElse,
                new SpecBooleanDeclaration(false),
                partial,
                IrTypeKind.Boolean),
            new SpecConditionalDeclaration(
                new SpecBooleanDeclaration(true),
                partial,
                new SpecBooleanDeclaration(true),
                IrTypeKind.Boolean)
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var condition in reached)
            {
                var declaration = Declaration(
                    "reachable-partial-" + condition.GetType().Name +
                    Array.IndexOf(reached, condition),
                    resultType: null,
                    SpecNullness.NotApplicable,
                    SpecCardinality.NotApplicable,
                    [condition]);
                Assert.Throws<ArgumentException>(() =>
                    ApiSpecTable.Create([declaration]));
            }
        }
    }

    [Test]
    public async Task SharedTermDagValidationRejectsInvalidRootWithinBound()
    {
        SpecTermDeclaration condition = new SpecBooleanDeclaration(true);
        for (var depth = 0; depth < 40; depth++)
        {
            condition = new SpecBinaryDeclaration(
                IrBinaryOperator.AndAlso,
                condition,
                condition,
                IrTypeKind.Boolean);
        }

        var invalidRoot = new SpecBinaryDeclaration(
            IrBinaryOperator.Equal,
            condition,
            new SpecIntegerDeclaration(0),
            IrTypeKind.Boolean);
        var declaration = Declaration(
            "shared-dag",
            resultType: null,
            SpecNullness.NotApplicable,
            SpecCardinality.NotApplicable,
            [invalidRoot]);
        var validation = Task.Run(() =>
            Assert.Throws<ArgumentException>(() =>
                ApiSpecTable.Create([declaration])));

        var exception = await validation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(
            exception!.Message,
            Does.Contain("Invalid binary spec expression types"));
    }

    private static SpecBinaryDeclaration Equal(
        SpecTermDeclaration left,
        SpecTermDeclaration right)
    {
        return new SpecBinaryDeclaration(
            IrBinaryOperator.Equal,
            left,
            right,
            IrTypeKind.Boolean);
    }

    private static SpecBinaryDeclaration PartialDivision()
    {
        return Equal(
            new SpecBinaryDeclaration(
                IrBinaryOperator.Divide,
                new SpecIntegerDeclaration(1),
                new SpecIntegerDeclaration(0),
                IrTypeKind.Integer),
            new SpecIntegerDeclaration(0));
    }

    private static ApiSpecDeclaration Declaration(
        string witness,
        IrTypeKind? resultType,
        SpecNullness nullness,
        SpecCardinality cardinality,
        ImmutableArray<SpecTermDeclaration> postconditions,
        SpecEffect effects = SpecEffect.Unknown,
        bool isStatic = true,
        ImmutableArray<IrTypeKind>? parameterTypes = null)
    {
        return new ApiSpecDeclaration(
            new ApiSpecTarget(
                witness,
                "M:Missing.Validation." + witness,
                "Missing.Validation",
                SpecTargetMemberKind.Method,
                witness,
                isStatic,
                0,
                isStatic ? null : IrTypeKind.Reference,
                parameterTypes ?? [],
                resultType,
                [new ApiSpecAssemblyIdentity("Missing", string.Empty)]),
            new ApiSpecFacets(
                new SpecEffectFacet(effects, Evidence),
                new SpecAllocationFacet(
                    SpecAllocationBehavior.Unknown,
                    Evidence),
                new SpecThrowFacet(
                    SpecThrowBehavior.Unknown,
                    [],
                    Evidence),
                new SpecNullnessFacet(nullness, Evidence),
                new SpecCardinalityFacet(
                    cardinality,
                    null,
                    Evidence)),
            [.. postconditions.Select(condition =>
                new SpecPostconditionDeclaration(condition, Evidence))]);
    }
}
