using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeCompilerEntryConstraintReplayTests
{
    private const string DisjointCallPreconditionSource = """
        using SharpProof.Attributes;
        public sealed class LeftInput { }
        public sealed class RightInput { }
        public static class Subject
        {
            public static int Target(LeftInput left, RightInput right, int value)
            {
                Contract.Ensures(Contract.Result<int>() == value);
                return Helper(value);
            }

            private static int Helper(int value)
            {
                Contract.Requires(value >= 0);
                return value;
            }
        }
        """;

    [Test]
    public void CallPreconditionReplayDeclinesWhenOwnedEntryConstraintsCannotBeChecked()
    {
        var candidate = CreatePreparedDisjointCallPreconditionCandidate();
        Assert.That(candidate.EntryConstraints, Is.Not.Empty);
        Assert.That(candidate.CallPreconditions, Is.Not.Empty);
        var plan = BuildPlan(candidate);

        var witness = plan.ReplayCallPrecondition(
            0,
            ImmutableDictionary<IrVarId, IrValue>.Empty,
            TestContext.CurrentContext.CancellationToken);

        Assert.That(witness, Is.Null);
    }

    private static PassiveCallableCandidate CreatePreparedDisjointCallPreconditionCandidate()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            DisjointCallPreconditionSource);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(static item =>
            item.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(preparation.Total, Is.Not.Null, preparation.FailureReason.ToString());
        Assert.That(preparation.Total!.CallPreconditions, Is.Not.Empty);
        Assert.That(preparation.Total.DisjointInputs, Is.Not.Empty);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        return candidate!;
    }

    private static PassiveCallableVcPlan BuildPlan(PassiveCallableCandidate candidate)
    {
        var success = PassiveCallableVcBuilder.TryBuild(candidate,
            out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(success, Is.True, failure.ToString());
        Assert.That(plan, Is.Not.Null);
        return plan!;
    }
}
