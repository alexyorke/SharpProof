using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task NativeResourceContractsReachValidatedCacheAndSarifConsumers()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                [ResourceUpperBound(ResourceMetric.AllocatedElements, 0, "n", 2)]
                public static void Target(int n) { for(int i=0;i<n;i++) { var a = new int[2]; } }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var evidence = second.ClaimResults.Single().ResourceBound!;
        Assert.That(evidence.Metric, Is.EqualTo(WorkerResourceMetric.AllocatedElements));
        Assert.That(evidence.InferredUpperBound, Does.Contain("n"));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("metric").GetString(), Is.EqualTo("AllocatedElements"));
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain("resource:AllocatedElements"));
        var originalMetric = evidence.Metric;
        evidence.Metric = WorkerResourceMetric.ArrayAllocations;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        evidence.Metric = originalMetric;
        var originalHash = evidence.DeclaredBoundSha256;
        evidence.DeclaredBoundSha256 = WorkerProtocolVersions.EmptySha256;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        evidence.DeclaredBoundSha256 = originalHash;
        second.Manifest.Claims.Single().ResourceUpperBound!.UpperBound = null!;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
    }
    [Test]
    public async Task NumericResourceViolationsReachValidatedCacheAndSarifConsumers()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                [ResourceUpperBound(ResourceMetric.AllocatedElements, 2)]
                public static void Target() { var a = new int[3]; }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);
        AssertClaimVerdict(second, WorkerClaimOutcome.Refuted);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var claim = second.ClaimResults.Single();
        var evidence = claim.ResourceBound!;
        Assert.That(evidence.ObservedLowerBound, Is.EqualTo("3"));
        Assert.That(evidence.EvaluatedUpperBound, Is.EqualTo("2"));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("ruleId").GetString() == "SharpProof.Refuted");
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain("observed prefix: 3 > 2"));
        foreach (var malformed in new[] { "2", "1", "03", "-1", "1.0", "18446744073709551616" })
        {
            evidence.ObservedLowerBound = malformed;
            Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False, malformed);
        }
        evidence.ObservedLowerBound = "3";
        evidence.EvaluatedUpperBound = "02";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        evidence.EvaluatedUpperBound = "2";
        evidence.InferredUpperBound = "3";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        evidence.InferredUpperBound = null;
        claim.EffectWitness!.Kind = "managed-array-allocation";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        claim.EffectWitness.Kind = "resource-bound-exceeded";
        claim.ResourceBound = null;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
    }
}
