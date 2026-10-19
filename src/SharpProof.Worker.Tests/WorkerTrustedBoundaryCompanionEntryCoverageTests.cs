using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task DeclaredBoundaryRequiresItsActualCompanionEntry(bool contradictory)
    {
        var secondRequires = contradictory ? "value < 0" : "value <= 2";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofTrusted("Entry qualification declaration control.")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static extern int Target(int value);
            }
            [ContractFor(typeof(Subject))]
            public static class Specification {
                public static int Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Requires({{secondRequires}});
                    return value;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.EffectClaims.Length != 0));
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.TotalEntry!.Clauses, Has.Length.EqualTo(2));
            Assert.That(preparation.Entry.Assumptions.Count(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Kind == WorkerAssumptionKind.Precondition)),
                Is.EqualTo(2));
            Assert.That(preparation.EffectClaims.Single().Certainty,
                Is.EqualTo(WorkerEffectEvidenceCertainty.TrustedCompleteBoundary));
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Proven, WorkerClaimReason.None,
            contradictory ? WorkerVacuityKind.ContradictoryPreconditions : WorkerVacuityKind.None);
        var usedPreconditions = result.Assumptions.Where(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Kind == WorkerAssumptionKind.Precondition)).ToImmutableArray();
        var trust = result.Assumptions.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Kind == WorkerAssumptionKind.TrustedBoundary));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.EffectCertainty, Is.EqualTo(contradictory
                ? WorkerEffectEvidenceCertainty.VacuousEntry
                : WorkerEffectEvidenceCertainty.TrustedCompleteBoundary));
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(trust.Used, Is.EqualTo(!contradictory));
            if (contradictory)
            {
                Assert.That(usedPreconditions, Has.Length.EqualTo(2));
                Assert.That(usedPreconditions.All(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Used)), Is.True);
            }
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
