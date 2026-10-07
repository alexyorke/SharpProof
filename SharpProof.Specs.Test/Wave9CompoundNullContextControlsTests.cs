using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class Wave9CompoundNullContextControlsTests
{
    private static readonly SpecEvidence Evidence = new(SpecEvidenceKind.Documented, "wave9-compound-null-controls");

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void ReverseContextRetainsExactTypeAndConditionalGuards(bool sequence, bool nested, bool normalCompletion)
    {
        var kind = sequence ? IrTypeKind.Sequence : IrTypeKind.Reference;
        var flag = new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Boolean);
        var value = new SpecVariableDeclaration(SpecVariableRole.Parameter, 1, kind);
        var nil = new SpecNullDeclaration(kind);
        var allNull = new SpecConditionalDeclaration(flag, nil, nil, kind);
        var right = nested
            ? new SpecConditionalDeclaration(flag, value, allNull, kind)
            : allNull;
        var condition = new SpecBinaryDeclaration(IrBinaryOperator.NotEqual, value, right, IrTypeKind.Boolean);
        var template = ApiSpecTable.Create([Declaration(condition, kind, normalCompletion)]).Templates.Single();
        var factory = new IrFactory();
        var type = sequence
            ? factory.GetOrCreateSequenceType(factory.IntegerType)
            : factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Widget");
        var flagTerm = factory.Variable(factory.CreateVariable("flag", factory.BooleanType));
        var valueTerm = factory.Variable(factory.CreateVariable("value", type));
        var result = Instantiate(template, factory, flagTerm, valueTerm);
        TestContext.Out.WriteLine($"sequence={sequence}; nested={nested}; completion={normalCompletion}; status={result.Status}; failure={result.Failure?.Kind}; detail={result.Failure?.Detail}");
        Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var instantiated = normalCompletion ? result.NormalCompletionCondition : result.Postconditions.Single();
        Assert.That(instantiated, Is.TypeOf<IrBinaryTerm>());
        var comparison = (IrBinaryTerm)instantiated!;
        Assert.That(comparison.Type, Is.EqualTo(factory.BooleanType));
        Assert.That(comparison.Left, Is.SameAs(valueTerm));
        Assert.That(comparison.Right.Type, Is.EqualTo(type));
        Assert.That(comparison.Right, Is.TypeOf<IrConditionalTerm>());
        var conditional = (IrConditionalTerm)comparison.Right;
        Assert.That(conditional.Condition, Is.SameAs(flagTerm));
        if (nested)
        {
            Assert.That(conditional.WhenTrue, Is.SameAs(valueTerm));
            Assert.That(conditional.WhenFalse, Is.TypeOf<IrConditionalTerm>());
            conditional = (IrConditionalTerm)conditional.WhenFalse;
            Assert.That(conditional.Condition, Is.SameAs(flagTerm));
        }

        Assert.That(conditional.WhenTrue, Is.TypeOf<IrNullTerm>());
        Assert.That(conditional.WhenFalse, Is.TypeOf<IrNullTerm>());
        Assert.That(conditional.WhenTrue.Type, Is.EqualTo(type));
        Assert.That(conditional.WhenFalse.Type, Is.EqualTo(type));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DirectSequenceNullRetainsConcretePeer(bool normalCompletion)
    {
        var kind = IrTypeKind.Sequence;
        var value = new SpecVariableDeclaration(SpecVariableRole.Parameter, 1, kind);
        var condition = new SpecBinaryDeclaration(IrBinaryOperator.Equal,
            new SpecNullDeclaration(kind), value, IrTypeKind.Boolean);
        var template = ApiSpecTable.Create([Declaration(condition, kind, normalCompletion)]).Templates.Single();
        var factory = new IrFactory();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var result = Instantiate(template, factory, factory.Boolean(true),
            factory.Variable(factory.CreateVariable("value", type)));
        Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var instantiated = normalCompletion ? result.NormalCompletionCondition : result.Postconditions.Single();
        var equality = (IrBinaryTerm)instantiated!;
        Assert.That(equality.Left, Is.TypeOf<IrNullTerm>());
        Assert.That(equality.Left.Type, Is.EqualTo(type));
        Assert.That(equality.Right.Type, Is.EqualTo(type));
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void IncompatibleConcretePeersRemainRejected(bool sequence, bool conditional, bool normalCompletion)
    {
        var kind = sequence ? IrTypeKind.Sequence : IrTypeKind.Reference;
        var flag = new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Boolean);
        var left = new SpecVariableDeclaration(SpecVariableRole.Parameter, 1, kind);
        var right = new SpecVariableDeclaration(SpecVariableRole.Parameter, 2, kind);
        var branches = conditional
            ? new SpecConditionalDeclaration(flag, left, right, kind)
            : new SpecConditionalDeclaration(flag, new SpecNullDeclaration(kind), left, kind);
        var condition = new SpecBinaryDeclaration(IrBinaryOperator.Equal, branches, right, IrTypeKind.Boolean);
        var template = ApiSpecTable.Create([Declaration(condition, kind, normalCompletion, twoValues: true)]).Templates.Single();
        var factory = new IrFactory();
        var leftType = sequence
            ? factory.GetOrCreateSequenceType(factory.IntegerType)
            : factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Widget");
        var rightType = sequence
            ? factory.GetOrCreateSequenceType(factory.StringType)
            : factory.GetOrCreateReferenceType(factory.CreateIdentity(), "OtherWidget");
        var result = ApiSpecInstantiator.InstantiatePostconditions(template, factory, new Dictionary<SpecVarId, IrTerm>
        {
            [template.Parameters[0]] = factory.Variable(factory.CreateVariable("flag", factory.BooleanType)),
            [template.Parameters[1]] = factory.Variable(factory.CreateVariable("left", leftType)),
            [template.Parameters[2]] = factory.Variable(factory.CreateVariable("right", rightType))
        });
        Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Failed));
        Assert.That(result.Failure?.Kind, Is.EqualTo(conditional
            ? SpecInstantiationFailureKind.InvalidExpression
            : SpecInstantiationFailureKind.TypeMismatch));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SequenceNullWithoutConcreteContextRemainsUnsupported(bool compound, bool normalCompletion)
    {
        var kind = IrTypeKind.Sequence;
        var flag = new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Boolean);
        var nil = new SpecNullDeclaration(kind);
        SpecTermDeclaration operand = compound
            ? new SpecConditionalDeclaration(flag, nil, nil, kind)
            : nil;
        var condition = new SpecBinaryDeclaration(IrBinaryOperator.Equal, operand, nil, IrTypeKind.Boolean);
        var template = ApiSpecTable.Create([Declaration(condition, kind, normalCompletion)]).Templates.Single();
        var factory = new IrFactory();
        var result = Instantiate(template, factory,
            factory.Variable(factory.CreateVariable("flag", factory.BooleanType)),
            factory.Variable(factory.CreateVariable("unused", factory.GetOrCreateSequenceType(factory.IntegerType))));
        Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Failed));
        Assert.That(result.Failure?.Kind, Is.EqualTo(SpecInstantiationFailureKind.UnsupportedValueType));
    }

    private static SpecInstantiationResult Instantiate(ApiSpecTemplate template, IrFactory factory, IrTerm flag, IrTerm value)
    {
        return ApiSpecInstantiator.InstantiatePostconditions(template, factory, new Dictionary<SpecVarId, IrTerm>
        {
            [template.Parameters[0]] = flag,
            [template.Parameters[1]] = value
        });
    }

    private static ApiSpecDeclaration Declaration(
        SpecTermDeclaration condition, IrTypeKind kind, bool normalCompletion, bool twoValues = false)
    {
        return new ApiSpecDeclaration(
            new ApiSpecTarget("wave9.compound-null-controls", "M:Wave9.Null.Controls", "Wave9.Null", SpecTargetMemberKind.Method,
                "Controls", true, 0, null, twoValues ? [IrTypeKind.Boolean, kind, kind] : [IrTypeKind.Boolean, kind], null,
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
