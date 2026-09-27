using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerProgramTests
{

    [Test]
    [NonParallelizable]
    public async Task InvalidProjectedRequestStopsBeforeWritingRequest()
    {
        using var temporaryDirectory = new TempDirectory(
            "SharpProof.Worker.Test-");
        var directory = temporaryDirectory.FullName;
        var manifestPath = Path.Combine(directory, "compiler-manifest.json");
        var requestPath = Path.Combine(directory, "request.json");
        var resultPath = Path.Combine(directory, "result.json");
        var compilation = new CompilerCompilationSnapshot
        {
            ProjectDirectory = directory,
            AssemblyName = "Subject",
            AssemblyIdentity =
                "Subject, Version=1.0.0.0, Culture=neutral, " +
                "PublicKeyToken=null",
            TargetFramework = "net9.0",
            CompilerVersion = "1.0.0.0",
            CompilerMvid = Guid.NewGuid().ToString("D"),
            CSharpCompilerVersion = "1.0.0.0",
            CSharpCompilerMvid = Guid.NewGuid().ToString("D"),
            Options = new CompilerCompilationOptionsSnapshot
            {
                ResolverPolicy = CompilerResolverPolicy.EvidenceOnly
            }
        };
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var artifact = new CompilerManifestArtifact
        {
            Features = WorkerFeatureSet.All,
            Compilation = compilation,
            CompilationSha256 = CompilationFingerprint.ComputeSha256(compilation, []),
            Manifest = manifest
        };
        await File.WriteAllTextAsync(
            manifestPath,
            CompilerManifestArtifactJson.Serialize(artifact));

        var arguments = new[] {
            "verify",
            "--worker", typeof(SharpProofWorker).Assembly.Location,
            "--request", requestPath,
            "--result", resultPath,
            "--compiler-manifest", manifestPath,
            "--verify-policy", "advisory",
            "--assumption-policy", "allow",
            "--max-parallelism", "0"
        };
        var exitCode = await InvokeLauncherAsync(arguments);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(File.Exists(requestPath), Is.False);

        arguments[^1] = "1";
        var validExitCode = await InvokeLauncherAsync(arguments);
        Assert.That(validExitCode, Is.Zero);
        Assert.That(File.Exists(requestPath), Is.True);
        Assert.That(File.Exists(resultPath), Is.True);
    }

    [Test]
    public void NativeBackendLoadFailuresAreClassified()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerHost.IsBackendUnavailable(new DllNotFoundException()),
                Is.True);
            Assert.That(
                WorkerHost.IsBackendUnavailable(
                    new TypeInitializationException(
                        "Z3",
                        new EntryPointNotFoundException())),
                Is.True);
            Assert.That(
                WorkerHost.IsBackendUnavailable(new InvalidOperationException()),
                Is.False);
            Assert.That(
                WorkerHost.IsBackendUnavailable(new FileNotFoundException()),
                Is.False);
            Assert.That(
                WorkerHost.IsBackendUnavailable(new FileLoadException()),
                Is.False);
        }
    }

    private static Task<int> InvokeLauncherAsync(string[] arguments)
    {
        return SharpProof.Worker.Launcher.Program.Main(arguments);
    }
}
