using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CachedReferenceObjectViewTests
{
    [TestCase("")]
    [TestCase("text")]
    [TestCase("a\0b")]
    public async Task ObjectViewOfLiteralUsesItsConcreteIdentity(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var literal = factory.String(text);
        await AssertObjectView(factory, literal, factory.GetString(literal.Value));
    }

    [Test]
    public async Task ObjectViewOfCanonicalEmptyArrayUsesItsConcreteIdentity()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        await AssertObjectView(factory, factory.EmptyArray(type), factory.CreateEmptyArrayValue(type));
    }

    private static async Task AssertObjectView(IrFactory factory, IrTerm original, object identity)
    {
        var view = factory.CreateVariable("view", factory.ObjectType);
        var predicate = factory.Binary(IrBinaryOperator.Equal, factory.Variable(view), factory.Cast(factory.ObjectType, original));
        var query = new VerificationQuery(factory,
            [new Assumption(factory, predicate, new LoweredJustification(factory.CreateOperation()))],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [view]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
        var model = ((RefutedOutcome)outcome).Model.Assignments;
        Assert.That(model[view].Reference, Is.SameAs(identity));
        var replay = new IrInterpreter(factory).Evaluate(predicate, model);
        Assert.That(replay.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(replay.Value!.Boolean, Is.True);
    }
}
