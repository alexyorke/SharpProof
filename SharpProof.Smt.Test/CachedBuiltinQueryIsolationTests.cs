using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CachedBuiltinQueryIsolationTests
{
    [TestCase("bool-array", false)]
    [TestCase("uint-array", false)]
    [TestCase("empty-string", false)]
    [TestCase("bool-array", true)]
    [TestCase("uint-array", true)]
    [TestCase("empty-string", true)]
    public async Task IndependentCachedBuiltinsDoNotBlockConcreteCounterexamples(string otherKind, bool previousQuery)
    {
        Assert.That(ReferenceEquals(Array.Empty<int>(), otherKind switch
        {
            "bool-array" => (object)Array.Empty<bool>(),
            "uint-array" => Array.Empty<uint>(),
            _ => string.Empty
        }), Is.False);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        IrTerm first = factory.EmptyArray(factory.GetOrCreateSequenceType(factory.IntegerType));
        IrTerm second = otherKind switch
        {
            "bool-array" => factory.EmptyArray(factory.GetOrCreateSequenceType(factory.BooleanType)),
            "uint-array" => factory.EmptyArray(factory.GetOrCreateSequenceType(factory.GetOrCreateIntegerType(32, false))),
            _ => factory.String(string.Empty)
        };
        var known = factory.Binary(IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.NotEqual, first, factory.Null(first.Type)),
            factory.Binary(IrBinaryOperator.NotEqual, second, factory.Null(second.Type)));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        if (previousQuery)
        {
            var priming = new VerificationQuery(factory, [],
                new Goal(factory, known, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
            Assert.That(await new ProofKernel(session).VerifyAsync(priming), Is.TypeOf<ProvenOutcome>());
        }
        var assumptions = previousQuery ? Array.Empty<Assumption>() :
            new[] { new Assumption(factory, known, new LoweredJustification(factory.CreateOperation())) };
        var query = new VerificationQuery(factory, assumptions,
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(1)));
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
        Assert.That(((RefutedOutcome)outcome).Model.Assignments, Is.Empty);
    }
}
