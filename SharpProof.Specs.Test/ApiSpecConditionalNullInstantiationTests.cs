using NUnit.Framework;
using SharpProof.Ir;
using static SharpProof.Testing.ApiSpecTestFacets;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecConditionalNullInstantiationTests
{
    private static readonly SpecEvidence Evidence =
        new(SpecEvidenceKind.Observed, "conditional-null-instantiation");

    [TestCase(true)]
    [TestCase(false)]
    public void ReferenceNullBranchUsesExactSubstitutedPeerType(
        bool nullWhenTrue)
    {
        var condition = new SpecVariableDeclaration(
            SpecVariableRole.Parameter,
            0,
            IrTypeKind.Boolean);
        var reference = new SpecVariableDeclaration(
            SpecVariableRole.Parameter,
            1,
            IrTypeKind.Reference);
        var nullReference = new SpecNullDeclaration(IrTypeKind.Reference);
        var conditional = new SpecConditionalDeclaration(
            condition,
            nullWhenTrue ? nullReference : reference,
            nullWhenTrue ? reference : nullReference,
            IrTypeKind.Reference);
        var template = CreateTemplate(new SpecBinaryDeclaration(
            IrBinaryOperator.NotEqual,
            conditional,
            nullReference,
            IrTypeKind.Boolean));
        var factory = new IrFactory();
        var widgetType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Widget");
        var conditionTerm = factory.Variable(
            factory.CreateVariable("condition", factory.BooleanType));
        var referenceTerm = factory.Variable(
            factory.CreateVariable("reference", widgetType));

        var result = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm>
            {
                [template.Parameters[0]] = conditionTerm,
                [template.Parameters[1]] = referenceTerm
            });

        Assert.That(
            result.Status,
            Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var comparison = result.Postconditions.Single() as IrBinaryTerm;
        Assert.That(comparison, Is.Not.Null);
        var instantiated = comparison!.Left as IrConditionalTerm;
        Assert.That(instantiated, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(instantiated!.Type, Is.EqualTo(widgetType));
            Assert.That(instantiated.WhenTrue.Type, Is.EqualTo(widgetType));
            Assert.That(instantiated.WhenFalse.Type, Is.EqualTo(widgetType));
            Assert.That(
                nullWhenTrue
                    ? instantiated.WhenTrue
                    : instantiated.WhenFalse,
                Is.TypeOf<IrNullTerm>());
        }
    }

    private static ApiSpecTemplate CreateTemplate(
        SpecTermDeclaration postcondition)
    {
        var declaration = new ApiSpecDeclaration(
            new ApiSpecTarget(
                "conditional-null-" + Guid.NewGuid().ToString("N"),
                "M:ConditionalNull.Target",
                "ConditionalNull",
                SpecTargetMemberKind.Method,
                "Target",
                true,
                0,
                null,
                [IrTypeKind.Boolean, IrTypeKind.Reference],
                null,
                [new ApiSpecAssemblyIdentity("ConditionalNull", string.Empty)]),
            NeutralFacets(Evidence),
            [new SpecPostconditionDeclaration(postcondition, Evidence)]);
        return ApiSpecTable.Create([declaration]).Templates.Single();
    }
}
