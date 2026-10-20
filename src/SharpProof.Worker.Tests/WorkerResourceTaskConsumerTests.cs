using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [Test]
    public async Task JoinedTaskSourceLoopCostsReachCacheAndSarifWithValidatedScope()
    {
        using var project = TestProject.Create("using SharpProof.Attributes; using System.Threading.Tasks; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)] public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {for(int i=0;i<count;i++){}} }");
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
        Assert.That(second.ClaimResults.Single().ResourceBound!.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskJoinScope));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain(WorkerResourceModels.TaskJoinScope));
        Assert.That(row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("executionScope").GetString(),
            Is.EqualTo(WorkerResourceModels.TaskJoinScope));
        var json = WorkerProtocolJson.SerializeResponse(second);
        foreach (var mutation in new[] { "scope", "model", "duplicate-model", "projection", "duplicate-projection", "metadata", "lower", "expected", "ordinary-scope", "stripped-task", "witness" })
        {
            var modified = WorkerProtocolJson.DeserializeResponse(json)!;
            var claim = modified.ClaimResults.Single();
            var bound = claim.ResourceBound!;
            switch (mutation)
            {
                case "scope":
                    bound.ExecutionScope = "foreign";
                    break;
                case "model":
                    claim.ProofCore = [.. claim.ProofCore.Where(static item => item is null ? throw new ArgumentNullException(nameof(item)) : !item.StartsWith("resource-task-model:", StringComparison.Ordinal))];
                    break;
                case "duplicate-model":
                    claim.ProofCore = [.. claim.ProofCore, "resource-task-model:" + WorkerResourceModels.TaskJoinModel];
                    break;
                case "projection":
                    claim.ProofCore = [.. claim.ProofCore.Select(static item => item is null ? throw new ArgumentNullException(nameof(item)) : item.StartsWith("resource-task-projection:", StringComparison.Ordinal) ? "resource-task-projection:bad" : item)];
                    break;
                case "duplicate-projection":
                    claim.ProofCore = [.. claim.ProofCore, "resource-task-projection:" + new string('a', 64)];
                    break;
                case "metadata":
                    bound.MetadataAssumptions = [];
                    break;
                case "lower":
                    bound.Asymptotic!.Lower!.UnknownReason = null;
                    break;
                case "expected":
                    bound.Expected = null;
                    break;
                case "ordinary-scope":
                    bound.ExecutionScope = WorkerResourceModels.CallableScope;
                    break;
                case "stripped-task":
                    bound.ExecutionScope = WorkerResourceModels.CallableScope;
                    bound.MetadataAssumptions = [];
                    foreach (var assumption in claim.Assumptions)
                    { assumption.Used = false; }
                    break;
                case "witness":
                    bound.MetadataAssumptions.Single().WitnessIdentifier = "other";
                    break;
                default:
                    Assert.Fail("Unknown mutation");
                    break;
            }
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
}
