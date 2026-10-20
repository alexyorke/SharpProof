using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualLauncherResultFileAdmissionTests
{
    [Test]
    public async Task TimeoutPromotionPreservesActualCompletedResponseAndIgnoresAbsentResult()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """;
        var compilation = TestCompilation.CreateExact("LauncherResultFileAdmission", [("Subject.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(response.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var resultPath = Path.Combine(project.Snapshot.CompilerManifest.Compilation.ProjectDirectory, "actual-completed-result.json");
        var absentPath = Path.Combine(project.Snapshot.CompilerManifest.Compilation.ProjectDirectory, "absent-result.json");
        var bytes = Encoding.UTF8.GetBytes(WorkerProtocolJson.SerializeCanonicalResponse(response));
        await File.WriteAllBytesAsync(resultPath, bytes);
        var expectedInputHash = project.Bind().InputHash;
        var versions = Launcher.Program.ExpectedVersions();
        // The maintained parser must leave a real completed response unchanged.
        await Launcher.Program.PromotePreManifestProjectTimeoutAsync(resultPath, project.Request,
            project.Snapshot.CompilerManifest, expectedInputHash, versions, TestContext.CurrentContext.CancellationToken);
        Assert.That(await File.ReadAllBytesAsync(resultPath), Is.EqualTo(bytes));
        Assert.That(File.Exists(absentPath), Is.False);
        // An actual missing file is an invalid lifecycle input, not a synthetic timeout result.
        await Launcher.Program.PromotePreManifestProjectTimeoutAsync(absentPath, project.Request,
            project.Snapshot.CompilerManifest, expectedInputHash, versions, TestContext.CurrentContext.CancellationToken);
        Assert.That(File.Exists(absentPath), Is.False);
        Assert.That(await File.ReadAllBytesAsync(resultPath), Is.EqualTo(bytes));
        Assert.That(WorkerProtocolJson.Validate(WorkerProtocolJson.DeserializeResponse(Encoding.UTF8.GetString(bytes))!).IsValid, Is.True);
    }
}
