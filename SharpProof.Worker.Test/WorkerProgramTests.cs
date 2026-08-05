using System.Reflection;
using System.Runtime.InteropServices;
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
    public async Task DirectInvocationRejectsRequestResultAliasBeforeStartBarrier()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Worker.Test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var requestAndResultPath = Path.Combine(directory, "request.json");
        const string sentinel = "request-sentinel";
        await File.WriteAllTextAsync(requestAndResultPath, sentinel);
        try
        {
            var exitCode = await Program.Main([
                "verify",
                "--request",
                requestAndResultPath,
                "--result",
                requestAndResultPath,
                "--start-event",
                "missing-start-event"
            ]);

            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(
                await File.ReadAllTextAsync(requestAndResultPath),
                Is.EqualTo(sentinel));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DirectInvocationWritesFailureForMalformedRequest()
    {
        if (!OperatingSystem.IsWindows() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            Assert.Ignore("The direct worker is supported only on Windows x64.");
        }

        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SharpProof-worker-malformed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var requestPath = Path.Combine(directory, "request.json");
        var resultPath = Path.Combine(directory, "result.json");
        var eventName = "Local\\SharpProof.Worker.Test." +
            Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(requestPath, "{");
            using var startEvent = new EventWaitHandle(
                true, EventResetMode.ManualReset, eventName);

            var exitCode = await Program.Main([
                "verify",
                "--request", requestPath,
                "--result", resultPath,
                "--start-event", eventName
            ]);

            var response = WorkerProtocolJson.DeserializeResponse(
                await File.ReadAllTextAsync(resultPath))!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(
                    response.FailureReason,
                    Is.EqualTo(WorkerRunFailureReason.InvalidRequest));
                Assert.That(
                    response.Errors.Select(static error => error.Code),
                    Does.Contain("request.malformed"));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
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
