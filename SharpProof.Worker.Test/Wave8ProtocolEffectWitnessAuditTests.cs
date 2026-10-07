using System.Text;
using NUnit.Framework;
using SharpProof.Worker.Protocol;
using LauncherProgram = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ProtocolEffectWitnessAuditTests
{
    private static IEnumerable<TestCaseData> WitnessCases()
    {
        foreach (var boundary in new[] { "validation", "launcher" })
        {
            yield return new TestCaseData(WorkerEffectContractKind.DoesNotThrow, WorkerEffectSet.Throws, true, boundary);
            yield return new TestCaseData(WorkerEffectContractKind.DoesNotThrow, WorkerEffectSet.Allocates, false, boundary);
            yield return new TestCaseData(WorkerEffectContractKind.DoesNotThrow, WorkerEffectSet.None, false, boundary);
            yield return new TestCaseData(WorkerEffectContractKind.ZeroAllocations, WorkerEffectSet.Allocates, true, boundary);
            yield return new TestCaseData(WorkerEffectContractKind.ZeroAllocations, WorkerEffectSet.Throws, false, boundary);
            yield return new TestCaseData(WorkerEffectContractKind.ZeroAllocations, WorkerEffectSet.None, false, boundary);
        }
    }

    [TestCaseSource(nameof(WitnessCases))]
    public async Task RefutationWitnessMustDescribeTheDeclaredContractViolation(
        WorkerEffectContractKind contract, WorkerEffectSet effects, bool expectedValid, string boundary)
    {
        var request = CreateRequest();
        var response = CreateResponse(request, contract, effects);
        var json = WorkerProtocolJson.SerializeResponse(response);
        await TestContext.Out.WriteLineAsync($"contract={contract} effects={effects} boundary={boundary} bytes={Encoding.UTF8.GetByteCount(json)} sha256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(json))}");
        if (boundary == "validation")
        {
            var decoded = WorkerProtocolJson.DeserializeResponse(json)!;
            var validation = WorkerProtocolJson.ValidateForRequest(decoded, response.RequestHash, response.InputHash,
                response.Manifest, request, response.Summary.Versions);
            Assert.That(validation.IsValid, Is.EqualTo(expectedValid));
        }
        else
        {
            using var directory = new TempDirectory("wave8-witness-");
            var path = Path.Combine(directory.FullName, "response.json");
            await File.WriteAllTextAsync(path, json);
            var exitCode = LauncherProgram.ValidateAndReport(path, request, response.InputHash,
                response.Manifest, response.Summary.Versions, out var accepted, out var validated);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(expectedValid ? 5 : 3));
                Assert.That(accepted, Is.EqualTo(expectedValid));
                Assert.That(validated != null, Is.EqualTo(expectedValid));
            }
        }
    }

    [TestCase(WorkerEffectContractKind.DoesNotThrow)]
    [TestCase(WorkerEffectContractKind.ZeroAllocations)]
    public async Task ActualNativeRefutationRejectsReplacementWithUnrelatedEffect(WorkerEffectContractKind contract)
    {
        var source = contract == WorkerEffectContractKind.DoesNotThrow
            ? "using SharpProof.Attributes; public static class Subject { [DoesNotThrow] public static void Target() { throw new System.InvalidOperationException(); } }"
            : "using SharpProof.Attributes; public static class Subject { [ZeroAllocations] public static object Target() { return new object(); } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(WorkerProtocolJson.ValidateForRequest(response, response.RequestHash, response.InputHash,
            response.Manifest, project.Request, response.Summary.Versions).IsValid, Is.True);
        Assert.That(claim.EffectWitness, Is.Not.Null);
        Assert.That(claim.EffectWitness!.Effects, Is.EqualTo(contract == WorkerEffectContractKind.DoesNotThrow
            ? WorkerEffectSet.Throws : WorkerEffectSet.Allocates));
        claim.EffectWitness = CreateWitness(contract == WorkerEffectContractKind.DoesNotThrow
            ? WorkerEffectSet.Allocates : WorkerEffectSet.Throws, claim.EffectWitness.Location);
        var json = WorkerProtocolJson.SerializeResponse(response);
        using var directory = new TempDirectory("wave8-native-witness-");
        var path = Path.Combine(directory.FullName, "response.json");
        await File.WriteAllTextAsync(path, json);
        var exitCode = LauncherProgram.ValidateAndReport(path, project.Request, response.InputHash,
            response.Manifest, response.Summary.Versions, out var accepted, out var validated);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(3));
            Assert.That(accepted, Is.False);
            Assert.That(validated, Is.Null);
        }
    }

    private static WorkerVerifyRequest CreateRequest()
    {
        return new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference { Path = "compiler.manifest.json", Sha256 = WorkerProtocolVersions.EmptySha256 },
            Cache = new WorkerCacheOptions { Enabled = false }
        };
    }

    private static WorkerVerifyResponse CreateResponse(WorkerVerifyRequest request, WorkerEffectContractKind contract, WorkerEffectSet effects)
    {
        var location = new WorkerSourceLocation { Path = "Subject.cs", Start = 1, Length = 1, Line = 1, Column = 1 };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new WorkerCallableManifestEntry
            {
                CallableId = "M:Subject.Target", SelectedFeatures = [WorkerSelectedFeature.Effects],
                SelectionReasons = [WorkerSelectionReason.ExplicitAnnotation], Location = location, ClaimIds = ["effect:0"]
            }],
            Claims = [new WorkerClaimManifestEntry
            {
                ClaimId = "effect:0", CallableId = "M:Subject.Target", Ordinal = 0,
                Kind = WorkerClaimKind.Effect, Evidence = WorkerClaimEvidence.Attribute,
                EffectContractKind = contract, Location = location
            }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        return WorkerResultAssembler.Create(WorkerProtocolVersions.EmptySha256, manifest,
            WorkerRunStatus.Complete, WorkerRunFailureReason.None,
            [new WorkerCallableResult { CallableId = "M:Subject.Target", Coverage = WorkerCallableCoverage.Complete, Reason = WorkerCallableCoverageReason.None }],
            [new WorkerClaimResult
            {
                ClaimId = "effect:0", Outcome = WorkerClaimOutcome.Refuted, Reason = WorkerClaimReason.None,
                EffectCertainty = WorkerEffectEvidenceCertainty.DefiniteViolation, EffectWitness = CreateWitness(effects, location)
            }], request.Budgets, WorkerCacheStatus.Disabled, 0,
            requestHash: WorkerProtocolJson.ComputeRequestHash(request), versions: new WorkerVersionSummary
            {
                WorkerVersion = "test-worker",
                ApiSpecVersion = "test-spec",
                WorkerBinarySha256 = WorkerProtocolVersions.EmptySha256,
                ApiSpecContentSha256 = WorkerProtocolVersions.EmptySha256
            });
    }

    private static WorkerEffectViolationWitness CreateWitness(WorkerEffectSet effects, WorkerSourceLocation location)
    {
        return new WorkerEffectViolationWitness
        {
            Kind = effects == WorkerEffectSet.Allocates ? "managed-allocation" : "implicit-throw",
            Detail = "controlled protocol witness",
            Effects = effects,
            Location = location
        };
    }
}
