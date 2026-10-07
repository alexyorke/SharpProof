using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class Wave8SpecIntrinsicTypeAuditTests
{
    private static readonly SpecEvidence Evidence = new(SpecEvidenceKind.Documented, "wave8-intrinsic-type-audit");

    [TestCase("boolean", false)]
    [TestCase("integer", false)]
    [TestCase("string", false)]
    [TestCase("length", false)]
    [TestCase("boolean", true)]
    [TestCase("integer", true)]
    [TestCase("string", true)]
    [TestCase("length", true)]
    public void RelabeledIntrinsicTypesAreRejectedDuringTableCreation(string shape, bool normalCompletion)
    {
        var declaration = Declaration(Condition(shape, relabeled: true), normalCompletion);
        Assert.Throws<ArgumentException>(() =>
        {
            var template = ApiSpecTable.Create([declaration]).Templates.Single();
            var factory = new IrFactory();
            var result = ApiSpecInstantiator.InstantiatePostconditions(template, factory, new Dictionary<SpecVarId, IrTerm>());
            var instantiated = normalCompletion ? result.NormalCompletionCondition : result.Postconditions.FirstOrDefault();
            if (instantiated != null)
            {
                var builder = new IrProgramBuilder(factory);
                var entry = builder.CreateBlock("entry");
                try
                {
                    builder.Assume(entry, factory.CreateOperation("wave8:condition"), instantiated);
                    TestContext.Out.WriteLine("downstream IR assumption admitted");
                }
                catch (ArgumentException exception)
                {
                    TestContext.Out.WriteLine("downstream IR assumption rejected: " + exception.Message);
                }
            }
            TestContext.Out.WriteLine($"shape={shape}; completion={normalCompletion}; admitted=true; instantiation={result.Status}; actualType={(instantiated == null ? "absent" : factory.GetTypeInfo(instantiated.Type).Kind.ToString())}");
        });
    }

    [TestCase("boolean", false)]
    [TestCase("integer", false)]
    [TestCase("string", false)]
    [TestCase("length", false)]
    [TestCase("boolean", true)]
    [TestCase("integer", true)]
    [TestCase("string", true)]
    [TestCase("length", true)]
    public void CorrectIntrinsicTypesProduceBooleanConditions(string shape, bool normalCompletion)
    {
        var template = ApiSpecTable.Create([Declaration(Condition(shape, relabeled: false), normalCompletion)]).Templates.Single();
        var factory = new IrFactory();
        var result = ApiSpecInstantiator.InstantiatePostconditions(template, factory, new Dictionary<SpecVarId, IrTerm>());
        Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var condition = normalCompletion ? result.NormalCompletionCondition : result.Postconditions.Single();
        Assert.That(condition, Is.Not.Null);
        Assert.That(factory.GetTypeInfo(condition!.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
    }

    private static SpecTermDeclaration Condition(string shape, bool relabeled)
    {
        return shape switch
        {
            "boolean" => relabeled
                ? Equal(new SpecBooleanDeclaration(true) { Type = IrTypeKind.Integer }, new SpecIntegerDeclaration(1))
                : new SpecBooleanDeclaration(true),
            "integer" => relabeled
                ? new SpecIntegerDeclaration(1) { Type = IrTypeKind.Boolean }
                : Equal(new SpecIntegerDeclaration(1), new SpecIntegerDeclaration(1)),
            "string" => relabeled
                ? new SpecStringDeclaration("x") { Type = IrTypeKind.Boolean }
                : Equal(new SpecStringDeclaration("x"), new SpecStringDeclaration("x")),
            "length" => relabeled
                ? new SpecLengthDeclaration(new SpecStringDeclaration("x")) { Type = IrTypeKind.Boolean }
                : Equal(new SpecLengthDeclaration(new SpecStringDeclaration("x")), new SpecIntegerDeclaration(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private static SpecBinaryDeclaration Equal(SpecTermDeclaration left, SpecTermDeclaration right)
    {
        return new(IrBinaryOperator.Equal, left, right, IrTypeKind.Boolean);
    }

    private static ApiSpecDeclaration Declaration(SpecTermDeclaration condition, bool normalCompletion)
    {
        return new(
            new ApiSpecTarget("wave8.term-type", "M:Wave8.Term.Target", "Wave8.Term", SpecTargetMemberKind.Method,
                "Target", true, 0, null, [], null, [new ApiSpecAssemblyIdentity("Wave8", string.Empty)]),
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
