using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LogicalSourceStackContractsReachCacheAndSarifWithTheDeclaredModel(bool bytes)
    {
        var metric = bytes ? "SourceStackBytes" : "SourceActivationDepth";
        var bound = bytes ? 48 : 2;
        using var project = TestProject.Create("using SharpProof.Attributes; public static class Subject { " +
            $"[ResourceUpperBound(ResourceMetric.{metric},{bound})] public static void Target(int n) {{ Leaf(n); }} private static void Leaf(int n) {{}} }}");
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var claim = second.ClaimResults.Single();
        Assert.That(claim.ResourceBound!.CostModel, Is.EqualTo(WorkerResourceModels.StackCostModel));
        Assert.That(claim.ResourceBound.MetadataAssumptions, Is.Empty);
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("metric").GetString(), Is.EqualTo(metric));
        claim.Outcome = WorkerClaimOutcome.Refuted;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
    }
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
        Assert.That(first.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
            first.ClaimResults.Single().Reason + ": " + first.ClaimResults.Single().ResourceBound?.UnknownReason);
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
    [TestCase(false)]
    [TestCase(true)]
    public async Task NumericResourceViolationsReachValidatedCacheAndSarifConsumers(bool loop)
    {
        var metric = loop ? "LoopBodyEntries" : "AllocatedElements";
        var body = loop ? "for(int i=0;i<3;i++) {}" : "var a=new int[3];";
        using var project = TestProject.Create("using SharpProof.Attributes; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric." + metric + ", 2)] public static void Target() { " + body + " } }");
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
        evidence.Asymptotic = new WorkerResourceAsymptoticEvidence { UpperBound = "O(1)", PolynomialDegree = 0, LogarithmicDegree = 0, IsZero = false };
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        evidence.Asymptotic = null;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
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
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
        claim.EffectWitness!.Effects = loop ? WorkerEffectSet.Allocates : WorkerEffectSet.None;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        claim.EffectWitness.Effects = loop ? WorkerEffectSet.None : WorkerEffectSet.Allocates;
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
    [Test]
    public async Task ReviewedResourceMetadataReachesValidatedCacheAndSarifConsumers()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                [ResourceUpperBound(ResourceMetric.ArrayAllocations, 0)]
                public static void Target(int n) { int value=System.Math.Max(n,0); }
            }
            """);
        project.UseNetCoreReferencePack(8);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(first.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
            first.ClaimResults.Single().Reason + ": " + first.ClaimResults.Single().ResourceBound?.UnknownReason);
        AssertClaimVerdict(first, WorkerClaimOutcome.Proven);
        AssertClaimVerdict(second, WorkerClaimOutcome.Proven);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var result = second.ClaimResults.Single();
        var metadata = result.ResourceBound!.MetadataAssumptions.Single();
        Assert.That(metadata.WitnessIdentifier, Is.EqualTo("bcl.math.max.int32-int32"));
        var metadataRows = result.ResourceBound.MetadataAssumptions;
        result.ResourceBound.MetadataAssumptions = null!;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        result.ResourceBound.MetadataAssumptions = Enumerable.Repeat(metadata, 17).ToArray();
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        result.ResourceBound.MetadataAssumptions = metadataRows;
        var originalWitness = metadata.WitnessIdentifier;
        metadata.WitnessIdentifier = null!;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        metadata.WitnessIdentifier = originalWitness;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
        Assert.That(metadata.RuntimeModel, Is.EqualTo("net8.0.32"));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain("conditional resource metadata"));
        var witness = metadata.WitnessIdentifier;
        metadata.WitnessIdentifier = "bcl.math.abs.int32";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        metadata.WitnessIdentifier = witness;
        var runtime = metadata.RuntimeModel;
        metadata.RuntimeModel = "net10.0";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        metadata.RuntimeModel = runtime;
        var assembly = metadata.ReferenceAssembly;
        metadata.ReferenceAssembly = "foreign";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        metadata.ReferenceAssembly = assembly;
        var hash = metadata.ReferenceSha256;
        metadata.ReferenceSha256 = "invalid";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        metadata.ReferenceSha256 = hash;
        var assumption = result.Assumptions.Single(item => item is null ? throw new ArgumentNullException(nameof(item)) : item.Id == metadata.AssumptionId);
        assumption.Used = false;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        assumption.Used = true;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
        result.ResourceBound.MetadataAssumptions = [];
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
    }
    [Test]
    public async Task UnknownResourceContractsRejectPartialAndUndefinedEvidence()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 0, "n", 1)]
                public static void Target(int n) { for(int i=0;i<n;i+=2) {} }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(response, WorkerClaimOutcome.Unknown);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.ResourceBound, Is.Not.Null);
        var evidence = claim.ResourceBound!;
        Assert.That(evidence.UnknownReason, Is.Not.Empty);
        evidence.ObservedLowerBound = "1";
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False);
        evidence.ObservedLowerBound = null;
        evidence.InferredUpperBound = "n";
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False);
        evidence.InferredUpperBound = null;
        var reason = evidence.UnknownReason;
        evidence.UnknownReason = "bad\nreason";
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False);
        evidence.UnknownReason = reason;
        claim.Outcome = (WorkerClaimOutcome)999;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False);
        claim.Outcome = WorkerClaimOutcome.Unknown;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task LayoutBoundByteContractsReachValidatedCacheAndSarifConsumers(bool violates)
    {
        var bound = violates ? 80 : 120;
        var expected = violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven;
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                [ResourceUpperBound(ResourceMetric.AllocatedBytes, {{bound}})]
                public static void Target() { for(int i=0;i<3;i++) { var a=new int[3]; } }
            }
            """);
        project.UseNetCoreReferencePack(8);
        var options = TestCompilation.CreateOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary)
            .WithPlatform(Microsoft.CodeAnalysis.Platform.X64);
        var request = project.CreateRequest(cacheEnabled: true, compilationOptions: options);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var second = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertClaimVerdict(first, expected);
        AssertClaimVerdict(second, expected);
        Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(second, WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
            second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        var result = second.ClaimResults.Single();
        var evidence = result.ResourceBound!;
        Assert.That(evidence.Metric, Is.EqualTo(WorkerResourceMetric.AllocatedBytes));
        if (violates)
        {
            Assert.That(evidence.ObservedLowerBound, Is.EqualTo("120"));
            Assert.That(evidence.EvaluatedUpperBound, Is.EqualTo("80"));
            Assert.That(evidence.InferredUpperBound, Is.Null);
        }
        else
        { Assert.That(evidence.InferredUpperBound, Is.EqualTo("120")); }
        Assert.That(evidence.CostModel, Is.EqualTo(WorkerResourceModels.AllocationCostModel));
        var dependency = evidence.MetadataAssumptions.Single();
        Assert.That(dependency.RuntimeModel, Is.EqualTo(WorkerResourceModels.AllocationLayout));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof." + expected);
        Assert.That(row.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("metric").GetString(), Is.EqualTo("AllocatedBytes"));
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain("conditional resource metadata"));
        var assumption = result.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == dependency.AssumptionId);
        evidence.MetadataAssumptions = [];
        assumption.Used = false;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        evidence.MetadataAssumptions = [dependency];
        assumption.Used = true;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
        var model = dependency.RuntimeModel;
        dependency.RuntimeModel = "net8.0.32";
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
        dependency.RuntimeModel = model;
        evidence.CostModel = WorkerResourceModels.CostModel;
        Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.False);
    }
}
