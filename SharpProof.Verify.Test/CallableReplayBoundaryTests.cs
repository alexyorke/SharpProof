using System.Collections.Immutable;
using System.Numerics;

namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class CallableReplayBoundaryTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task OpaqueWriterRequiresConcreteHeapEvidenceForContractReads(
        bool readInGuard, bool restoreCell)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var owner = factory.CreateVariable("owner", type);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "Writer", factory.IntegerType, true, type);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Call(block, factory.CreateOperation(), null, member, null, factory.Variable(owner));
        if (restoreCell)
        {
            builder.ElementStore(block, factory.CreateOperation(), factory.Variable(owner),
                factory.Integer(0), factory.Integer(7));
        }
        builder.Return(block, factory.CreateOperation());
        var heapRead = factory.Binary(IrBinaryOperator.Equal,
            factory.SequenceAccess(factory.Variable(owner), factory.Integer(0)), factory.Integer(9));
        var context = new CallableReplayContext(builder.Build(), false,
            ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(owner, owner),
            ImmutableDictionary<IrVarId, IrVarId?>.Empty, [],
            readInGuard ? factory.Boolean(false) : heapRead,
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, 100, [],
            postconditionGuard: readInGuard ? heapRead : factory.Boolean(true), replayOptions: null);
        var outcome = await Replay(factory,
            [KeyValuePair.Create(owner, factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)]))],
            context, owner);

        if (restoreCell)
        {
            Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        }
        else
        {
            Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(((UnknownOutcome)outcome).Reason,
                Is.EqualTo(AbstentionReason.CounterexampleNotReplayable));
        }
    }

    [Test]
    public async Task NontrivialReplayRequiresAProgramAndEveryEntryBinding()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var entry = factory.CreateVariable("entry", factory.IntegerType);
        var body = factory.CreateVariable("body", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Return(block, factory.CreateOperation());
        var program = builder.Build();
        var bindings = ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(body, entry);

        var noProgram = await Replay(factory, [], Context(factory, null, false));
        var missingEntry = await Replay(factory, [], Context(factory, program, false,
            parameterBindings: bindings));
        var valid = await Replay(factory,
            [KeyValuePair.Create(entry, factory.CreateIntegerValue(1))],
            Context(factory, program, false, parameterBindings: bindings), entry);

        AssertReplayFailed(noProgram);
        AssertReplayFailed(missingEntry);
        Assert.That(valid, Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task TrivialContextCannotClaimAnUnreplayedProgram()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Return(block, factory.CreateOperation());

        AssertReplayFailed(await Replay(factory, [], Context(factory, builder.Build(), true)));
        Assert.That(await Replay(factory, [], Context(factory, null, true)),
            Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task ForeignPrestateIdentifierCannotValidateARefutation()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var entry = factory.CreateVariable("entry", factory.IntegerType);
        var old = factory.CreateVariable("old", factory.IntegerType);
        var foreignFactory = new IrFactory(IrExecutionSemantics.Total);
        var foreign = foreignFactory.CreateVariable("foreign", foreignFactory.IntegerType);
        var model = new[] { KeyValuePair.Create(entry, factory.CreateIntegerValue(1)) };
        var foreignContext = Context(factory, null, true,
            preStateBindings: ImmutableDictionary<IrVarId, IrVarId?>.Empty.Add(foreign, entry));
        var localContext = Context(factory, null, true,
            preStateBindings: ImmutableDictionary<IrVarId, IrVarId?>.Empty.Add(old, entry));

        AssertReplayFailed(await Replay(factory, model, foreignContext, entry));
        Assert.That(await Replay(factory, model, localContext, entry),
            Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task DeclaredCallableResultRequiresAReturnedValueOfItsType()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var noValue = new IrProgramBuilder(factory);
        var voidBlock = noValue.CreateBlock();
        noValue.Return(voidBlock, factory.CreateOperation());
        var withValue = new IrProgramBuilder(factory);
        var valueBlock = withValue.CreateBlock();
        withValue.Return(valueBlock, factory.CreateOperation(), factory.Integer(1));

        AssertReplayFailed(await Replay(factory, [], Context(factory, noValue.Build(), false,
            resultVariables: [result])));
        Assert.That(await Replay(factory, [], Context(factory, withValue.Build(), false,
            resultVariables: [result])), Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task CallablePostconditionMustConcretelyReplayAsFalse()
    {
        var factory = new IrFactory();
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var divisionByZero = factory.Binary(IrBinaryOperator.Equal,
            factory.Binary(IrBinaryOperator.Divide,
                factory.Integer(0), factory.Variable(divisor)), factory.Integer(0));
        var model = new[] { KeyValuePair.Create(divisor, factory.CreateIntegerValue(0)) };

        var falsePostcondition = await Replay(factory, model,
            Context(factory, null, true), divisor);
        var truePostcondition = await Replay(factory, model,
            Context(factory, null, true, postcondition: factory.Boolean(true)), divisor);
        var exceptionalPostcondition = await Replay(factory, model,
            Context(factory, null, true, postcondition: divisionByZero), divisor);

        Assert.That(falsePostcondition, Is.TypeOf<RefutedOutcome>());
        AssertReplayFailed(truePostcondition);
        Assert.That(exceptionalPostcondition, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)exceptionalPostcondition).Reason,
            Is.EqualTo(AbstentionReason.PostconditionMayBeUndefined));
    }

    private static CallableReplayContext Context(
        IrFactory factory, IrProgram? program, bool isTrivial,
        ImmutableDictionary<IrVarId, IrVarId>? parameterBindings = null,
        ImmutableDictionary<IrVarId, IrVarId?>? preStateBindings = null,
        ImmutableArray<IrVarId> resultVariables = default,
        IrTerm? postcondition = null)
    {
        return new CallableReplayContext(program, isTrivial,
            parameterBindings ?? ImmutableDictionary<IrVarId, IrVarId>.Empty,
            preStateBindings ?? ImmutableDictionary<IrVarId, IrVarId?>.Empty,
            resultVariables.IsDefault ? [] : resultVariables,
            postcondition ?? factory.Boolean(false),
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty,
            100, [], postconditionGuard: factory.Boolean(true), replayOptions: null);
    }

    private static async Task<ProofOutcome> Replay(IrFactory factory,
        IEnumerable<KeyValuePair<IrVarId, IrValue>> assignments,
        CallableReplayContext context, params IrVarId[] variables)
    {
        var query = new VerificationQuery(factory, [], new Goal(factory, factory.Boolean(false),
            ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [.. variables]);
        return await new ProofKernel(new ModelBackend(new BackendModel(assignments)))
            .VerifyCallableAsync(query, context, TestContext.CurrentContext.CancellationToken);
    }

    private static void AssertReplayFailed(ProofOutcome outcome)
    {
        Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)outcome).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
    }

    private sealed class ModelBackend(BackendModel model) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackendCheckResult.Satisfiable(model));
        }
    }
}
