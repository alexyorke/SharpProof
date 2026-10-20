using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [Test]
    public async Task ActualScalarArrayStoreReplaysAndProjectsItsAmbientWrite()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                [EnforcePure] public static void Target(int[] values) {
                    Contract.Requires(values != null && values.Length > 0);
                    values[0] = 1;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        Assert.That(preparation.TotalEntry!.Clauses, Has.Length.EqualTo(1));
        Assert.That(preparation.Entry.Assumptions.Count(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Kind == WorkerAssumptionKind.Precondition)),
            Is.EqualTo(1));
        var total = preparation.Total!;
        var instructions = total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToImmutableArray();
        Assert.That(total.IsBodyAbstraction, Is.False);
        Assert.That(total.EffectsCompleteAtEntry, Is.True);
        Assert.That(instructions.OfType<IrHavocInstruction>().Any(instruction =>
            instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Origin == IrHavocOrigin.Approximation)), Is.False);
        var writes = instructions.OfType<IrWriteInstruction>().ToImmutableArray();
        Assert.That(writes, Has.Length.EqualTo(1));
        var write = writes[0];
        Assert.That(write.Region, Is.EqualTo(IrWriteRegion.Element));
        Assert.That(write.IsElementStore, Is.True);
        var info = total.Program.Factory.GetOperationInfo(write.Operation);
        Assert.That(info.SourceSpan, Is.Not.Null);
        Assert.That(info.Description, Is.Not.Null);
        var span = info.SourceSpan!;
        Assert.That(span.Line, Is.GreaterThan(0));
        var detail = total.Program.Factory.GetString(info.Description!.Value);

        var native = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(native.Outcome, Is.TypeOf<RefutedOutcome>(), native.Reason.ToString());
            Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(native.HasFeasibleEntryWitness, Is.True);
            Assert.That(native.WriteWitness, Is.EqualTo(write.Operation));
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Refuted, WorkerClaimReason.None,
            WorkerVacuityKind.None);
        Assert.That(result.EffectWitness, Is.Not.Null);
        var witness = result.EffectWitness!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(witness.Kind, Is.EqualTo("nonlocal-write"));
            Assert.That(witness.Detail, Is.EqualTo(detail));
            Assert.That(witness.Effects, Is.EqualTo(WorkerEffectSet.WritesAmbientState));
            Assert.That(witness.Capabilities, Is.EqualTo(WorkerEffectCapabilitySet.None));
            Assert.That(witness.ExactExceptionTypeHierarchy, Is.Empty);
            Assert.That(witness.Location.Start, Is.EqualTo(span.Start));
            Assert.That(witness.Location.Length, Is.EqualTo(span.Length));
            Assert.That(witness.Location.Line, Is.EqualTo(span.Line));
            Assert.That(witness.Location.Column, Is.EqualTo(span.Column));
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
