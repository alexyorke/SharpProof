using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerClaimOrdinalInputCoverageTests
{
    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public void CompiledClaimRejectsAnOutOfRangeOrdinal(int ordinal)
    {
        var target = PrepareSourceControl();
        var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            CallableClaimResultAssembler.Unknown(target, ordinal, WorkerClaimReason.UnsupportedExpression)));

        Assert.That(exception!.ParamName, Is.EqualTo("contractOrdinal"));
    }

    [Test]
    public void ValidOrdinalPreservesTheCompiledClaimIdentity()
    {
        var target = PrepareSourceControl();
        var result = CallableClaimResultAssembler.Unknown(target, 0, WorkerClaimReason.UnsupportedExpression);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ClaimId, Is.EqualTo(target.Entry.ClaimIds.Single()));
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        }
    }

    private static CompilerCallablePreparation PrepareSourceControl()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var target = preparations.Single();
        Assert.That(target.Entry.ClaimIds, Has.Length.EqualTo(1));
        return target;
    }
}
