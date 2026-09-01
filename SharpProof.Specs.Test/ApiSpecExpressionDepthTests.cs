using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecExpressionDepthTests
{
    private const int MaximumExpressionDepth = 256;
    private static readonly SpecEvidence Evidence = new(
        SpecEvidenceKind.Documented,
        "expression-depth-test");

    [Test]
    public void ExpressionAtDepthLimitValidatesDigestsAndInstantiates()
    {
        var table = ApiSpecTable.Create([
            Declaration(NestedNot(MaximumExpressionDepth))
        ]);
        var result = ApiSpecInstantiator.InstantiatePostconditions(
            table.Templates.Single(),
            new IrFactory(),
            ImmutableDictionary<SpecVarId, IrTerm>.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.ContentSha256, Has.Length.EqualTo(64));
            Assert.That(
                result.Status,
                Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(result.Postconditions, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void ExpressionBeyondDepthLimitIsRejectedBeforeDigesting()
    {
        var declaration = Declaration(
            NestedNot(MaximumExpressionDepth + 1));

        var exception = Assert.Throws<ArgumentException>(() =>
            ApiSpecTable.Create([declaration]));

        Assert.That(
            exception!.Message,
            Does.Contain("expression depth limit"));
    }

    private static SpecTermDeclaration NestedNot(int depth)
    {
        SpecTermDeclaration result = new SpecBooleanDeclaration(true);
        for (var current = 1; current < depth; current++)
        {
            result = new SpecUnaryDeclaration(
                IrUnaryOperator.Not,
                result,
                IrTypeKind.Boolean);
        }
        return result;
    }

    private static ApiSpecDeclaration Declaration(
        SpecTermDeclaration condition)
    {
        return new ApiSpecDeclaration(
            new ApiSpecTarget(
                "expression-depth",
                "M:Missing.ExpressionDepth.Run",
                "Missing.ExpressionDepth",
                SpecTargetMemberKind.Method,
                "Run",
                true,
                0,
                null,
                [],
                null,
                [new ApiSpecAssemblyIdentity("Missing", string.Empty)]),
            new ApiSpecFacets(
                new SpecEffectFacet(SpecEffect.None, Evidence),
                new SpecAllocationFacet(
                    SpecAllocationBehavior.None,
                    Evidence),
                new SpecThrowFacet(
                    SpecThrowBehavior.DoesNotThrow,
                    [],
                    Evidence),
                new SpecNullnessFacet(
                    SpecNullness.NotApplicable,
                    Evidence),
                new SpecCardinalityFacet(
                    SpecCardinality.NotApplicable,
                    null,
                    Evidence)),
            [new SpecPostconditionDeclaration(condition, Evidence)]);
    }
}
