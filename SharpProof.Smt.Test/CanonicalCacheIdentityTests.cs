using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CanonicalCacheIdentityTests
{
    [TestCase("bool-array")]
    [TestCase("uint-array")]
    [TestCase("empty-string")]
    public async Task DifferentCanonicalCachesHaveDifferentIdentities(string otherKind)
    {
        object runtimeFirst = Array.Empty<int>();
        object runtimeSecond = otherKind switch
        {
            "bool-array" => Array.Empty<bool>(),
            "uint-array" => Array.Empty<uint>(),
            _ => string.Empty
        };
        Assert.That(ReferenceEquals(runtimeFirst, runtimeSecond), Is.False);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        IrTerm first = factory.EmptyArray(factory.GetOrCreateSequenceType(factory.IntegerType));
        IrTerm second = otherKind switch
        {
            "bool-array" => factory.EmptyArray(factory.GetOrCreateSequenceType(factory.BooleanType)),
            "uint-array" => factory.EmptyArray(factory.GetOrCreateSequenceType(factory.GetOrCreateIntegerType(32, false))),
            _ => factory.String(string.Empty)
        };
        var unequal = factory.Binary(IrBinaryOperator.NotEqual,
            factory.Cast(factory.ObjectType, first), factory.Cast(factory.ObjectType, second));
        var concrete = new IrInterpreter(factory).Evaluate(unequal);
        Assert.That(concrete.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(concrete.Value!.Boolean, Is.True);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = new VerificationQuery(factory, [],
            new Goal(factory, unequal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>(), outcome.ToString());
    }
}
