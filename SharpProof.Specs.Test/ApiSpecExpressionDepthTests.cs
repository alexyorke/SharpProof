using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using static SharpProof.Testing.ApiSpecTestFacets;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecExpressionDepthTests
{
    private const int MaximumExpressionDepth = 256;
    private static readonly SpecEvidence Evidence = new(
        SpecEvidenceKind.Documented,
        "expression-depth-test");

    [Test]
    public void SharedExpressionExpansionBeyondWorkLimitIsRejected()
    {
        SpecTermDeclaration expression = new SpecBooleanDeclaration(true);
        for (var level = 0; level < 18; level++)
        {
            expression = new SpecBinaryDeclaration(IrBinaryOperator.AndAlso,
                expression, expression, IrTypeKind.Boolean);
        }
        Assert.Throws<ArgumentException>(() => ApiSpecTable.Create([Declaration(expression)]));
    }

    [Test]
    public void SharedSubtreeCannotBypassDepthLimit()
    {
        var shared = NestedNot(200);
        SpecTermDeclaration nested = shared;
        for (var level = 0; level < 100; level++)
        {
            nested = new SpecUnaryDeclaration(IrUnaryOperator.Not, nested, IrTypeKind.Boolean);
        }
        var expression = new SpecBinaryDeclaration(IrBinaryOperator.AndAlso,
            shared, nested, IrTypeKind.Boolean);
        Assert.Throws<ArgumentException>(() => ApiSpecTable.Create([Declaration(expression)]));
    }

    [Test]
    public void BoundedSharedExpressionPreservesExpandedDigestAndInstantiation()
    {
        var shared = NestedNot(4);
        var sharedTable = ApiSpecTable.Create([Declaration(new SpecBinaryDeclaration(
            IrBinaryOperator.AndAlso, shared, shared, IrTypeKind.Boolean))]);
        var expandedTable = ApiSpecTable.Create([Declaration(new SpecBinaryDeclaration(
            IrBinaryOperator.AndAlso, NestedNot(4), NestedNot(4), IrTypeKind.Boolean))]);
        Assert.That(sharedTable.ContentSha256, Is.EqualTo(expandedTable.ContentSha256));
        var result = ApiSpecInstantiator.InstantiatePostconditions(
            sharedTable.Templates.Single(), new IrFactory(),
            ImmutableDictionary<SpecVarId, IrTerm>.Empty);
        Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
    }

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
            NeutralFacets(Evidence),
            [new SpecPostconditionDeclaration(condition, Evidence)]);
    }
}
