using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CanonicalCacheDynamicViewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompatibleInputViewCanAliasTheOtherCanonicalCache(bool unsignedCache)
    {
        object runtimeCache = unsignedCache ? Array.Empty<uint>() : Array.Empty<int>();
        object runtimeView = unsignedCache ? (int[])runtimeCache : (uint[])runtimeCache;
        Assert.That(ReferenceEquals(runtimeCache, runtimeView), Is.True);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.GetOrCreateIntegerType(32, !unsignedCache));
        var otherType = factory.GetOrCreateSequenceType(factory.GetOrCreateIntegerType(32, unsignedCache));
        var cached = factory.Cast(factory.ObjectType, factory.EmptyArray(type));
        var otherCached = factory.Cast(factory.ObjectType, factory.EmptyArray(otherType));
        var input = factory.CreateVariable("input", otherType);
        var view = factory.Cast(factory.ObjectType, factory.Variable(input));
        var alias = factory.Binary(IrBinaryOperator.Equal, view, cached);
        var distinctCaches = factory.Binary(IrBinaryOperator.NotEqual, cached, otherCached);
        var query = new VerificationQuery(factory,
            [new Assumption(factory, alias, new LoweredJustification(factory.CreateOperation())),
             new Assumption(factory, distinctCaches, new LoweredJustification(factory.CreateOperation()))],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [input]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
    }
}
