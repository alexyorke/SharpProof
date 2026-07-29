using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CallableCounterexampleReplayerTests
{
    [Test]
    public void ReplayFollowsExactBranchAndRebuildsContractState()
    {
        var fixture = CreateIncrementingBranch(static (factory, current, result, old) =>
            factory.Binary(IrBinaryOperator.OrElse,
                factory.Binary(IrBinaryOperator.NotEqual,
                    factory.Variable(result), factory.Variable(current)),
                factory.Binary(IrBinaryOperator.Equal,
                    factory.Variable(old), factory.Variable(current))));

        Assert.That(CallableCounterexampleReplayer.Replay(
            fixture.Target, 0, fixture.Model), Is.EqualTo(WorkerClaimReason.None));
    }

    [Test]
    public void ReplayRejectsAConcreteExecutionThatSatisfiesThePostcondition()
    {
        var fixture = CreateIncrementingBranch(static (factory, current, result, old) =>
            factory.Binary(IrBinaryOperator.AndAlso,
                factory.Binary(IrBinaryOperator.Equal,
                    factory.Variable(result), factory.Variable(current)),
                factory.Binary(IrBinaryOperator.Equal,
                    factory.Variable(current),
                    factory.Binary(IrBinaryOperator.Add,
                        factory.Variable(old), factory.Integer(1)))));

        Assert.That(CallableCounterexampleReplayer.Replay(
            fixture.Target, 0, fixture.Model), Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [Test]
    public void ReplayFailsClosedWhenARequiredModelValueIsMissing()
    {
        var fixture = CreateIncrementingBranch(static (factory, _, _, _) =>
            factory.Boolean(false));

        Assert.That(CallableCounterexampleReplayer.Replay(
            fixture.Target, 0,
            ImmutableDictionary<IrVarId, IrValue>.Empty), Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [Test]
    public void TrivialNormalCompletionRequiresItsPostconditionToBeFalse()
    {
        Assert.That(CallableCounterexampleReplayer.Replay(
            CreateTrivial(postcondition: false), 0,
            ImmutableDictionary<IrVarId, IrValue>.Empty), Is.EqualTo(WorkerClaimReason.None));
        Assert.That(CallableCounterexampleReplayer.Replay(
            CreateTrivial(postcondition: true), 0,
            ImmutableDictionary<IrVarId, IrValue>.Empty), Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [Test]
    public void ReplayAllowsACallOutsideTheConcretePath()
    {
        var fixture = Create(static (factory, _, _, _) => factory.Boolean(false),
            static (factory, source) =>
            {
                var builder = new IrProgramBuilder(factory);
                var entry = builder.CreateBlock("entry");
                var returned = builder.CreateBlock("returned");
                var unreachableCall = builder.CreateBlock("unreachable-call");
                builder.Branch(entry, factory.CreateOperation(),
                    factory.Binary(IrBinaryOperator.GreaterThan,
                        factory.Variable(source), factory.Integer(0)),
                    returned, unreachableCall);
                builder.Return(returned, factory.CreateOperation(),
                    factory.Variable(source));
                var member = factory.GetOrCreateMember(
                    factory.CreateIdentity(), factory.ObjectType, "Call",
                    factory.IntegerType, true, factory.IntegerType);
                builder.Call(unreachableCall, factory.CreateOperation(), source,
                    member, null, factory.Variable(source));
                builder.Return(unreachableCall, factory.CreateOperation(),
                    factory.Variable(source));
                return builder.Build();
            });

        Assert.That(CallableCounterexampleReplayer.Replay(
            fixture.Target, 0, fixture.Model), Is.EqualTo(WorkerClaimReason.None));
    }

    [Test]
    public void ReplayRejectsAProgramAboveTheCompilerInstructionBound()
    {
        var fixture = Create(static (factory, _, _, _) => factory.Boolean(false),
            static (factory, source) =>
            {
                var builder = new IrProgramBuilder(factory);
                var entry = builder.CreateBlock("entry");
                for (var index = 0; index < CompilerPreparedBody.MaximumInstructions; index++)
                {
                    builder.Assign(entry, factory.CreateOperation(), source,
                        factory.Variable(source));
                }

                builder.Return(entry, factory.CreateOperation(),
                    factory.Variable(source));
                return builder.Build();
            });

        Assert.That(CallableCounterexampleReplayer.Replay(
            fixture.Target, 0, fixture.Model), Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [TestCase(ReplayObstacle.Call)]
    [TestCase(ReplayObstacle.Havoc)]
    [TestCase(ReplayObstacle.UnsupportedTerm)]
    [TestCase(ReplayObstacle.Exception)]
    public void ReplayFailsClosedAtNonConcreteExecution(ReplayObstacle obstacle)
    {
        var fixture = CreateObstacle(obstacle);

        Assert.That(CallableCounterexampleReplayer.Replay(
            fixture.Target, 0, fixture.Model), Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [Test]
    public void ReplayObservesPreCanceledExecution()
    {
        var target = CreateTrivial(postcondition: false);
        var cancellationToken = new CancellationToken(canceled: true);

        Assert.Throws<OperationCanceledException>(new Action(() =>
            _ = CallableCounterexampleReplayer.Replay(
                target,
                0,
                ImmutableDictionary<IrVarId, IrValue>.Empty,
                cancellationToken)));
    }

    [Test]
    public void ReplayFailsClosedForMalformedCanonicalResultIdentity()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(
            entry,
            factory.CreateOperation(),
            factory.Integer(0));
        var target = new CompilerCallablePreparation(
            factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "malformed-result",
                ClaimIds = ["claim"]
            },
            [new CompilerPreparedClause(
                CompilerContractKind.Ensures,
                factory.Boolean(false),
                CompilerContractEvidence.CompilerBoundInvocation,
                "claim",
                null)],
            [new CompilerCanonicalVariable(
                CompilerVariableRole.Result,
                -1,
                default,
                null,
                null,
                "result")],
            WorkerClaimReason.None,
            CompilerPreparedBody.ProgramBody(
                builder.Build(),
                ImmutableDictionary<IrVarId, IrVarId>.Empty,
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSpecCall>.Empty));

        Assert.That(
            CallableCounterexampleReplayer.Replay(
                target,
                0,
                ImmutableDictionary<IrVarId, IrValue>.Empty),
            Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    private static ReplayFixture CreateIncrementingBranch(
        Func<IrFactory, IrVarId, IrVarId, IrVarId, IrTerm> postcondition)
    {
        return Create(postcondition, static (factory, source) =>
        {
            var builder = new IrProgramBuilder(factory);
            var entry = builder.CreateBlock("entry");
            var increment = builder.CreateBlock("increment");
            var unchanged = builder.CreateBlock("unchanged");
            builder.Branch(entry, factory.CreateOperation(),
                factory.Binary(IrBinaryOperator.GreaterThan,
                    factory.Variable(source), factory.Integer(0)),
                increment, unchanged);
            builder.Assign(increment, factory.CreateOperation(), source,
                factory.Binary(IrBinaryOperator.Add,
                    factory.Variable(source), factory.Integer(1)));
            builder.Return(increment, factory.CreateOperation(),
                factory.Variable(source));
            builder.Return(unchanged, factory.CreateOperation(),
                factory.Variable(source));
            return builder.Build();
        });
    }

    private static ReplayFixture CreateObstacle(ReplayObstacle obstacle)
    {
        return Create(static (factory, _, _, _) => factory.Boolean(false),
            (factory, source) =>
            {
                var builder = new IrProgramBuilder(factory);
                var entry = builder.CreateBlock("entry");
                if (obstacle == ReplayObstacle.Havoc)
                {
                    builder.Havoc(entry, factory.CreateOperation(),
                                        IrHavocKind.Variables, source);
                }
                else if (obstacle == ReplayObstacle.Exception)
                {
                    builder.Assign(entry, factory.CreateOperation(), source,
                                        factory.Binary(IrBinaryOperator.Divide,
                                            factory.Variable(source), factory.Integer(0)));
                }
                else
                {
                    var member = factory.GetOrCreateMember(
                        factory.CreateIdentity(), factory.ObjectType, "Opaque",
                        factory.IntegerType, true, factory.IntegerType);
                    if (obstacle == ReplayObstacle.Call)
                    {
                        builder.Call(entry, factory.CreateOperation(), source,
                                                member, null, factory.Variable(source));
                    }
                    else
                    {
                        builder.Assign(entry, factory.CreateOperation(), source,
                                                factory.PureOpaque(member, null,
                                                    factory.Variable(source)));
                    }
                }
                builder.Return(entry, factory.CreateOperation(),
                    factory.Variable(source));
                return builder.Build();
            });
    }

    private static CompilerCallablePreparation CreateTrivial(bool postcondition)
    {
        var factory = new IrFactory();
        return new CompilerCallablePreparation(factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "trivial",
                ClaimIds = ["claim"]
            },
            [new CompilerPreparedClause(CompilerContractKind.Ensures,
                factory.Boolean(postcondition),
                CompilerContractEvidence.CompilerBoundInvocation, "claim", null)],
            [], WorkerClaimReason.None, CompilerPreparedBody.Trivial());
    }

    private static ReplayFixture Create(
        Func<IrFactory, IrVarId, IrVarId, IrVarId, IrTerm> postcondition,
        Func<IrFactory, IrVarId, IrProgram> program)
    {
        var factory = new IrFactory();
        var current = factory.CreateVariable("parameter:0", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var old = factory.CreateVariable("pre:0", factory.IntegerType);
        var source = factory.CreateVariable("body-parameter", factory.IntegerType);
        var target = new CompilerCallablePreparation(factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "callable",
                ClaimIds = ["claim"]
            },
            [new CompilerPreparedClause(CompilerContractKind.Ensures,
                postcondition(factory, current, result, old),
                CompilerContractEvidence.CompilerBoundInvocation, "claim", null)],
            [
                new CompilerCanonicalVariable(CompilerVariableRole.Parameter, 0,
                    current, null, null, "parameter:0"),
                new CompilerCanonicalVariable(CompilerVariableRole.Result, -1,
                    result, null, null, "result"),
                new CompilerCanonicalVariable(CompilerVariableRole.PreState, -1,
                    old, current, null, "pre:0")
            ],
            WorkerClaimReason.None,
            CompilerPreparedBody.ProgramBody(program(factory, source),
                ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(source, current),
                ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty));
        return new ReplayFixture(target,
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(
                current, factory.CreateIntegerValue(5)));
    }

    public enum ReplayObstacle
    {
        Call, Havoc, UnsupportedTerm, Exception
    }
    private sealed record ReplayFixture(CompilerCallablePreparation Target,
        ImmutableDictionary<IrVarId, IrValue> Model);
}
