using System.Reflection;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerProgramTests
{
    [Test]
    public async Task DirectInvocationRequiresContainmentStartBarrier()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Worker.Test",
            Guid.NewGuid().ToString("N"));
        var resultPath = Path.Combine(directory, "result.json");

        var exitCode = await Program.Main([
            "verify",
            "--request",
            Path.Combine(directory, "request.json"),
            "--result",
            resultPath
        ]);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(File.Exists(resultPath), Is.False);
    }

    [Test]
    [NonParallelizable]
    public async Task InvalidProjectedRequestDisposesRuntimeSnapshotBeforeReturning()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Worker.Test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var manifestPath = Path.Combine(directory, "compiler-manifest.json");
        var requestPath = Path.Combine(directory, "request.json");
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            var compilation = new CompilerCompilationSnapshot
            {
                ProjectDirectory = directory,
                AssemblyName = "Subject",
                AssemblyIdentity = "Subject, Version=1.0.0.0",
                TargetFramework = "net9.0",
                CompilerVersion = "SharpProof.Test",
                CompilerMvid = Guid.NewGuid().ToString("D"),
                CSharpCompilerVersion = "SharpProof.Test",
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
                CompilationSha256 = CompilationFingerprint.ComputeSha256(compilation),
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
            Assert.That(validExitCode, Is.EqualTo(3));
            Assert.That(File.Exists(requestPath), Is.True);
            Assert.That(File.Exists(resultPath), Is.True);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void NativeBackendLoadFailuresAreClassified()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Program.IsBackendUnavailable(new DllNotFoundException()),
                Is.True);
            Assert.That(
                Program.IsBackendUnavailable(
                    new TypeInitializationException(
                        "Z3",
                        new EntryPointNotFoundException())),
                Is.True);
            Assert.That(
                Program.IsBackendUnavailable(new InvalidOperationException()),
                Is.False);
        }
    }

    private static async Task<int> InvokeLauncherAsync(string[] arguments)
    {
        var launcherProgram = Assembly.Load("SharpProof.Worker.Launcher")
            .GetType("SharpProof.Worker.Launcher.Program", throwOnError: true)!;
        var main = launcherProgram.GetMethod(
            "Main",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return await (Task<int>)main.Invoke(null, [arguments])!;
    }
}
