using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase("var a=new byte[9];", 40)]
    [TestCase("var a=new long[3];", 48)]
    [TestCase("for(int i=0;i<3;i++) { var a=new int[3]; }", 120)]
    [TestCase("if(n>0) { var a=new short[3]; } else { var a=new double[3]; }", 48)]
    [TestCase("", 0)]
    public async Task ByteContractsBindOwnedLayoutAndReferenceEvidence(string body, int upper)
    {
        ArgumentNullException.ThrowIfNull(body);
        var artifact = CreateArtifact(Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {upper})]", body), reviewedRuntime: true, x64: true);
        var target = Read(artifact);
        Assert.That(target.Compilation.ResourceTargetPlatform, Is.EqualTo("X64"));
        Assert.That(target.Compilation.References, Is.Empty);
        Assert.That(target.Compilation.ResourceMetadataReferences, Has.Length.EqualTo(1));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.Metric, Is.EqualTo(WorkerResourceMetric.AllocatedBytes));
        Assert.That(result.ResourceBound.CostModel, Is.EqualTo(WorkerResourceModels.AllocationCostModel));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var dependency = result.ResourceBound.MetadataAssumptions.Single();
        Assert.That(dependency.RuntimeModel, Is.EqualTo(ResourceRuntimeLayout.ModelId));
        Assert.That(dependency.CatalogSha256, Is.EqualTo(ResourceRuntimeLayout.ContentSha256.ToUpperInvariant()));
        Assert.That(dependency.ReferenceSha256, Is.EqualTo(target.Compilation.ResourceMetadataReferences[0].Modules[0].Sha256));
        Assert.That(dependency.EvidenceSource, Is.EqualTo(ResourceRuntimeLayout.EvidenceSource));
        Assert.That(result.Assumptions.Single(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Used).Id,
            Is.EqualTo(dependency.AssumptionId));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    public async Task ByteContractsRequireCompatibleTargetAndOwnedEvidence(int mutation)
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 100)]", "var a=new int[3];"), reviewedRuntime: true, x64: true));
        switch (mutation)
        {
            case 0:
                target.Compilation.ResourceTargetPlatform = "AnyCpu";
                break;
            case 1:
                target.Compilation.TargetFramework = "net10.0";
                break;
            case 2:
                target.Compilation.ResourceMetadataReferences = [];
                break;
            case 3:
                target.Compilation.ResourceMetadataReferences[0].Modules[0].Sha256 = "invalid";
                break;
            case 4:
                target.Entry.Assumptions = [];
                break;
            case 5:
                target.Entry.Assumptions[0].Id += "wrong-owner";
                break;
            case 6:
                target.Compilation.ResourceMetadataReferences[0].Identity = "System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";
                break;
            case 7:
                target.Compilation.ResourceMetadataReferences[0].Kind = "Module";
                break;
            case 8:
                target.Compilation.ResourceMetadataReferences[0].Modules = [];
                break;
            case 9:
                var reference = target.Compilation.ResourceMetadataReferences[0];
                var conflicting = System.Text.Json.JsonSerializer.Deserialize<CompilerReferenceSnapshot>(System.Text.Json.JsonSerializer.Serialize(reference))!;
                conflicting.Modules[0].Sha256 = new string('0', 64);
                target.Compilation.ResourceMetadataReferences = [reference, conflicting];
                break;
        }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceLayoutAssumptionsNotBound"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [Test]
    public async Task ByteEnvelopesCannotInventNumericalViolations()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", "var a=new int[3];"), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
    }

    [Test]
    public void OrdinaryResourceContractsDoNotCaptureLayoutReferences()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 1)]", "var a=new int[3];"), reviewedRuntime: true, x64: true);
        Assert.That(artifact.Compilation.ResourceMetadataReferences, Is.Empty);
        Assert.That(artifact.Manifest.Callables.Single().Assumptions, Is.Empty);
    }
    [Test]
    public async Task ByteAndElementClaimsRetainIndependentOutcomes()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)] [ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]",
            "var a=new int[3];"), reviewedRuntime: true, x64: true));
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        var elements = results.Single(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.ResourceBound?.Metric == WorkerResourceMetric.AllocatedElements);
        var bytes = results.Single(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.ResourceBound?.Metric == WorkerResourceMetric.AllocatedBytes);
        Assert.That(elements.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(elements.ResourceBound!.MetadataAssumptions, Is.Empty);
        Assert.That(elements.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        Assert.That(bytes.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(bytes.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
    }
    [TestCase("var a=new int[n];", 31, "n", 4)]
    [TestCase("var a=new char[n];", 31, "n", 2)]
    [TestCase("var a=new byte[values.Length];", 31, "values.Length", 1)]
    [TestCase("for(int i=0;i<n;i++) { var a=new int[3]; }", 0, "n", 40)]
    public async Task SymbolicByteContractsIncludeAlignmentAndRepeatedStorage(string body, int constant, string size, int coefficient)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(size);
        var target = Read(CreateArtifact(Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {constant}, \"{size}\", {coefficient})]", body), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Does.Contain(size));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
    }
}
