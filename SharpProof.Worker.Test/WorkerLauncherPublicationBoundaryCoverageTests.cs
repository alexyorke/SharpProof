using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase("none")]
    [NonParallelizable]
    public async Task PublicationPreservesPrivateInputsAndPublishesDistinctMembers(string alias)
    {
        System.ArgumentNullException.ThrowIfNull(alias);
        using var project = TestProject.Create(TautologySource);
        var sourceRequest = project.CreateRequest(cacheEnabled: false);
        var privateRequest = Path.Combine(project.DirectoryPath, "private-request.json");
        var privateResult = Path.Combine(project.DirectoryPath, "private-result.json");
        var privateManifest = sourceRequest.CompilerManifest.Path;
        var originalManifest = await File.ReadAllBytesAsync(privateManifest);
        var publishedRequest = alias switch
        {
            "request" => privateRequest,
            "result" => privateResult,
            "manifest" => privateManifest,
            _ => Path.Combine(project.DirectoryPath, "published-request.json")
        };
        var publishedResult = Path.Combine(project.DirectoryPath, "published-result.json");
        var publishedManifest = Path.Combine(project.DirectoryPath, "published-manifest.json");
        var originalError = Console.Error;
        using var diagnostic = new StringWriter();
        int exitCode;
        try
        {
            Console.SetError(diagnostic);
            exitCode = await Launcher.Program.Main([
                "verify", "--worker", typeof(SharpProofWorker).Assembly.Location,
                "--request", privateRequest, "--result", privateResult,
                "--compiler-manifest", privateManifest,
                "--verify-policy", "advisory", "--assumption-policy", "allow",
                "--cache-enabled", "false",
                "--publish-request", publishedRequest, "--publish-result", publishedResult,
                "--publish-compiler-manifest", publishedManifest
            ]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(privateResult))
            ?? throw new AssertionException("The launcher did not produce a private response.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
            Assert.That(await File.ReadAllBytesAsync(privateManifest), Is.EqualTo(originalManifest));
            Assert.That(exitCode, Is.EqualTo(alias == "none" ? 0 : 3), diagnostic.ToString());
        }

        if (alias == "none")
        {
            var published = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(publishedResult))
                ?? throw new AssertionException("The launcher did not produce a published response.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(publishedRequest), Is.True);
                Assert.That(await File.ReadAllBytesAsync(publishedManifest), Is.EqualTo(originalManifest));
                Assert.That(published.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(WorkerProtocolJson.Validate(published).IsValid, Is.True);
            }
        }
        else
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(diagnostic.ToString(), Does.Contain("SharpProof worker result could not be published."));
                Assert.That(File.Exists(publishedResult), Is.False);
                Assert.That(File.Exists(publishedManifest), Is.False);
            }
        }
    }
}
