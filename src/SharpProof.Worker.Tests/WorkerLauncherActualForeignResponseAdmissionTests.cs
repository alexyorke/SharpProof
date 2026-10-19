using System.Collections.Immutable;
using System.Text;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task PublicationRejectsActualOtherProjectNativeResponseBeforeStableWrites()
    {
        const string firstSource = "using SharpProof.Attributes; public static class FirstSubject { public static int Target(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; } }";
        const string otherSource = "using SharpProof.Attributes; public static class OtherSubject { public static int Target(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; } }";
        using var first = TestProject.Create(firstSource);
        using var other = TestProject.Create(otherSource);
        foreach (var project in ImmutableArray.Create(first, other))
        {
            var compilation = project.CreateCompilation();
            TestCompilation.AssertNoErrors(compilation);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        }
        var request = first.CreateRequest(cacheEnabled: false);
        var otherRequest = other.CreateRequest(cacheEnabled: false);
        var originalRequestJson = WorkerProtocolJson.SerializeRequest(request);
        var originalManifestPath = request.CompilerManifest.Path;
        var artifactBytes = await File.ReadAllBytesAsync(originalManifestPath);
        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, TestContext.CurrentContext.CancellationToken);
        using var firstWorker = SharpProofWorker.Create(request.Budgets);
        using var otherWorker = SharpProofWorker.Create(otherRequest.Budgets);
        var actual = await firstWorker.VerifyAsync(request, snapshot, TestContext.CurrentContext.CancellationToken);
        var foreign = await otherWorker.VerifyAsync(otherRequest, cancellationToken: TestContext.CurrentContext.CancellationToken);
        foreach (var response in ImmutableArray.Create(actual, foreign))
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(response.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
        Assert.That(foreign.InputHash, Is.Not.EqualTo(actual.InputHash));
        Assert.That(foreign.Manifest.Claims.Single().ClaimId, Is.Not.EqualTo(actual.Manifest.Claims.Single().ClaimId));
        var privateRequest = Path.Combine(first.DirectoryPath, "private-request.json");
        var privateResult = Path.Combine(first.DirectoryPath, "private-result.json");
        await File.WriteAllTextAsync(privateRequest, originalRequestJson, new UTF8Encoding(false));
        var privateResultBytes = Encoding.UTF8.GetBytes(WorkerProtocolJson.SerializeCanonicalResponse(actual));
        await File.WriteAllBytesAsync(privateResult, privateResultBytes);
        var positive = Arguments("accepted");
        await Launcher.Program.PublishOutputsAsync(positive, request, snapshot.CompilerManifest, artifactBytes,
            snapshot.InputHash, Launcher.Program.ExpectedVersions(), actual, TestContext.CurrentContext.CancellationToken);
        Assert.That(await File.ReadAllBytesAsync(positive.PublishCompilerManifestPath!), Is.EqualTo(artifactBytes));
        var published = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(positive.PublishResultPath!))
            ?? throw new AssertionException("Matching actual response was not published.");
        var publishedRequest = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(positive.PublishRequestPath!))
            ?? throw new AssertionException("Matching actual request was not published.");
        Assert.That(WorkerProtocolJson.SerializeRequest(request), Is.EqualTo(originalRequestJson));
        Assert.That(WorkerProtocolJson.ValidateForRequest(published, WorkerProtocolJson.ComputeRequestHash(publishedRequest),
            snapshot.InputHash, snapshot.CompilerManifest.Manifest, publishedRequest, Launcher.Program.ExpectedVersions()).IsValid, Is.True);
        var negativeRequest = WorkerProtocolJson.DeserializeRequest(originalRequestJson)
            ?? throw new AssertionException("The original actual request did not deserialize.");
        var negative = Arguments("rejected");
        // Wrong actual owner at the raw binding boundary; never admitted as first-project evidence.
        Func<Task> reject = () => Launcher.Program.PublishOutputsAsync(negative, negativeRequest,
            snapshot.CompilerManifest, artifactBytes, snapshot.InputHash, Launcher.Program.ExpectedVersions(), foreign, TestContext.CurrentContext.CancellationToken);
        Assert.ThrowsAsync<IOException>(reject);
        Assert.That(File.Exists(negative.PublishRequestPath!), Is.False);
        Assert.That(File.Exists(negative.PublishResultPath!), Is.False);
        Assert.That(File.Exists(negative.PublishCompilerManifestPath!), Is.False);
        Assert.That(await File.ReadAllBytesAsync(originalManifestPath), Is.EqualTo(artifactBytes));
        Assert.That(await File.ReadAllTextAsync(privateRequest), Is.EqualTo(originalRequestJson));
        Assert.That(await File.ReadAllBytesAsync(privateResult), Is.EqualTo(privateResultBytes));

        Launcher.LauncherArguments Arguments(string prefix)
        {
            System.ArgumentNullException.ThrowIfNull(prefix);
            Assert.That(Launcher.LauncherArguments.TryParse([
                "verify", "--request", privateRequest, "--result", privateResult,
                "--compiler-manifest", originalManifestPath, "--verify-policy", "advisory", "--assumption-policy", "allow",
                "--cache-enabled", "false", "--publish-request", Path.Combine(first.DirectoryPath, prefix + "-request.json"),
                "--publish-result", Path.Combine(first.DirectoryPath, prefix + "-result.json"),
                "--publish-compiler-manifest", Path.Combine(first.DirectoryPath, prefix + "-manifest.json")
            ], out var arguments), Is.True);
            return arguments ?? throw new AssertionException("Owned publication arguments did not parse.");
        }
    }
}
