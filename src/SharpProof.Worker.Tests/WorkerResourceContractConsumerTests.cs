using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
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
        claim = claim with { Outcome = WorkerClaimOutcome.Refuted };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim] }).IsValid, Is.False);
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
        evidence = evidence with { Metric = WorkerResourceMetric.ArrayAllocations };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [second.ClaimResults.Single() with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { Metric = originalMetric };
        var originalHash = evidence.DeclaredBoundSha256;
        evidence = evidence with { DeclaredBoundSha256 = WorkerProtocolVersions.EmptySha256 };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [second.ClaimResults.Single() with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { DeclaredBoundSha256 = originalHash };
        second = second with { Manifest = second.Manifest with { Claims = second.Manifest.Claims.SetItem(0, second.Manifest.Claims.Single() with { ResourceUpperBound = second.Manifest.Claims.Single().ResourceUpperBound! with { UpperBound = null! } }) } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [second.ClaimResults.Single() with { ResourceBound = evidence }] }).IsValid, Is.False);
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
        evidence = evidence with { Asymptotic = new WorkerResourceAsymptoticEvidence { UpperBound = "O(1)", PolynomialDegree = 0, LogarithmicDegree = 0, IsZero = false } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { Asymptotic = null };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.True);
        Assert.That(evidence.ObservedLowerBound, Is.EqualTo("3"));
        Assert.That(evidence.EvaluatedUpperBound, Is.EqualTo("2"));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(result => result.GetProperty("ruleId").GetString() == "SharpProof.Refuted");
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain("observed prefix: 3 > 2"));
        foreach (var malformed in new[] { "2", "1", "03", "-1", "1.0", "18446744073709551616" })
        {
            evidence = evidence with { ObservedLowerBound = malformed };
            Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False, malformed);
        }
        evidence = evidence with { ObservedLowerBound = "3" };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.True);
        claim = claim with { EffectWitness = claim.EffectWitness! with { Effects = loop ? WorkerEffectSet.Allocates : WorkerEffectSet.None } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        claim = claim with { EffectWitness = claim.EffectWitness! with { Effects = loop ? WorkerEffectSet.None : WorkerEffectSet.Allocates } };
        evidence = evidence with { ObservedLowerBound = "3" };
        evidence = evidence with { EvaluatedUpperBound = "02" };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { EvaluatedUpperBound = "2" };
        evidence = evidence with { InferredUpperBound = "3" };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { InferredUpperBound = null };
        claim = claim with { EffectWitness = claim.EffectWitness! with { Kind = "managed-array-allocation" } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        claim = claim with { EffectWitness = claim.EffectWitness! with { Kind = "resource-bound-exceeded" } };
        evidence = null!;
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
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
        var metadataRows = result.ResourceBound!.MetadataAssumptions;
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = null! } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = Enumerable.Repeat(metadata, 17).ToArray() } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = metadataRows } };
        var originalWitness = metadata.WitnessIdentifier;
        metadata = metadata with { WitnessIdentifier = null! };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        metadata = metadata with { WitnessIdentifier = originalWitness };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.True);
        Assert.That(metadata.RuntimeModel, Is.EqualTo("net8.0.32"));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(request, second, project.DirectoryPath));
        var row = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            .Single(item => item.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(row.GetProperty("message").GetProperty("text").GetString(), Does.Contain("conditional resource metadata"));
        var witness = metadata.WitnessIdentifier;
        metadata = metadata with { WitnessIdentifier = "bcl.math.abs.int32" };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        metadata = metadata with { WitnessIdentifier = witness };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        var runtime = metadata.RuntimeModel;
        metadata = metadata with { RuntimeModel = "net10.0" };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        metadata = metadata with { RuntimeModel = runtime };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        var assembly = metadata.ReferenceAssembly;
        metadata = metadata with { ReferenceAssembly = "foreign" };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        metadata = metadata with { ReferenceAssembly = assembly };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        var hash = metadata.ReferenceSha256;
        metadata = metadata with { ReferenceSha256 = "invalid" };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        metadata = metadata with { ReferenceSha256 = hash };
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [metadata] } };
        var assumption = result.Assumptions.Single(item => item is null ? throw new ArgumentNullException(nameof(item)) : item.Id == metadata.AssumptionId);
        assumption = assumption with { Used = false };
        result.Assumptions = result.Assumptions.Select(item => item is null ? throw new ArgumentNullException(nameof(item)) : item.Id == assumption.Id ? assumption : item).ToImmutableArray();
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
        assumption = assumption with { Used = true };
        result.Assumptions = result.Assumptions.Select(item => item is null ? throw new ArgumentNullException(nameof(item)) : item.Id == assumption.Id ? assumption : item).ToImmutableArray();
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.True);
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [] } };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result] }).IsValid, Is.False);
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
        evidence = evidence with { ObservedLowerBound = "1" };
        Assert.That(WorkerProtocolJson.Validate(response with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { ObservedLowerBound = null };
        evidence = evidence with { InferredUpperBound = "n" };
        Assert.That(WorkerProtocolJson.Validate(response with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { InferredUpperBound = null };
        var reason = evidence.UnknownReason;
        evidence = evidence with { UnknownReason = "bad\nreason" };
        Assert.That(WorkerProtocolJson.Validate(response with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { UnknownReason = reason };
        claim = claim with { Outcome = (WorkerClaimOutcome)999 };
        Assert.That(WorkerProtocolJson.Validate(response with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.False);
        claim = claim with { Outcome = WorkerClaimOutcome.Unknown };
        Assert.That(WorkerProtocolJson.Validate(response with { ClaimResults = [claim with { ResourceBound = evidence }] }).IsValid, Is.True);
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
        evidence = evidence with { MetadataAssumptions = [] };
        assumption = assumption with { Used = false };
        result.Assumptions = result.Assumptions.Select(item => item is null ? throw new ArgumentNullException(nameof(item)) : item.Id == assumption.Id ? assumption : item).ToImmutableArray();
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result with { ResourceBound = evidence }] }).IsValid, Is.False);
        evidence = evidence with { MetadataAssumptions = [dependency] };
        assumption = assumption with { Used = true };
        result.Assumptions = result.Assumptions.Select(item => item is null ? throw new ArgumentNullException(nameof(item)) : item.Id == assumption.Id ? assumption : item).ToImmutableArray();
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result with { ResourceBound = evidence }] }).IsValid, Is.True);
        var model = dependency.RuntimeModel;
        dependency = dependency with { RuntimeModel = "net8.0.32" };
        evidence = evidence with { MetadataAssumptions = [dependency] };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result with { ResourceBound = evidence }] }).IsValid, Is.False);
        dependency = dependency with { RuntimeModel = model };
        evidence = evidence with { MetadataAssumptions = [dependency] };
        evidence = evidence with { CostModel = WorkerResourceModels.CostModel };
        Assert.That(WorkerProtocolJson.Validate(second with { ClaimResults = [result with { ResourceBound = evidence }] }).IsValid, Is.False);
    }
}
