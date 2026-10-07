using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class Wave9CompoundNullInstantiationAuditTests
{
    private static readonly SpecEvidence Evidence = new(SpecEvidenceKind.Documented, "wave9-compound-null-audit");

    [TestCase("direct", false, false)]
    [TestCase("both-null", false, false)]
    [TestCase("nested", false, false)]
    [TestCase("direct", true, false)]
    [TestCase("both-null", true, false)]
    [TestCase("nested", true, false)]
    [TestCase("direct", false, true)]
    [TestCase("both-null", false, true)]
    [TestCase("nested", false, true)]
    [TestCase("direct", true, true)]
    [TestCase("both-null", true, true)]
    [TestCase("nested", true, true)]
    public void CompoundNullsRetainTheirConcreteReferenceContext(string shape, bool nominal, bool normalCompletion)
    {
        var flag = new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Boolean);
        var reference = new SpecVariableDeclaration(SpecVariableRole.Parameter, 1, IrTypeKind.Reference);
        var nil = new SpecNullDeclaration(IrTypeKind.Reference);
        var allNull = new SpecConditionalDeclaration(flag, nil, nil, IrTypeKind.Reference);
        var left = shape switch
        {
            "direct" => (SpecTermDeclaration)nil,
            "both-null" => allNull,
            "nested" => new SpecConditionalDeclaration(flag, allNull, reference, IrTypeKind.Reference),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var condition = new SpecBinaryDeclaration(IrBinaryOperator.Equal, left, reference, IrTypeKind.Boolean);
        var template = ApiSpecTable.Create([Declaration(condition, normalCompletion)]).Templates.Single();
        var factory = new IrFactory();
        var referenceType = nominal
            ? factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Widget")
            : factory.ObjectType;
        var result = ApiSpecInstantiator.InstantiatePostconditions(template, factory, new Dictionary<SpecVarId, IrTerm>
        {
            [template.Parameters[0]] = factory.Variable(factory.CreateVariable("flag", factory.BooleanType)),
            [template.Parameters[1]] = factory.Variable(factory.CreateVariable("reference", referenceType))
        });
        TestContext.Out.WriteLine($"shape={shape}; nominal={nominal}; completion={normalCompletion}; status={result.Status}; failure={result.Failure?.Kind}; detail={result.Failure?.Detail}");
        Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var instantiated = normalCompletion ? result.NormalCompletionCondition : result.Postconditions.Single();
        Assert.That(instantiated, Is.TypeOf<IrBinaryTerm>());
        var equality = (IrBinaryTerm)instantiated!;
        Assert.That(equality.Type, Is.EqualTo(factory.BooleanType));
        Assert.That(equality.Left.Type, Is.EqualTo(referenceType));
        Assert.That(equality.Right.Type, Is.EqualTo(referenceType));
    }

    private static ApiSpecDeclaration Declaration(SpecTermDeclaration condition, bool normalCompletion)
    {
        return new ApiSpecDeclaration(
            new ApiSpecTarget("wave9.compound-null", "M:Wave9.Null.Target", "Wave9.Null", SpecTargetMemberKind.Method,
                "Target", true, 0, null, [IrTypeKind.Boolean, IrTypeKind.Reference], null,
                [new ApiSpecAssemblyIdentity("Wave9", string.Empty)]),
            new ApiSpecFacets(
                new SpecEffectFacet(SpecEffect.Unknown, Evidence),
                new SpecAllocationFacet(SpecAllocationBehavior.Unknown, Evidence),
                new SpecThrowFacet(normalCompletion ? SpecThrowBehavior.MayThrow : SpecThrowBehavior.Unknown,
                    normalCompletion ? ["System.Exception"] : [], Evidence, normalCompletion ? condition : null),
                new SpecNullnessFacet(SpecNullness.NotApplicable, Evidence),
                new SpecCardinalityFacet(SpecCardinality.NotApplicable, null, Evidence)),
            normalCompletion ? [] : [new SpecPostconditionDeclaration(condition, Evidence)]);
    }
}
