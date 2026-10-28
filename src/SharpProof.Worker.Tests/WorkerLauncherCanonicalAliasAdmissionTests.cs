using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [Test]
    [NonParallelizable]
    public async Task CanonicalDirectoryAliasCannotPublishOverActualPrivateRequest()
    {
        using var project = TestProject.Create(TautologySource);
        var compilation = project.CreateCompilation();
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var sourceRequest = project.CreateRequest(cacheEnabled: false);
        var privateDirectory = Path.Combine(project.DirectoryPath, "private-inputs");
        var aliasDirectory = Path.Combine(project.DirectoryPath, "private-alias");
        Directory.CreateDirectory(privateDirectory);
        Directory.CreateSymbolicLink(aliasDirectory, privateDirectory);
        try
        {
            var privateRequest = Path.Combine(privateDirectory, "request.json");
            var privateResult = Path.Combine(privateDirectory, "result.json");
            var aliasRequest = Path.Combine(aliasDirectory, "request.json");
            var publishedResult = Path.Combine(project.DirectoryPath, "published-result.json");
            var publishedManifest = Path.Combine(project.DirectoryPath, "published-manifest.json");
            var originalManifest = (await File.ReadAllBytesAsync(sourceRequest.CompilerManifest.Path)).ToImmutableArray();
            Assert.That(Path.GetFullPath(aliasRequest), Is.Not.EqualTo(Path.GetFullPath(privateRequest)));
            Assert.That(PublicationLease.CanonicalMember(aliasRequest), Is.EqualTo(PublicationLease.CanonicalMember(privateRequest)));
            var originalError = Console.Error;
            using var diagnostic = new StringWriter();
            int exitCode;
            try
            {
                Console.SetError(diagnostic);
                exitCode = await Launcher.Program.RunMain([
                    "verify", "--worker", typeof(SharpProofWorker).Assembly.Location,
                    "--request", privateRequest, "--result", privateResult,
                    "--compiler-manifest", sourceRequest.CompilerManifest.Path,
                    "--verify-policy", "advisory", "--assumption-policy", "allow", "--cache-enabled", "false",
                    "--publish-request", aliasRequest, "--publish-result", publishedResult,
                    "--publish-compiler-manifest", publishedManifest
                ], verify: null, validatePreflight: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
            }
            finally
            {
                Console.SetError(originalError);
            }
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(diagnostic.ToString(), Does.Contain("SharpProof I/O paths must be distinct."));
            Assert.That(await File.ReadAllBytesAsync(sourceRequest.CompilerManifest.Path), Is.EqualTo(originalManifest));
            Assert.That(File.Exists(privateRequest), Is.False);
            Assert.That(File.Exists(privateResult), Is.False);
            Assert.That(File.Exists(publishedResult), Is.False);
            Assert.That(File.Exists(publishedManifest), Is.False);
        }
        finally
        {
            // The link and its real target are both exclusively owned children; remove only the link first.
            Directory.Delete(aliasDirectory);
        }
    }
}
