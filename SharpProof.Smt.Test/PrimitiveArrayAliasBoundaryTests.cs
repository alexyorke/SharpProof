using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class PrimitiveArrayAliasBoundaryTests
{
    [TestCase(8, false)]
    [TestCase(16, false)]
    [TestCase(32, false)]
    [TestCase(64, false)]
    [TestCase(8, true)]
    [TestCase(16, true)]
    [TestCase(32, true)]
    [TestCase(64, true)]
    public async Task CompatibleArrayAliasWitnessReplays(int width, bool unsignedFirst)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var signedType = factory.GetOrCreateIntegerType(width, true);
        var unsignedType = factory.GetOrCreateIntegerType(width, false);
        var signed = factory.CreateVariable("signed", factory.GetOrCreateSequenceType(signedType));
        var unsigned = factory.CreateVariable("unsigned", factory.GetOrCreateSequenceType(unsignedType));
        var x = factory.Variable(signed);
        var y = factory.Variable(unsigned);
        var bits = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        var query = new VerificationQuery(factory,
            [Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.Length(x), factory.Integer(1))),
             Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.Length(y), factory.Integer(1))),
             Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.Cast(factory.ObjectType, x), factory.Cast(factory.ObjectType, y))),
             Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.SequenceAccess(x, factory.Integer(0)), factory.IntegerBits(signedType, bits))),
             Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.SequenceAccess(y, factory.Integer(0)), factory.IntegerBits(unsignedType, bits)))],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)),
            unsignedFirst ? [unsigned, signed] : [signed, unsigned]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
    }

    [TestCase(8)]
    [TestCase(16)]
    [TestCase(32)]
    [TestCase(64)]
    public async Task CompatibleArrayEntryViewsShareElementBits(int width)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var signedType = factory.GetOrCreateIntegerType(width, true);
        var unsignedType = factory.GetOrCreateIntegerType(width, false);
        var signed = factory.CreateVariable("signed", factory.GetOrCreateSequenceType(signedType));
        var unsigned = factory.CreateVariable("unsigned", factory.GetOrCreateSequenceType(unsignedType));
        var x = factory.Variable(signed);
        var y = factory.Variable(unsigned);
        var bits = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        var query = new VerificationQuery(factory,
            [Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.Length(x), factory.Integer(1))),
             Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.Length(y), factory.Integer(1))),
             Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.Cast(factory.ObjectType, x), factory.Cast(factory.ObjectType, y))),
             Assume(factory, factory.Binary(IrBinaryOperator.Equal, factory.SequenceAccess(x, factory.Integer(0)), factory.IntegerBits(signedType, bits)))],
            new Goal(factory, factory.Binary(IrBinaryOperator.Equal, factory.SequenceAccess(y, factory.Integer(0)), factory.IntegerBits(unsignedType, bits)),
                ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [signed, unsigned]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>(), outcome.ToString());
    }

    private static Assumption Assume(IrFactory factory, IrTerm predicate)
    {
        return new(factory, predicate, new LoweredJustification(factory.CreateOperation()));
    }
}
