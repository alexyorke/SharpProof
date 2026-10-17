using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeSequenceIndexAdmissionTests
{
    [TestCase(32)]
    [TestCase(64)]
    public async Task LegalGuardedStoreIndexesKeepTheNativePositionBoundary(int width)
    {
        await CheckGuardedStore(width, hasSign: true, falsePostcondition: false);
    }

    [TestCase(8, true, false)]
    [TestCase(8, true, true)]
    [TestCase(16, true, false)]
    [TestCase(16, true, true)]
    [TestCase(32, true, true)]
    [TestCase(64, true, true)]
    [TestCase(8, false, false)]
    [TestCase(8, false, true)]
    [TestCase(16, false, false)]
    [TestCase(16, false, true)]
    [TestCase(32, false, false)]
    [TestCase(32, false, true)]
    [TestCase(64, false, false)]
    [TestCase(64, false, true)]
    public async Task AllLegalStoreWidthsPreserveAdmissionAndConcretePostconditionReplay(int width, bool hasSign, bool falsePostcondition)
    {
        await CheckGuardedStore(width, hasSign, falsePostcondition);
    }

    private static async Task CheckGuardedStore(int width, bool hasSign, bool falsePostcondition)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var indexType = factory.GetOrCreateIntegerType(width, hasSign);
        var admitted = width < 32 || width == 32 && hasSign;
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", sequenceType),
            factory.CreateVariable("current", sequenceType), factory.CreateVariable("old", sequenceType));
        var resultVariable = factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var site = factory.CreateOperation("guarded-sequence-store");
        builder.Assign(block, site, parameter.Current, factory.Variable(parameter.Entry));
        builder.ElementStore(block, site, factory.Variable(parameter.Current), factory.Integer(indexType, 0L), factory.Integer(1));
        builder.Return(block, site, factory.SequenceAccess(factory.Variable(parameter.Current), factory.Integer(0)));
        var entry = factory.Variable(parameter.Entry);
        // ElementStore requires its caller to establish nullness and range.
        var requires = factory.Binary(IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.NotEqual, entry, factory.Null(sequenceType)),
            factory.Binary(IrBinaryOperator.GreaterThan, factory.Length(entry), factory.Integer(0)));
        var ensures = factory.Binary(IrBinaryOperator.Equal, factory.Variable(resultVariable), factory.Integer(falsePostcondition ? 2 : 1));
        var candidate = new PassiveCallableCandidate("sequence-index-admission", builder.Build(), [parameter], resultVariable,
            [new(requires, factory.Boolean(true), site)], [new(ensures, factory.Boolean(true), site)]);
        foreach (var original in new[] { 0, 7 })
        {
            var replay = new IrProgramInterpreter(factory).Execute(candidate.Program,
                new Dictionary<IrVarId, IrValue>
                { [parameter.Entry] = factory.CreateSequenceValue(sequenceType, [factory.CreateIntegerValue(original)]) });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
                Assert.That(replay.ConsumedApproximation, Is.False);
            }
        }
        var accepted = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted, Is.EqualTo(admitted));
            Assert.That(failure, Is.EqualTo(admitted ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
            Assert.That(plan == null, Is.EqualTo(!admitted));
        }
        if (admitted)
        {
            using var solver = new PassiveCallableSolver(plan!);
            var proof = await solver.VerifyEnsuresAsync(0, cancellationToken: CancellationToken.None);
            Assert.That(proof.Outcome, falsePostcondition ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>(), proof.Reason.ToString());
            Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
            if (falsePostcondition)
            {
                Assert.That(proof.EntryModel.ContainsKey(parameter.Entry), Is.True);
                var replay = new IrProgramInterpreter(factory).Execute(candidate.Program, proof.EntryModel);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
                Assert.That(replay.ConsumedApproximation, Is.False);
            }
        }
        var projection = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate,
            WorkerEffectCapabilitySet.None, new WorkerBudgets(), cancellationToken: CancellationToken.None);
        Assert.That(projection.Outcome, admitted ? Is.TypeOf<ProvenOutcome>() : Is.Null);
        Assert.That(projection.Reason, Is.EqualTo(admitted ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
    }
}
