using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275BulkWorkerApiTests
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
    public void PreparedPlanPropertiesAndCallQueriesStayBoundToOwnedCallPreconditions()
    {
        var candidate = CreatePreparedCallPreconditionCandidate();
        Assert.That(candidate.CallPreconditions, Is.Not.Empty);
        var plan = BuildPlan(candidate);
        var queries = plan.CallPreconditionQueries(0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Factory, Is.SameAs(candidate.Factory));
            Assert.That(plan.EnsuresCount, Is.EqualTo(candidate.Ensures.Length));
            Assert.That(plan.CallPreconditionCount,
                Is.EqualTo(candidate.CallPreconditions.Length));
            Assert.That(plan.CallableId, Is.EqualTo(candidate.CallableId));
            Assert.That(queries, Is.Not.Empty);
            Assert.That(queries.All(query =>
                query.Goal.Diagnostic == ProofDiagnosticKind.Precondition), Is.True);
        }
    }

    [Test]
    public void CallPreconditionQueriesRejectNegativeAndPastEndOrdinals()
    {
        var plan = BuildPlan(CreatePreparedCallPreconditionCandidate());

        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.CallPreconditionQueries(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.CallPreconditionQueries(plan.CallPreconditionCount)));
    }

    [Test]
    public void CallPreconditionReplayRejectsNegativeAndPastEndOrdinals()
    {
        var plan = BuildPlan(CreatePreparedCallPreconditionCandidate());
        var missingInputs = ImmutableDictionary<IrVarId, IrValue>.Empty;

        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.ReplayCallPrecondition(-1, missingInputs, CancellationToken.None)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.ReplayCallPrecondition(plan.CallPreconditionCount,
                missingInputs, CancellationToken.None)));
    }

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
            CancellationToken.None);

        Assert.That(witness, Is.Null);
    }

    private static PassiveCallableCandidate CreatePreparedCallPreconditionCandidate()
    {
        var source = GoldenTest.Load("worker", "reachable-call-precondition-artifact").Source;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single(static item =>
            item.Total is { CallPreconditions.Length: > 0 });
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        return candidate!;
    }

    private static PassiveCallableCandidate CreatePreparedDisjointCallPreconditionCandidate()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            DisjointCallPreconditionSource);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
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
            out var plan, out var failure);
        Assert.That(success, Is.True, failure.ToString());
        Assert.That(plan, Is.Not.Null);
        return plan!;
    }

}
