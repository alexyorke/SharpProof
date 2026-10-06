using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class DynamicBuiltinWitnessControlTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task DistinctArrayTypesRequireConcreteAliasReplay(bool requireAlias)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.GetOrCreateSequenceType(factory.BooleanType));
        var second = factory.CreateVariable("second", factory.GetOrCreateSequenceType(factory.IntegerType));
        var assumptions = new List<Assumption>();
        foreach (var variable in new[] { first, second })
        {
            assumptions.Add(new Assumption(factory,
                factory.Binary(IrBinaryOperator.Equal, factory.Length(factory.Variable(variable)), factory.Integer(1)),
                new LoweredJustification(factory.CreateOperation())));
        }
        if (requireAlias)
        {
            assumptions.Add(new Assumption(factory,
                factory.Binary(IrBinaryOperator.Equal, factory.Cast(factory.ObjectType, factory.Variable(first)),
                    factory.Cast(factory.ObjectType, factory.Variable(second))),
                new LoweredJustification(factory.CreateOperation())));
        }
        var query = new VerificationQuery(factory, assumptions,
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [first, second]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, requireAlias ? Is.Not.TypeOf<RefutedOutcome>() : Is.TypeOf<RefutedOutcome>(), outcome.ToString());
    }
}
