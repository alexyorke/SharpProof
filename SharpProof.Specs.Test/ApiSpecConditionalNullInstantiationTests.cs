using NUnit.Framework;
using SharpProof.Ir;
using static SharpProof.Testing.ApiSpecTestFacets;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecConditionalNullInstantiationTests
{
    private static readonly SpecEvidence Evidence =
        new(SpecEvidenceKind.Observed, "conditional-null-instantiation");
    private static readonly SpecVariableDeclaration Condition =
        new(SpecVariableRole.Parameter, 0, IrTypeKind.Boolean);
    private static readonly SpecVariableDeclaration Reference =
        new(SpecVariableRole.Parameter, 1, IrTypeKind.Reference);
    private static readonly SpecNullDeclaration NullReference =
        new(IrTypeKind.Reference);
    private static readonly ApiSpecTarget Target = new(
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
        [new ApiSpecAssemblyIdentity("ConditionalNull", string.Empty)]);
    private static readonly ApiSpecFacets Facets = NeutralFacets(Evidence);
    private static readonly IrFactory Factory = new();
    private static readonly IrTerm ConditionTerm = Factory.Variable(
        Factory.CreateVariable("condition", Factory.BooleanType));
    private static readonly IrTerm ReferenceTerm = Factory.Variable(
        Factory.CreateVariable(
            "reference",
            Factory.GetOrCreateReferenceType(
                Factory.CreateIdentity(),
                "Widget")));

    [TestCase(true)]
    [TestCase(false)]
    public void ReferenceNullBranchUsesExactSubstitutedPeerType(
        bool nullWhenTrue)
    {
        var conditional = new SpecConditionalDeclaration(
            Condition,
            nullWhenTrue ? NullReference : Reference,
            nullWhenTrue ? Reference : NullReference,
            IrTypeKind.Reference);
        var template = CreateTemplate(new SpecBinaryDeclaration(
            IrBinaryOperator.NotEqual,
            conditional,
            NullReference,
            IrTypeKind.Boolean));

        var result = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            Factory,
            new Dictionary<SpecVarId, IrTerm>
            {
                [template.Parameters[0]] = ConditionTerm,
                [template.Parameters[1]] = ReferenceTerm
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
            Assert.That(instantiated!.Type, Is.EqualTo(ReferenceTerm.Type));
            Assert.That(instantiated.WhenTrue.Type, Is.EqualTo(ReferenceTerm.Type));
            Assert.That(instantiated.WhenFalse.Type, Is.EqualTo(ReferenceTerm.Type));
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
            Target,
            Facets,
            [new SpecPostconditionDeclaration(postcondition, Evidence)]);
        return ApiSpecTable.Create([declaration]).Templates.Single();
    }
}
