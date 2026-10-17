using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeFieldRefutationReplayTests
{
    [Test]
    public async Task FalseCurrentFieldPostconditionReplaysFromNativeEntryModel()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var boxType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "BoxRef");
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), boxType, "field:Value",
            factory.IntegerType, isStatic: false);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("owner:entry", boxType),
            factory.CreateVariable("owner:current", boxType), factory.CreateVariable("owner:old", boxType));
        var site = factory.CreateOperation("field-refutation-write");

        IrTerm Read(IrVarId owner)
        { if (owner.IsDefault) { throw new System.ArgumentOutOfRangeException(nameof(owner)); } return factory.PureOpaque(field, factory.Variable(owner)); }
        IrTerm Equal(IrTerm left, IrTerm right)
        { System.ArgumentNullException.ThrowIfNull(left, nameof(left)); System.ArgumentNullException.ThrowIfNull(right, nameof(right)); return factory.Binary(IrBinaryOperator.Equal, left, right); }

        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        builder.FieldStore(block, site, IrWriteRegion.Field, factory.Variable(parameter.Current), field, factory.Integer(1));
        var observed = factory.CreateVariable("observed", factory.IntegerType);
        builder.Assign(block, site, observed, Read(parameter.Current));
        builder.Return(block, site);
        var requires = new[]
        {
            new PassiveContractClause(factory.Binary(IrBinaryOperator.NotEqual,
                factory.Variable(parameter.Entry), factory.Null(boxType)), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Entry), factory.Integer(0)), factory.Boolean(true), site)
        };
        var ensures = new[]
        {
            new PassiveContractClause(Equal(Read(parameter.Current), factory.Integer(1)), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Old), factory.Integer(0)), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Current), factory.Integer(0)), factory.Boolean(true), site)
        };
        var candidate = new PassiveCallableCandidate("field-current-refutation", builder.Build(), [parameter], null,
            [.. requires], [.. ensures]);
        var owner = factory.CreateReferenceValue(boxType,
            new IrObjectState().WithField(field, factory.CreateIntegerValue(0)));
        var initialValues = new Dictionary<IrVarId, IrValue>
        {
            [parameter.Entry] = owner,
            [parameter.Current] = owner,
            [parameter.Old] = owner
        };

        var actual = new IrProgramInterpreter(factory).Execute(candidate.Program, initialValues);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(actual.GetCurrentValue(observed)?.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
            Assert.That(actual.ConsumedApproximation, Is.False);
            Assert.That(actual.Unsupported, Is.Null);
            Assert.That(actual.Exception, Is.Null);
        }

        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.None));
        using var solver = new PassiveCallableSolver(plan!);
        var feasibility = await solver.VerifyFeasibilityAsync();
        Assert.That(feasibility.Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var current = await solver.VerifyEnsuresAsync(0);
        var old = await solver.VerifyEnsuresAsync(1);
        var falseCurrent = await solver.VerifyEnsuresAsync(2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(current.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(current.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(old.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(old.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(falseCurrent.Outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(falseCurrent.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(falseCurrent.EntryModel.Keys, Is.EquivalentTo([parameter.Entry]));
        }

        var witness = falseCurrent.EntryModel[parameter.Entry];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(witness.Type, Is.EqualTo(boxType));
            Assert.That(witness.Kind, Is.EqualTo(IrValueKind.Reference));
            Assert.That(witness.Reference, Is.TypeOf<IrObjectState>());
        }
        var replayValues = new Dictionary<IrVarId, IrValue>
        {
            [parameter.Entry] = witness,
            [parameter.Current] = witness,
            [parameter.Old] = witness
        };
        var replay = new IrProgramInterpreter(factory).Execute(candidate.Program, replayValues);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.GetCurrentValue(observed)?.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.Unsupported, Is.Null);
            Assert.That(replay.Exception, Is.Null);
        }
    }
}
