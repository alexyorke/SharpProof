using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task NormallyProducedArtifactWithDifferentCompilerOptionRejectsWithoutChangingProofBudgets()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var originalReference = request.CompilerManifest;
        var originalDepth = request.Budgets.MaximumExpressionDepth;
        Assert.That(originalDepth, Is.EqualTo(64));
        using var worker = SharpProofWorker.Create(request.Budgets);
        var admitted = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        AssertClaimVerdict(admitted, WorkerClaimOutcome.Proven);
        Assert.That(WorkerProtocolJson.Validate(admitted).IsValid, Is.True);

        var compilation = project.CreateCompilation();
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        var differentOptions = CompilerManifestArtifactProducer.Create(compilation,
            project.DirectoryPath, "net8.0", WorkerFeatureSet.All, discovery,
            65, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(differentOptions.MaximumExpressionDepth, Is.InRange(1, 256));
            Assert.That(differentOptions.MaximumExpressionDepth, Is.EqualTo(65));
            Assert.That(differentOptions.MaximumExpressionDepth, Is.Not.EqualTo(originalDepth));
        }
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(differentOptions, cancellationToken: CancellationToken.None));
        var path = Path.Combine(project.DirectoryPath, "different-compiler-options.json");
        await File.WriteAllBytesAsync(path, bytes);
        request.CompilerManifest = new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) };
        var rejected = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Budgets.MaximumExpressionDepth, Is.EqualTo(originalDepth));
            Assert.That(rejected.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(rejected.FailureReason, Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
            Assert.That(rejected.Errors.Single().Code, Is.EqualTo("compiler_manifest.options"));
            Assert.That(rejected.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(rejected).IsValid, Is.True);
        }
        request.CompilerManifest = originalReference;
        var restored = await worker.VerifyAsync(request, cancellationToken: CancellationToken.None);
        AssertClaimVerdict(restored, WorkerClaimOutcome.Proven);
        Assert.That(WorkerProtocolJson.Validate(restored).IsValid, Is.True);
        Assert.That(request.Budgets.MaximumExpressionDepth, Is.EqualTo(originalDepth));
    }
}
