using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerCallPreconditionInputCoverageTests
{
    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(int.MaxValue)]
    public void OwnedCallPreconditionQueriesRejectOutOfRangeOrdinals(int ordinal)
    {
        var candidate = EnrollSourceControl();
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: CancellationToken.None), Is.True,
            failure.ToString());
        var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan!.CallPreconditionQueries(ordinal)));

        Assert.That(exception!.ParamName, Is.EqualTo("ordinal"));

        // The ordinal is rejected before replay inputs are read. No model or
        // witness is constructed or accepted by this invalid-input call.
        var replayException = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan!.ReplayCallPrecondition(ordinal, null!, CancellationToken.None)));
        Assert.That(replayException!.ParamName, Is.EqualTo("ordinal"));
    }

    [Test]
    public void ValidOrdinalsPreserveTheActualOwnedCallQueries()
    {
        var candidate = EnrollSourceControl();
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: CancellationToken.None), Is.True,
            failure.ToString());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan!.CallPreconditionCount, Is.EqualTo(2));
            Assert.That(plan.CallPreconditionQueries(0), Has.Length.EqualTo(1));
            Assert.That(plan.CallPreconditionQueries(1), Has.Length.EqualTo(1));
            Assert.That(plan.CallPreconditionQueries(0).Single().Factory, Is.SameAs(candidate.Factory));
            Assert.That(plan.CallPreconditionQueries(1).Single().Factory, Is.SameAs(candidate.Factory));
        }
    }

    [Test]
    public void EveryOriginalCallMarkerRequiresItsOwnedObligationRow()
    {
        var original = EnrollSourceControl();
        var exception = Assert.Throws<ArgumentException>(new Action(() =>
            new PassiveCallableCandidate(original.CallableId, original.Program, original.Parameters, original.Result,
                original.Requires, original.Ensures,
                callPreconditions: original.CallPreconditions.RemoveAt(1))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("callPreconditions"));
            Assert.That(exception.Message, Does.Contain("Reserved call-precondition markers require owned obligation rows"));
        }
    }

    private static PassiveCallableCandidate EnrollSourceControl()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(Contract.Result<int>() == value);
                    var first = Helper(value);
                    return Helper(first);
                }
                private static int Helper(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: CancellationToken.None), out var preparations, cancellationToken: CancellationToken.None);
        var target = preparations.Single(preparation => preparation.Total is { CallPreconditions.Length: 2 });
        var candidate = PassiveCallableArtifactAdapter.Enroll(target);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.Parameters, Has.Length.EqualTo(1));
            Assert.That(candidate.Requires, Has.Length.EqualTo(1));
            Assert.That(candidate.Ensures, Has.Length.EqualTo(1));
            Assert.That(candidate.CallPreconditions, Has.Length.EqualTo(2));
        }
        return candidate!;
    }
}
