using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    private static readonly ImmutableArray<string> EntryLockContradictoryCore = ["requires:0", "requires:1"];

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task NativeEffectEntryPrecedesActualForbiddenLockWithSatisfiableControls(
        bool effectSummary, bool contradictory)
    {
        var annotation = effectSummary
            ? "[EffectContract(SharpProofEffect.Allocates, Complete = true)]"
            : "[AllowedCapabilities(SharpProofCapability.None)]";
        var secondRequires = contradictory ? "value < 0" : "value <= 2";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                {{annotation}}
                public static void Target(int value) {
                    Contract.Requires(value > 0);
                    Contract.Requires({{secondRequires}});
                    lock (new object()) { }
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        var claim = preparation.EffectClaims.Single();
        var instructions = total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToArray();
        var locks = instructions.OfType<IrLockInstruction>().ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.ContractKind, Is.EqualTo(effectSummary
                ? WorkerEffectContractKind.EffectContract : WorkerEffectContractKind.AllowedCapabilities));
            Assert.That(total.ValidEffectClaimIds, Does.Contain(claim.ClaimId));
            Assert.That(claim.Constraint.AllowedCapabilities, Is.EqualTo(WorkerEffectCapabilitySet.None));
            Assert.That(claim.Constraint.AllowedEffects, Is.EqualTo(effectSummary ? WorkerEffectSet.Allocates : WorkerEffectSet.None));
            Assert.That(total.Clauses.Count(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Requires)), Is.EqualTo(2));
            Assert.That(preparation.Entry.Assumptions.Count(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Kind == WorkerAssumptionKind.Precondition)), Is.EqualTo(2));
            Assert.That(total.IsBodyAbstraction, Is.False);
            Assert.That(total.EffectsCompleteAtEntry, Is.True);
            Assert.That(locks, Is.Not.Empty);
            Assert.That(locks.All(site => site is null ? throw new System.ArgumentNullException(nameof(site)) : (total.Program.Factory.GetOperationInfo(site.Operation).SourceSpan is { Line: > 0 })), Is.True);
            Assert.That(instructions.OfType<IrAllocationInstruction>(), Is.Not.Empty);
            Assert.That(instructions.OfType<IrHavocInstruction>().Any(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Origin == IrHavocOrigin.Approximation)), Is.False);
        }
        var native = effectSummary
            ? await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken)
            : await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.None));
        if (contradictory)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>());
                Assert.That(native.Core, Is.EquivalentTo(EntryLockContradictoryCore));
                Assert.That(native.LockWitness, Is.Null);
            }
        }
        else
        {
            Assert.That(native.Outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(native.LockWitness, Is.Not.Null);
            Assert.That(locks.Any(site => site is null ? throw new System.ArgumentNullException(nameof(site)) : (site.Operation == native.LockWitness)), Is.True);
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = AssertClaimVerdict(response,
            contradictory ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted,
            WorkerClaimReason.None, contradictory ? WorkerVacuityKind.ContradictoryPreconditions : WorkerVacuityKind.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.EffectCertainty, Is.EqualTo(contradictory
                ? WorkerEffectEvidenceCertainty.VacuousEntry : WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
        if (contradictory)
        {
            var requires = result.Assumptions.Where(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Kind == WorkerAssumptionKind.Precondition)).ToArray();
            Assert.That(requires, Has.Length.EqualTo(2));
            Assert.That(requires.All(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Used)), Is.True);
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(result.EffectWitness, Is.Null);
        }
        else
        {
            var span = total.Program.Factory.GetOperationInfo(native.LockWitness!.Value).SourceSpan!;
            Assert.That(result.EffectWitness, Is.Not.Null);
            var witness = result.EffectWitness!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(witness.Kind, Is.EqualTo("synchronization-lock"));
                Assert.That(witness.Effects, Is.EqualTo(WorkerEffectSet.Synchronizes));
                Assert.That(witness.Capabilities, Is.EqualTo(WorkerEffectCapabilitySet.Synchronization));
                Assert.That(witness.Location.Start, Is.EqualTo(span.Start));
                Assert.That(witness.Location.Length, Is.EqualTo(span.Length));
                Assert.That(witness.Location.Line, Is.EqualTo(span.Line));
                Assert.That(witness.Location.Column, Is.EqualTo(span.Column));
            }
        }
    }
}
