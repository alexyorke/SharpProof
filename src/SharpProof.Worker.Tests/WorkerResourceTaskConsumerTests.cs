using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [TestCase(false)]
    [TestCase(true)]
    [TestCase(false, "ArrayAllocations", 4, 1)]
    [TestCase(true, "ArrayAllocations", 4, 1)]
    [TestCase(false, "AllocatedElements", 8, 2)]
    [TestCase(true, "AllocatedElements", 8, 2)]
    public async Task JoinedTaskSourceLoopCostsReachCacheAndSarifWithValidatedScope(bool configuredAwait,
        string metric = "LoopBodyEntries", int constant = 3, int coefficient = 1)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var scope = metric == "LoopBodyEntries" ? WorkerResourceModels.TaskJoinScope : WorkerResourceModels.TaskSourceAllocationScope;
        using var project = TestProject.Create("using SharpProof.Attributes; using System.Threading.Tasks; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric." + metric + "," + constant + ",nameof(n)," + coefficient + ")] public static " + (configuredAwait ? "async " : "") +
            "Task Target(int n) => " + (configuredAwait ? "await " : "") + "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3)))" +
            (configuredAwait ? ".ConfigureAwait(false)" : "") + "; static void Child(int count) {for(int i=0;i<count;i++){" +
            (metric == "LoopBodyEntries" ? "" : "var data=new int[2];") + "}} }");
        project.UseNetCoreReferencePack(8);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(first.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), first.ClaimResults.Single().ResourceBound?.UnknownReason);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        Assert.That(second.ClaimResults.Single().ResourceBound!.ExecutionScope, Is.EqualTo(scope));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain(scope));
        Assert.That(row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("executionScope").GetString(),
            Is.EqualTo(scope));
        var json = WorkerProtocolJson.SerializeResponse(second);
        foreach (var mutation in new[] { "scope", "exchanged-task-scope", "model", "duplicate-model", "projection", "duplicate-projection", "metadata", "lower", "expected", "ordinary-scope", "stripped-task", "witness" })
        {
            var modified = WorkerProtocolJson.DeserializeResponse(json)!;
            var claim = modified.ClaimResults.Single();
            var bound = claim.ResourceBound!;
            switch (mutation)
            {
                case "scope":
                    bound = bound with { ExecutionScope = "foreign" };
                    break;
                case "exchanged-task-scope":
                    bound = bound with { ExecutionScope = scope == WorkerResourceModels.TaskJoinScope ? WorkerResourceModels.TaskSourceAllocationScope : WorkerResourceModels.TaskJoinScope };
                    break;
                case "model":
                    claim.ProofCore = [.. claim.ProofCore.Where(static item => item is null ? throw new ArgumentNullException(nameof(item)) : !item.StartsWith("resource-task-model:", StringComparison.Ordinal))];
                    break;
                case "duplicate-model":
                    claim.ProofCore = [.. claim.ProofCore, "resource-task-model:" + bound.MetadataAssumptions.Single().RuntimeModel];
                    break;
                case "projection":
                    claim.ProofCore = [.. claim.ProofCore.Select(static item => item is null ? throw new ArgumentNullException(nameof(item)) : item.StartsWith("resource-task-projection:", StringComparison.Ordinal) ? "resource-task-projection:bad" : item)];
                    break;
                case "duplicate-projection":
                    claim.ProofCore = [.. claim.ProofCore, "resource-task-projection:" + new string('a', 64)];
                    break;
                case "metadata":
                    bound = bound with { MetadataAssumptions = [] };
                    break;
                case "lower":
                    bound = bound with { Asymptotic = bound.Asymptotic! with { Lower = bound.Asymptotic!.Lower! with { UnknownReason = null } } };
                    break;
                case "expected":
                    bound = bound with { Expected = null };
                    break;
                case "ordinary-scope":
                    bound = bound with { ExecutionScope = WorkerResourceModels.CallableScope };
                    break;
                case "stripped-task":
                    bound = bound with { ExecutionScope = WorkerResourceModels.CallableScope };
                    bound = bound with { MetadataAssumptions = [] };
                    claim.Assumptions = claim.Assumptions.Select(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption with { Used = false }).ToImmutableArray();
                    break;
                case "witness":
                    bound = bound with { MetadataAssumptions = [bound.MetadataAssumptions.Single() with { WitnessIdentifier = "other" }] };
                    break;
                default:
                    Assert.Fail("Unknown mutation");
                    break;
            }
            modified = modified with { ClaimResults = [claim with { ResourceBound = bound }] };
            var validation = WorkerProtocolJson.Validate(modified);
            Assert.That(validation.IsValid, Is.False, mutation);
            Assert.That(validation.Errors.Any(static error => error is null ? throw new ArgumentNullException(nameof(error)) :
                error.Code == "response.resource_evidence"), Is.True, mutation);
        }
        var missing = JsonNode.Parse(json)!.AsObject();
        missing["claimResults"]![0]!["resourceBound"]!.AsObject().Remove("executionScope");
        Action decodeMissing = () => { _ = WorkerProtocolJson.DeserializeResponse(missing.ToJsonString()); };
        Assert.Throws<JsonException>(decodeMissing);
    }
    [TestCase("PeakAuxiliaryBytes", false)]
    [TestCase("PeakAuxiliaryBytes", true)]
    [TestCase("RetainedBytes", false)]
    [TestCase("RetainedBytes", true)]
    public async Task JoinedTaskLifetimeReachesValidatedCacheAndSarifWithNormalScope(string metric, bool configured)
    {
        ArgumentNullException.ThrowIfNull(metric);
        using var project = TestProject.Create("using SharpProof.Attributes; using System.Threading.Tasks; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric." + metric + ",1000)] public static " + (configured ? "async " : "") +
            "Task Target(int n) => " + (configured ? "await " : "") + "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3)))" +
            (configured ? ".ConfigureAwait(false)" : "") + "; static void Child(int n) {for(int i=0;i<n;i++){}} }");
        project.UseNetCoreReferencePack(8);
        var options = TestCompilation.CreateOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
            .WithPlatform(Microsoft.CodeAnalysis.Platform.X64);
        var request = project.CreateRequest(cacheEnabled: true, compilationOptions: options);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var evidence = second.ClaimResults.Single().ResourceBound!;
        Assert.That(evidence.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskStorageScope));
        Assert.That(evidence.MetadataAssumptions, Has.Length.EqualTo(3));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain(WorkerResourceModels.TaskStorageScope));
        Assert.That(row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("executionScope").GetString(),
            Is.EqualTo(WorkerResourceModels.TaskStorageScope));
        var json = WorkerProtocolJson.SerializeResponse(second);
        foreach (var mutation in new[] { "ordinary-scope", "work-scope", "stripped-task", "storage-model", "compiler-model", "extra-model", "reference", "layout", "lower", "expected", "cumulative" })
        {
            var modified = WorkerProtocolJson.DeserializeResponse(json)!;
            var claim = modified.ClaimResults.Single();
            var bound = claim.ResourceBound!;
            switch (mutation)
            {
                case "ordinary-scope":
                    bound = bound with { ExecutionScope = WorkerResourceModels.CallableScope };
                    break;
                case "work-scope":
                    bound = bound with { ExecutionScope = WorkerResourceModels.TaskJoinScope };
                    break;
                case "stripped-task":
                    bound = bound with { ExecutionScope = WorkerResourceModels.CallableScope };
                    bound = bound with { MetadataAssumptions = [] };
                    claim.Assumptions = claim.Assumptions.Select(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption with { Used = false }).ToImmutableArray();
                    break;
                case "storage-model":
                    claim.ProofCore = [.. claim.ProofCore.Where(static item => item is not null && !item.StartsWith("resource-task-storage-model:", StringComparison.Ordinal))];
                    break;
                case "compiler-model":
                    claim.ProofCore = [.. claim.ProofCore.Where(static item => item is not null && !item.StartsWith("resource-task-compiler-carriers:", StringComparison.Ordinal))];
                    break;
                case "extra-model":
                    claim.ProofCore = [.. claim.ProofCore, "resource-task-model:foreign"];
                    break;
                case "reference":
                    var storageDependency = bound.MetadataAssumptions.Single(static dependency => dependency is not null &&
                        dependency.RuntimeModel is WorkerResourceModels.TaskStorageModel or WorkerResourceModels.ConfiguredTaskStorageModel);
                    bound = bound with { MetadataAssumptions = [.. bound.MetadataAssumptions.Select(dependency => dependency is null ? throw new ArgumentNullException(nameof(dependency)) : ReferenceEquals(dependency, storageDependency) ? dependency with { ReferenceSha256 = new string('a', 64) } : dependency)] };
                    break;
                case "layout":
                    bound = bound with { MetadataAssumptions = [.. bound.MetadataAssumptions.Where(static dependency => dependency is not null && dependency.RuntimeModel != WorkerResourceModels.AllocationLayout)] };
                    break;
                case "lower":
                    bound = bound with { Asymptotic = bound.Asymptotic! with { Lower = bound.Asymptotic!.Lower! with { UnknownReason = null } } };
                    break;
                case "expected":
                    bound = bound with { Expected = null };
                    break;
                case "cumulative":
                    bound = bound with { Metric = WorkerResourceMetric.AllocatedBytes };
                    break;
                default:
                    Assert.Fail("Unknown mutation");
                    break;
            }
            modified = modified with { ClaimResults = [claim with { ResourceBound = bound }] };
            var validation = WorkerProtocolJson.Validate(modified);
            Assert.That(validation.IsValid, Is.False, mutation);
            Assert.That(validation.Errors.Any(static error => error is not null && error.Code == "response.resource_evidence"), Is.True, mutation);
        }
    }
}
