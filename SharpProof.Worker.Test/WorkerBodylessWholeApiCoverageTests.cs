using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task BodylessInstanceBoundaryKeepsEntryAndNativeBodyAdmissionOwnership()
    {
        const string source = """
            using SharpProof.Attributes;
            public abstract class Subject {
                [SharpProofTrusted("Declared bodyless instance boundary with typed positive entry.")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                [AllowedCapabilities(SharpProofCapability.None)]
                [ZeroAllocations]
                [EnforcePure]
                public abstract void Target([Positive] int value);
            }
            """;
        var compilation = TestCompilation.Create("BodylessInstanceEntry", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var symbol = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").Single();
        Assert.That(symbol.IsStatic, Is.False);
        Assert.That(symbol.IsAbstract, Is.True);
        Assert.That(symbol.ContainingType.IsReferenceType, Is.True);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single(item => item.EffectClaims.Length != 0);
        Assert.That(preparation.Total, Is.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        var entry = preparation.TotalEntry!;
        Assert.That(preparation.EffectClaims, Has.Length.EqualTo(4));
        Assert.That(preparation.EffectClaims.Select(claim => claim.ContractKind), Is.EquivalentTo(new[] {
            WorkerEffectContractKind.EffectContract, WorkerEffectContractKind.AllowedCapabilities,
            WorkerEffectContractKind.ZeroAllocations, WorkerEffectContractKind.EnforcePure }));
        Assert.That(preparation.Entry.ClaimIds, Is.EquivalentTo(preparation.EffectClaims.Select(claim => claim.ClaimId)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.HasReceiver, Is.True);
            Assert.That(entry.CallableId, Is.EqualTo(preparation.Entry.CallableId));
            Assert.That(entry.Parameters, Has.Length.EqualTo(2));
            Assert.That(entry.Factory.GetTypeInfo(entry.Factory.GetVariableInfo(entry.Parameters[^1].Entry).Type).Kind,
                Is.EqualTo(IrTypeKind.Reference));
            Assert.That(entry.Clauses, Has.Length.EqualTo(1));
            Assert.That(entry.Clauses.Single().Kind, Is.EqualTo(CompilerContractKind.Requires));
            Assert.That(preparation.Entry.Assumptions.Count(item => item.Kind == WorkerAssumptionKind.Precondition), Is.EqualTo(1));
            Assert.That(preparation.EffectClaims.All(evidence => evidence.Outcome == WorkerClaimOutcome.Proven &&
                evidence.Reason == WorkerClaimReason.None &&
                evidence.Certainty == WorkerEffectEvidenceCertainty.TrustedCompleteBoundary), Is.True);
        }
        var nativeEntry = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), default);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nativeEntry.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
            Assert.That(nativeEntry.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(nativeEntry.ProofCore, Is.Empty);
            Assert.That(nativeEntry.UsedAssumptionIds, Is.Empty);
        }

        var nativeCapability = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets());
        var nativeSummary = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets());
        var nativeAllocation = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var nativePurity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(nativeCapability.Outcome, Is.Null);
            Assert.That(nativeCapability.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
            Assert.That(nativeSummary.Outcome, Is.Null);
            Assert.That(nativeSummary.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
            Assert.That(nativeAllocation.Outcome, Is.Null);
            Assert.That(nativeAllocation.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(nativePurity.Outcome, Is.Null);
            Assert.That(nativePurity.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(new[] { nativeCapability, nativeSummary, nativeAllocation, nativePurity }
                .All(check => check.Core.IsEmpty && check.LockWitness == null && check.AllocationWitness == null && check.WriteWitness == null), Is.True);
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(4));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.ClaimResults.All(result => result.Outcome == WorkerClaimOutcome.Proven &&
                result.Reason == WorkerClaimReason.None && result.Vacuity == WorkerVacuityKind.None &&
                result.EffectCertainty == WorkerEffectEvidenceCertainty.TrustedCompleteBoundary &&
                result.ProofCore.Length != 0 && result.EffectWitness == null), Is.True);
            Assert.That(response.ClaimResults.Select(result => result.ClaimId).Distinct().Count(), Is.EqualTo(4));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
        foreach (var result in response.ClaimResults)
        {
            Assert.That(result.Assumptions.Single(item => item.Kind == WorkerAssumptionKind.TrustedBoundary).Used, Is.True);
            Assert.That(result.Assumptions.Count(item => item.Kind == WorkerAssumptionKind.Precondition), Is.EqualTo(1));
            Assert.That(response.CallableResults.Single(item => item.CallableId == GetCallableId(response, result)).Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
        }
    }
}
