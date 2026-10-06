using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CachedReferenceObjectViewControlTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task TypedCachedValueAndObjectViewKeepTheirAlias(bool array)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.StringType;
        IrTerm cached = array ? factory.EmptyArray(type) : factory.String("text");
        var typed = factory.CreateVariable("typed", type);
        var view = factory.CreateVariable("view", factory.ObjectType);
        var query = new VerificationQuery(factory,
            [Assume(factory, factory.Variable(typed), cached),
             Assume(factory, factory.Variable(view), factory.Cast(factory.ObjectType, factory.Variable(typed)))],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [view, typed]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
        var model = ((RefutedOutcome)outcome).Model.Assignments;
        Assert.That(model[view].Reference, Is.SameAs(array ? model[typed] : model[typed].String));
    }

    [Test]
    public async Task DistinctLiteralViewsRemainDistinct()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.ObjectType);
        var second = factory.CreateVariable("second", factory.ObjectType);
        var query = new VerificationQuery(factory,
            [Assume(factory, factory.Variable(first), factory.Cast(factory.ObjectType, factory.String("first"))),
             Assume(factory, factory.Variable(second), factory.Cast(factory.ObjectType, factory.String("second")))],
            new Goal(factory, factory.Binary(IrBinaryOperator.Equal, factory.Variable(first), factory.Variable(second)),
                ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [first, second]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
        var model = ((RefutedOutcome)outcome).Model.Assignments;
        Assert.That(model[first].Reference, Is.Not.SameAs(model[second].Reference));
    }

    private static Assumption Assume(IrFactory factory, IrTerm left, IrTerm right)
    {
        return new(factory, factory.Binary(IrBinaryOperator.Equal, left, right),
            new LoweredJustification(factory.CreateOperation()));
    }
}
