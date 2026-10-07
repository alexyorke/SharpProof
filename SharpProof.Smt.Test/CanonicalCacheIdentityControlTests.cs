using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CanonicalCacheIdentityControlTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SameCacheKeepsItsIdentityThroughAnObjectView(bool array, bool wrongClaim)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        IrTerm cached = array ? factory.EmptyArray(factory.GetOrCreateSequenceType(factory.IntegerType)) : factory.String(string.Empty);
        var view = factory.CreateVariable("view", factory.ObjectType);
        var alias = factory.Binary(IrBinaryOperator.Equal, factory.Variable(view), factory.Cast(factory.ObjectType, cached));
        var goal = wrongClaim ? factory.Unary(IrUnaryOperator.Not, alias) : alias;
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = new VerificationQuery(factory,
            [new Assumption(factory, alias, new LoweredJustification(factory.CreateOperation()))],
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [view]);
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, wrongClaim ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>(), outcome.ToString());
    }

    [Test]
    public async Task DifferentArrayTypeViewsCanAliasOneUncachedZeroLengthArray()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.GetOrCreateSequenceType(factory.IntegerType));
        var second = factory.CreateVariable("second", factory.GetOrCreateSequenceType(factory.GetOrCreateIntegerType(32, false)));
        var alias = factory.Binary(IrBinaryOperator.Equal,
            factory.Cast(factory.ObjectType, factory.Variable(first)), factory.Cast(factory.ObjectType, factory.Variable(second)));
        var assumptions = new[]
        {
            new Assumption(factory, factory.Binary(IrBinaryOperator.Equal, factory.Length(factory.Variable(first)), factory.Integer(0)), new LoweredJustification(factory.CreateOperation())),
            new Assumption(factory, factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(first), factory.Null(factory.GetVariableInfo(first).Type)), new LoweredJustification(factory.CreateOperation())),
            new Assumption(factory, alias, new LoweredJustification(factory.CreateOperation()))
        };
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = new VerificationQuery(factory, assumptions,
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [first, second]);
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
    }
}
