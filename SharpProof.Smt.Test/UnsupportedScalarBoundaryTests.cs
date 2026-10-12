namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class UnsupportedScalarBoundaryTests
{
    [Test]
    public async Task ReferenceArrayElementDoesNotEnterTheScalarArrayModel()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var arrayType = factory.GetOrCreateSequenceType(factory.ObjectType);
        var array = factory.CreateVariable("objects", arrayType);
        var element = factory.SequenceAccess(factory.Variable(array), factory.Integer(0));
        var predicate = factory.Binary(IrBinaryOperator.Equal, element, factory.Null(factory.ObjectType));
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());

        var result = await backend.CheckAsync(Query(factory, predicate), CancellationToken.None);

        Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
    }

    [Test]
    public async Task ObjectUnboxingDoesNotAcquireAnUnjustifiedIntegerValue()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var boxed = factory.CreateVariable("boxed", factory.ObjectType);
        var value = factory.Cast(factory.IntegerType, factory.Variable(boxed));
        var predicate = factory.Binary(IrBinaryOperator.Equal, value, factory.Integer(0));
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());

        var result = await backend.CheckAsync(Query(factory, predicate), CancellationToken.None);

        Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
    }

    [TestCase(8)]
    [TestCase(16)]
    public async Task UnpromotedNarrowShiftDoesNotUseClrCountMasking(int width)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, true);
        var value = factory.CreateVariable("value", type);
        var count = factory.CreateVariable("count", type);
        var shift = factory.Binary(IrBinaryOperator.ShiftLeft, factory.Variable(value), factory.Variable(count));
        var predicate = factory.Binary(IrBinaryOperator.Equal, shift, factory.Integer(type, 0));
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());

        var result = await backend.CheckAsync(Query(factory, predicate), CancellationToken.None);

        Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.UnsupportedEncoding));
    }

    [Test]
    public async Task TotalZeroDividendRemainderIsZeroIncludingZeroDivisor()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var remainder = factory.Binary(IrBinaryOperator.Remainder, factory.Integer(0), factory.Variable(divisor));
        var predicate = factory.Binary(IrBinaryOperator.Equal, remainder, factory.Integer(0));
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());

        var outcome = await new ProofKernel(backend).VerifyAsync(Query(factory, predicate));

        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
    }

    private static VerificationQuery Query(IrFactory factory, IrTerm predicate)
    {
        return new(factory, [], new Goal(factory, predicate,
            ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
