using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CanonicalCacheIdentityOrderTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task StringAndArrayCachesAreDistinctInEitherEncodingOrder(bool arrayFirst)
    {
        Assert.That(ReferenceEquals(string.Empty, Array.Empty<int>()), Is.False);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.Cast(factory.ObjectType, factory.String(string.Empty));
        var array = factory.Cast(factory.ObjectType, factory.EmptyArray(factory.GetOrCreateSequenceType(factory.IntegerType)));
        var predicate = factory.Binary(IrBinaryOperator.NotEqual, arrayFirst ? array : text, arrayFirst ? text : array);
        Assert.That(new IrInterpreter(factory).Evaluate(predicate).Value!.Boolean, Is.True);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(new VerificationQuery(factory, [],
            new Goal(factory, predicate, ProofDiagnosticKind.Postcondition, new SourceLocationId(0))));
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>(), outcome.ToString());
    }
}
