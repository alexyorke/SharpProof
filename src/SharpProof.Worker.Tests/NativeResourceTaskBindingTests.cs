using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    private static CompilerCallablePreparation TaskBindingTarget()
    {
        return Read(CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {for(int i=0;i<count;i++){}} }", reviewedRuntime: true));
    }

    [Test]
    public async Task TaskRuntimeBindingPreservesConditionalOwnedEvidenceWithoutPublishingVerdicts()
    {
        var target = TaskBindingTarget();
        var dependency = NativeResourceTaskBinding.Bind(target, TestContext.CurrentContext.CancellationToken);
        Assert.That(dependency, Is.Not.Null);
        Assert.That(dependency!.AssumptionId, Is.EqualTo(ResourceTaskCatalog.AssumptionId(target.Entry.CallableId,
            target.ForkJoinProjection!.FrameworkAssemblyIdentity)));
        Assert.That(dependency.CatalogSha256, Is.EqualTo(ResourceTaskCatalog.ContentSha256));
        Assert.That(dependency.ReferenceSha256, Is.EqualTo(target.Compilation.ResourceMetadataReferences.Single().Modules.Single().Sha256));
        Assert.That(dependency.WitnessIdentifier, Is.EqualTo(ResourceTaskCatalog.Witness));
        Assert.That(dependency.RuntimeModel, Is.EqualTo(ResourceTaskCatalog.Model));
        Assert.That(dependency.EvidenceSource, Is.EqualTo(ResourceTaskCatalog.EvidenceSource));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.All(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : !assumption.Used), Is.True);
    }

    [TestCase("projection")]
    [TestCase("entry")]
    [TestCase("root")]
    [TestCase("projection-root")]
    [TestCase("framework")]
    [TestCase("framework-version")]
    [TestCase("assumption")]
    [TestCase("assumption-kind")]
    [TestCase("assumption-owner")]
    [TestCase("duplicate-assumption")]
    [TestCase("references")]
    [TestCase("null-reference")]
    [TestCase("reference-identity")]
    [TestCase("reference-kind")]
    [TestCase("reference-hash")]
    [TestCase("module-count")]
    [TestCase("null-module")]
    [TestCase("conflicting-hash")]
    [TestCase("target-framework")]
    [TestCase("target-framework-null")]
    public void TaskRuntimeBindingRejectsMissingForeignOrAmbiguousEvidence(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = TaskBindingTarget();
        var projection = target.ForkJoinProjection!;
        var reference = target.Compilation.ResourceMetadataReferences.Single();
        switch (mutation)
        {
            case "projection":
                target = target with { ForkJoinProjection = null };
                break;
            case "entry":
                target = target with { TotalEntry = null };
                break;
            case "root":
                target = target with { TotalEntry = target.TotalEntry! with { CallableId = "other" } };
                break;
            case "projection-root":
                target = target with { ForkJoinProjection = projection with { RootCallableId = "other" } };
                break;
            case "framework":
                target = target with { ForkJoinProjection = projection with { FrameworkAssemblyIdentity = "foreign" } };
                break;
            case "framework-version":
                target = target with { ForkJoinProjection = projection with { FrameworkAssemblyIdentity = projection.FrameworkAssemblyIdentity.Replace("8.0.0.0", "9.0.0.0", StringComparison.Ordinal) } };
                break;
            case "assumption":
                target.Entry.Assumptions = [];
                break;
            case "assumption-kind":
                target.Entry.Assumptions.Single().Kind = WorkerAssumptionKind.Unspecified;
                break;
            case "assumption-owner":
                target.Entry.Assumptions.Single().Id = ResourceTaskCatalog.AssumptionId("other", projection.FrameworkAssemblyIdentity);
                break;
            case "duplicate-assumption":
                target.Entry.Assumptions = [.. target.Entry.Assumptions, target.Entry.Assumptions.Single()];
                break;
            case "references":
                target.Compilation.ResourceMetadataReferences = [];
                break;
            case "null-reference":
                target.Compilation.ResourceMetadataReferences = [null!];
                break;
            case "reference-identity":
                reference.Identity = "foreign";
                break;
            case "reference-kind":
                reference.Kind = "Module";
                break;
            case "reference-hash":
                reference.Modules.Single().Sha256 = "bad";
                break;
            case "module-count":
                reference.Modules = [];
                break;
            case "null-module":
                reference.Modules = [null!];
                break;
            case "conflicting-hash":
                target.Compilation.ResourceMetadataReferences = [reference, new CompilerReferenceSnapshot
                {
                    Kind = reference.Kind, Identity = reference.Identity,
                    Modules = [new CompilerReferenceModuleSnapshot { Sha256 = new string('a', 64) }]
                }];
                break;
            case "target-framework":
                target.Compilation.TargetFramework = "net9.0";
                break;
            case "target-framework-null":
                target.Compilation.TargetFramework = null!;
                break;
            default:
                Assert.Fail("Unknown mutation");
                break;
        }
        Assert.That(NativeResourceTaskBinding.Bind(target, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [Test]
    public void TaskRuntimeBindingAcceptsMatchingImagesAndPlatformQualifiedTarget()
    {
        var target = TaskBindingTarget();
        target.Compilation.TargetFramework = "net8.0-windows";
        var reference = target.Compilation.ResourceMetadataReferences.Single();
        target.Compilation.ResourceMetadataReferences = [reference, reference, new CompilerReferenceSnapshot { Identity = "unrelated" }];
        Assert.That(NativeResourceTaskBinding.Bind(target, TestContext.CurrentContext.CancellationToken), Is.Not.Null);
    }

    [Test]
    public void TaskRuntimeBindingHonorsCancellationAndNullGuards()
    {
        var target = TaskBindingTarget();
        using var source = new CancellationTokenSource();
        source.Cancel();
        Action canceled = () => { _ = NativeResourceTaskBinding.Bind(target, source.Token); };
        Assert.Throws<OperationCanceledException>(canceled);
        Action missing = () => { _ = NativeResourceTaskBinding.Bind(null!, default); };
        Assert.Throws<ArgumentNullException>(missing);
    }

    [TestCase("System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e", true)]
    [TestCase("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", true)]
    [TestCase("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=0000000000000000", false)]
    [TestCase("", false)]
    public void TaskRuntimeCatalogRequiresReviewedIdentity(string identity, bool expected)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Assert.That(ResourceTaskCatalog.IsReviewedReference(identity), Is.EqualTo(expected));
        Assert.That(ResourceTaskCatalog.ContentSha256, Does.Match("^[A-F0-9]{64}$"));
        Assert.That(ResourceTaskCatalog.AssumptionId("left", identity), Is.Not.EqualTo(ResourceTaskCatalog.AssumptionId("right", identity)));
    }
}
