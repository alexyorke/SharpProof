using System.Text;
using NUnit.Framework;
using SharpProof.Worker.Protocol;
using LauncherProgram = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ProtocolEffectWitnessContractMatrixTests
{
    private static IEnumerable<TestCaseData> ContractCases()
    {
        (WorkerEffectContractKind Contract, WorkerEffectSet Effects, WorkerEffectCapabilitySet Capabilities, bool Valid)[] cases = [
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.ReadsReceiverState, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.ReadsArgumentState, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.WritesStaticState, WorkerEffectCapabilitySet.None, true),
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.ReadsStaticState, WorkerEffectCapabilitySet.None, true),
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.Synchronizes, WorkerEffectCapabilitySet.Synchronization, true),
            (WorkerEffectContractKind.EnforcePure, WorkerEffectSet.None, WorkerEffectCapabilitySet.IO, true),
            (WorkerEffectContractKind.AllowedExceptions, WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.AllowedExceptions, WorkerEffectSet.WritesStaticState, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.AllowedExceptions, WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None, true),
            (WorkerEffectContractKind.AllowedCapabilities, WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.AllowedCapabilities, WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.AllowedCapabilities, WorkerEffectSet.Synchronizes, WorkerEffectCapabilitySet.None, false),
            (WorkerEffectContractKind.AllowedCapabilities, WorkerEffectSet.Synchronizes, WorkerEffectCapabilitySet.Synchronization, true),
            (WorkerEffectContractKind.AllowedCapabilities, WorkerEffectSet.None, WorkerEffectCapabilitySet.IO, true),
            (WorkerEffectContractKind.EffectContract, WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None, true),
            (WorkerEffectContractKind.EffectContract, WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None, true),
            (WorkerEffectContractKind.EffectContract, WorkerEffectSet.WritesStaticState, WorkerEffectCapabilitySet.None, true),
            (WorkerEffectContractKind.EffectContract, WorkerEffectSet.None, WorkerEffectCapabilitySet.IO, true),
            (WorkerEffectContractKind.EffectContract, WorkerEffectSet.None, WorkerEffectCapabilitySet.None, false)
        ];
        foreach (var row in cases)
        {
            foreach (var boundary in new[] { "validation", "launcher" })
            {
                yield return new TestCaseData(row.Contract, row.Effects, row.Capabilities, row.Valid, boundary);
            }
        }
    }

    [TestCaseSource(nameof(ContractCases))]
    public async Task ContractSpecificWitnessConditionsAreRequiredBeforePublication(
        WorkerEffectContractKind contract, WorkerEffectSet effects, WorkerEffectCapabilitySet capabilities,
        bool expectedValid, string boundary)
    {
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference { Path = "compiler.manifest.json", Sha256 = WorkerProtocolVersions.EmptySha256 },
            Cache = new WorkerCacheOptions { Enabled = false }
        };
        var response = CreateResponse(request, contract, effects, capabilities);
        var json = WorkerProtocolJson.SerializeResponse(response);
        await TestContext.Out.WriteLineAsync($"contract={contract} effects={effects} capabilities={capabilities} boundary={boundary} bytes={Encoding.UTF8.GetByteCount(json)} sha256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(json))}");
        if (boundary == "validation")
        {
            var decoded = WorkerProtocolJson.DeserializeResponse(json)!;
            var validation = WorkerProtocolJson.ValidateForRequest(decoded, response.RequestHash, response.InputHash,
                response.Manifest, request, response.Summary.Versions);
            Assert.That(validation.IsValid, Is.EqualTo(expectedValid));
        }
        else
        {
            using var directory = new TempDirectory("wave8-witness-contract-");
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

    [TestCase("purity-write", WorkerClaimOutcome.Refuted)]
    [TestCase("exceptions-throw", WorkerClaimOutcome.Refuted)]
    [TestCase("capabilities-lock", WorkerClaimOutcome.Refuted)]
    [TestCase("effect-allocation", WorkerClaimOutcome.Refuted)]
    [TestCase("purity-allocation", WorkerClaimOutcome.Proven)]
    [TestCase("purity-ambient-read", WorkerClaimOutcome.Unknown)]
    [TestCase("capabilities-opaque-call", WorkerClaimOutcome.Unknown)]
    [TestCase("effect-argument-read", WorkerClaimOutcome.Unknown)]
    public async Task GenuineNativeRefutationsAndUnsupportedBoundariesArePreserved(string shape, WorkerClaimOutcome expectedOutcome)
    {
        var member = shape switch
        {
            "purity-write" => "[EnforcePure] public static void Target() { State = 1; } public static int State;",
            "exceptions-throw" => "[AllowedExceptions(typeof(System.ArgumentException))] public static void Target() { throw new System.InvalidOperationException(); }",
            "capabilities-lock" => "[AllowedCapabilities(SharpProofCapability.None)] public static void Target([NotNull] object gate) { lock (gate) { } }",
            "effect-allocation" => "[EffectContract(SharpProofEffect.None)] public static object Target() { return new object(); }",
            "purity-allocation" => "[EnforcePure] public static void Target() { new object(); }",
            "purity-ambient-read" => "[EnforcePure] public static int Target() { return State; } public static int State;",
            "capabilities-opaque-call" => "[AllowedCapabilities(SharpProofCapability.None)] public static void Target() { System.Console.WriteLine(1); }",
            "effect-argument-read" => "[EffectContract(SharpProofEffect.None)] public static int Target([NotNull] int[] values) { return values.Length; }",
            _ => throw new AssertionException("Unknown frozen native control.")
        };
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " + member + " }");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expectedOutcome), claim.Reason.ToString());
        Assert.That(claim.EffectWitness != null, Is.EqualTo(expectedOutcome == WorkerClaimOutcome.Refuted));
        Assert.That(WorkerProtocolJson.ValidateForRequest(response, response.RequestHash, response.InputHash,
            response.Manifest, project.Request, response.Summary.Versions).IsValid, Is.True);
        using var directory = new TempDirectory("wave8-native-contract-");
        var path = Path.Combine(directory.FullName, "response.json");
        await File.WriteAllTextAsync(path, WorkerProtocolJson.SerializeResponse(response));
        _ = LauncherProgram.ValidateAndReport(path, project.Request, response.InputHash,
            response.Manifest, response.Summary.Versions, out var accepted, out var validated);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted, Is.True);
            Assert.That(validated, Is.Not.Null);
        }
    }

    private static WorkerVerifyResponse CreateResponse(WorkerVerifyRequest request, WorkerEffectContractKind contract,
        WorkerEffectSet effects, WorkerEffectCapabilitySet capabilities)
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
        var witness = new WorkerEffectViolationWitness
        {
            Kind = (effects & WorkerEffectSet.Throws) != 0 ? "implicit-throw" : effects == WorkerEffectSet.Allocates
                ? "managed-allocation" : capabilities != WorkerEffectCapabilitySet.None ? "synchronization-lock" : "nonlocal-write",
            Detail = "controlled contract witness",
            Effects = effects,
            Capabilities = capabilities,
            Location = location
        };
        var versions = new WorkerVersionSummary
        {
            WorkerVersion = "test-worker",
            ApiSpecVersion = "test-spec",
            WorkerBinarySha256 = WorkerProtocolVersions.EmptySha256,
            ApiSpecContentSha256 = WorkerProtocolVersions.EmptySha256
        };
        return WorkerResultAssembler.Create(WorkerProtocolVersions.EmptySha256, manifest,
            WorkerRunStatus.Complete, WorkerRunFailureReason.None,
            [new WorkerCallableResult { CallableId = "M:Subject.Target", Coverage = WorkerCallableCoverage.Complete, Reason = WorkerCallableCoverageReason.None }],
            [new WorkerClaimResult
            {
                ClaimId = "effect:0", Outcome = WorkerClaimOutcome.Refuted, Reason = WorkerClaimReason.None,
                EffectCertainty = WorkerEffectEvidenceCertainty.DefiniteViolation, EffectWitness = witness
            }], request.Budgets, WorkerCacheStatus.Disabled, 0,
            requestHash: WorkerProtocolJson.ComputeRequestHash(request), versions: versions);
    }
}
