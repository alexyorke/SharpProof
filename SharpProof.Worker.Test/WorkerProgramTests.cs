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
                "--start-stdin",
                "--parent-pid",
                "1"
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
    public async Task DirectInvocationRejectsMalformedPathsWithoutThrowing()
    {
        var exitCode = await Program.Main([
            "verify",
            "--request", "request\0.json",
            "--result", "result.json",
            "--start-stdin", "--parent-pid", "1"
        ]);

        Assert.That(exitCode, Is.EqualTo(2));
    }

    [Test]
    public async Task DirectInvocationWritesFailureForMalformedRequest()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The direct worker is supported only in the Linux container.");
        }

        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SharpProof-worker-malformed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var requestPath = Path.Combine(directory, "request.json");
        var resultPath = Path.Combine(directory, "result.json");
        try
        {
            await File.WriteAllTextAsync(requestPath, "{");
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            using var process = LinuxWorkerProcess.Start(
                host,
                [typeof(SharpProofWorker).Assembly.Location,
                    "verify", "--request", requestPath,
                    "--result", resultPath, "--start-stdin"],
                directory);
            var completion = process.WaitForExit(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(1));

            var response = WorkerProtocolJson.DeserializeResponse(
                await File.ReadAllTextAsync(resultPath))!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(completion.Kind, Is.EqualTo(LinuxWorkerCompletionKind.Exited));
                Assert.That(completion.ExitCode, Is.Zero);
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
    public async Task ParentDeathKillsAWorkerBlockedBeforeStartupRelease()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The direct worker is supported only in the Linux container.");
        }

        var directory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SharpProof-worker-parent-death-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var requestPath = Path.Combine(directory, "request.json");
        var resultPath = Path.Combine(directory, "result.json");
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            "dotnet";
        const string script = """
            "$1" "$2" verify --request "$3" --result "$4" --start-stdin --parent-pid "$$" <&0 &
            child="$!"
            sleep 2
            printf '%s\n' "$child"
            sleep 300
            """;
        using var parent = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        parent.StartInfo.ArgumentList.Add("-c");
        parent.StartInfo.ArgumentList.Add(script);
        parent.StartInfo.ArgumentList.Add("sharpproof-parent");
        parent.StartInfo.ArgumentList.Add(host);
        parent.StartInfo.ArgumentList.Add(typeof(SharpProofWorker).Assembly.Location);
        parent.StartInfo.ArgumentList.Add(requestPath);
        parent.StartInfo.ArgumentList.Add(resultPath);

        var childProcessId = 0;
        var parentStarted = false;
        try
        {
            parentStarted = parent.Start();
            Assert.That(parentStarted, Is.True);
            var childText = await parent.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(
                int.TryParse(childText, out childProcessId),
                Is.True);
            Assert.That(
                Directory.Exists($"/proc/{childProcessId}"),
                Is.True,
                "The worker exited before its parent-death boundary was tested.");

            parent.Kill();
            await parent.WaitForExitAsync();
            var terminated = SpinWait.SpinUntil(
                () => !Directory.Exists($"/proc/{childProcessId}"),
                TimeSpan.FromSeconds(5));
            Assert.That(
                terminated,
                Is.True,
                "The blocked worker survived termination of its direct parent.");
        }
        finally
        {
            if (parentStarted)
            {
                await parent.StandardInput.DisposeAsync();
                if (!parent.HasExited)
                {
                    parent.Kill();
                    await parent.WaitForExitAsync();
                }
            }
            if (childProcessId > 0 &&
                Directory.Exists($"/proc/{childProcessId}"))
            {
                using var child = Process.GetProcessById(childProcessId);
                child.Kill();
                await child.WaitForExitAsync();
            }
            Directory.Delete(directory, recursive: true);
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
