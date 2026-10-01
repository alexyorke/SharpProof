using System.Collections.Immutable;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class ReferenceCallableSolverTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ObjectWitnessesPreserveIdentity(bool same)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.ObjectType);
        var y = factory.CreateVariable("y", factory.ObjectType);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, x), NotNull(factory, y),
            Assume(factory, factory.Binary(same ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                factory.Variable(x), factory.Variable(y)))], factory.Boolean(false), [x, y]);
        var result = await session.CheckAsync(query, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var model = result.Model!.Assignments;
        Assert.That(ReferenceEquals(model[x].Reference, model[y].Reference), Is.EqualTo(same));
        AssertAssumptions(factory, query, model);
        Assert.That(await new ProofKernel(session).VerifyAsync(query), Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ArrayWitnessesPreserveIdentityAndLength(bool same)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, x), NotNull(factory, y),
            Assume(factory, factory.Binary(same ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                factory.Variable(x), factory.Variable(y))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(x)), factory.Integer(3))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(y)), factory.Integer(3)))],
            factory.Boolean(false), [x, y]);
        var result = await session.CheckAsync(query, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var model = result.Model!.Assignments;
        Assert.That(ReferenceEquals(model[x], model[y]), Is.EqualTo(same));
        Assert.That(model[x].Elements.Length, Is.EqualTo(3));
        Assert.That(model[y].Elements.Length, Is.EqualTo(3));
        AssertAssumptions(factory, query, model);
        Assert.That(await new ProofKernel(session).VerifyAsync(query), Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(true, 0)]
    [TestCase(false, 3)]
    public async Task StringWitnessesPreserveNullAndCodeUnitLength(bool isNull, int length)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.CreateVariable("text", factory.StringType);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [
            Assume(factory, factory.Binary(isNull ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                factory.Variable(text), factory.Null(factory.StringType))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(text)), factory.Integer(length)))],
            factory.Boolean(false), [text]);
        var result = await session.CheckAsync(query, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var value = result.Model!.Assignments[text];
        Assert.That(value.Kind, Is.EqualTo(isNull ? IrValueKind.Null : IrValueKind.String));
        if (!isNull)
        { Assert.That(value.String.Length, Is.EqualTo(length)); }
        AssertAssumptions(factory, query, result.Model.Assignments);
        Assert.That(await new ProofKernel(session).VerifyAsync(query), Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task FullLengthProofDomainIsPreservedWhileOversizedWitnessesAbstain()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.CreateVariable("text", factory.StringType);
        var length = factory.Length(factory.Variable(text));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var nonnegative = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, length, factory.Integer(0));
        var proof = await new ProofKernel(session).VerifyAsync(Query(factory, [], nonnegative, [text]));
        Assert.That(proof, Is.TypeOf<ProvenOutcome>());
        var maximum = Equal(factory, length, factory.Integer(int.MaxValue));
        var assumptions = ImmutableArray.Create(NotNull(factory, text), Assume(factory, maximum));
        var maximumProof = await new ProofKernel(session).VerifyAsync(Query(factory, assumptions, maximum, [text]));
        Assert.That(maximumProof, Is.TypeOf<ProvenOutcome>());
        var witness = await session.CheckAsync(Query(factory, assumptions, factory.Boolean(false), [text]), CancellationToken.None);
        Assert.That(witness.Status, Is.EqualTo(BackendCheckStatus.Unknown));
        Assert.That(witness.FailureReason, Is.EqualTo(BackendFailureReason.ResourceLimit));
    }

    [TestCase(0, false, 1UL)]
    [TestCase(8, true, 255UL)]
    [TestCase(16, false, 65535UL)]
    [TestCase(32, true, 2147483648UL)]
    [TestCase(32, false, 4294967295UL)]
    [TestCase(64, true, 9223372036854775808UL)]
    [TestCase(64, false, ulong.MaxValue)]
    public async Task ArrayElementWitnessesPreserveScalarBitsAndAliases(int width, bool isSigned, ulong bits)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var element = width == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(width, isSigned);
        var type = factory.GetOrCreateSequenceType(element);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var read = factory.SequenceAccess(factory.Variable(x), factory.Variable(index));
        IrTerm expected = width == 0 ? factory.Boolean(true) : factory.IntegerBits(element, bits);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, x),
            Assume(factory, Equal(factory, factory.Variable(x), factory.Variable(y))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(x)), factory.Integer(3))),
            Assume(factory, Equal(factory, factory.Variable(index), factory.Integer(2))),
            Assume(factory, Equal(factory, read, expected))], factory.Boolean(false), [x, y, index]);
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var result = await session.CheckAsync(query, CancellationToken.None);
        var model = result.Model!.Assignments;
        Assert.That(ReferenceEquals(model[x], model[y]), Is.True);
        Assert.That(model[x].Elements[2].Kind == IrValueKind.Boolean ? (ulong)(model[x].Elements[2].Boolean ? 1 : 0) : model[x].Elements[2].IntegerBits,
            Is.EqualTo(bits));
        AssertAssumptions(factory, query, model);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(int.MaxValue)]
    public async Task TotalArrayReadOutsideItsRangeHasTheDefaultValue(int index)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var x = factory.CreateVariable("x", type);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var read = factory.SequenceAccess(factory.Variable(x), factory.Integer(index));
        var query = Query(factory, [NotNull(factory, x),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(x)), factory.Integer(3)))],
            Equal(factory, read, factory.Integer(0)), [x]);
        Assert.That(await new ProofKernel(session).VerifyAsync(query), Is.TypeOf<ProvenOutcome>());
        var nullQuery = Query(factory, [Assume(factory, Equal(factory, factory.Variable(x), factory.Null(type)))],
            Equal(factory, read, factory.Integer(0)), [x]);
        Assert.That(await new ProofKernel(session).VerifyAsync(nullQuery), Is.TypeOf<ProvenOutcome>());
    }

    private static Assumption NotNull(IrFactory factory, IrVarId variable)
    {
        return Assume(factory, factory.Binary(IrBinaryOperator.NotEqual,
            factory.Variable(variable), factory.Null(factory.GetVariableInfo(variable).Type)));
    }

    private static Assumption Assume(IrFactory factory, IrTerm predicate)
    {
        return new(factory, predicate, new LoweredJustification(factory.CreateOperation()));
    }

    private static IrTerm Equal(IrFactory factory, IrTerm left, IrTerm right)
    {
        return factory.Binary(IrBinaryOperator.Equal, left, right);
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<Assumption> assumptions,
        IrTerm goal, ImmutableArray<IrVarId> variables)
    {
        return new(factory, assumptions, new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), variables);
    }

    private static void AssertAssumptions(IrFactory factory, VerificationQuery query, IReadOnlyDictionary<IrVarId, IrValue> model)
    {
        var interpreter = new IrInterpreter(factory);
        foreach (var assumption in query.Assumptions)
        {
            var value = interpreter.Evaluate(assumption.Predicate, model);
            Assert.That(value.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(value.Value!.Boolean, Is.True);
        }
    }
}
