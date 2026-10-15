using System.Globalization;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    [NonParallelizable]
    public async Task IsolatedLauncherTerminationPublishesActualCanceledResponse()
    {
        const string childVariable = "SHARPPROOF_WORKER_TERMINATION_CHILD";
        const string markerVariable = "SHARPPROOF_WORKER_TERMINATION_MARKER";
        const string tokenVariable = "SHARPPROOF_WORKER_TERMINATION_TOKEN";
        if (Environment.GetEnvironmentVariable(childVariable) == "1")
        {
            var marker = Environment.GetEnvironmentVariable(markerVariable)
                ?? throw new AssertionException("Child marker path is missing.");
            var token = Environment.GetEnvironmentVariable(tokenVariable)
                ?? throw new AssertionException("Child marker token is missing.");
            await VerifyTerminatedLauncherChildAsync(marker, token);
            await File.WriteAllTextAsync(marker + ".completed", token);
            return;
        }

        using var temporary = new TempDirectory("worker-termination-child-");
        var ready = Path.Combine(temporary.FullName, "ready");
        var identity = Guid.NewGuid().ToString("N");
        var startInfo = ProcessRunner.CreateStartInfo(Environment.CurrentDirectory, "dotnet", [
            "vstest", typeof(WorkerTests).Assembly.Location,
            "/TestCaseFilter:FullyQualifiedName=" + typeof(WorkerTests).FullName + "." +
                nameof(IsolatedLauncherTerminationPublishesActualCanceledResponse)
        ]);
        startInfo.Environment[childVariable] = "1";
        startInfo.Environment[markerVariable] = ready;
        startInfo.Environment[tokenVariable] = identity;
        using var cleanup = new CancellationTokenSource();
        var child = ProcessRunner.RunCapturedAsync(startInfo, cleanup.Token);
        try
        {
            while (!File.Exists(ready))
            {
                if (child.IsCompleted)
                {
                    var stopped = await child;
                    Assert.Fail("Child exited before registering the handler: " + stopped.CombinedOutput);
                }
                await Task.Delay(10);
            }
            var handshake = await File.ReadAllLinesAsync(ready);
            Assert.That(handshake, Has.Length.EqualTo(2));
            Assert.That(handshake[0], Is.EqualTo(identity));
            var childPid = int.Parse(handshake[1], CultureInfo.InvariantCulture);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(childPid, Is.GreaterThan(0));
                Assert.That(childPid, Is.Not.EqualTo(Environment.ProcessId));
                Assert.That(child.IsCompleted, Is.False);
            }
            var signal = await ProcessRunner.RunCapturedAsync(Environment.CurrentDirectory,
                "/bin/bash", "-c", "kill -TERM \"$1\"", "sharpproof-owned-child",
                childPid.ToString(CultureInfo.InvariantCulture));
            Assert.That(signal.ExitCode, Is.Zero, signal.CombinedOutput);
            var completed = await child;
            Assert.That(File.Exists(ready + ".completed"), Is.True, completed.CombinedOutput);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(completed.ExitCode, Is.Zero, completed.CombinedOutput);

                Assert.That(await File.ReadAllTextAsync(ready + ".completed"), Is.EqualTo(identity));
            }
        }
        finally
        {
            if (!child.IsCompleted)
            { await cleanup.CancelAsync(); }
            try
            { await child; }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
            { }
        }
    }

    private static async Task VerifyTerminatedLauncherChildAsync(string marker, string token)
    {
        using var project = TestProject.Create(TautologySource);
        var originalRequest = project.CreateRequest(cacheEnabled: false);
        var requestPath = Path.Combine(project.DirectoryPath, "terminated-request.json");
        var resultPath = Path.Combine(project.DirectoryPath, "terminated-result.json");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousError = Console.Error;
        using var diagnostic = new StringWriter();
        int exitCode;
        try
        {
            Console.SetError(diagnostic);
            exitCode = await Launcher.Program.RunMain([
                "verify", "--request", requestPath, "--result", resultPath,
                "--compiler-manifest", originalRequest.CompilerManifest.Path,
                "--verify-policy", "advisory", "--assumption-policy", "allow", "--cache-enabled", "false"
            ], async (request, cancellationToken) =>
            {
                using var worker = new SharpProofWorker(new SignalingDelayingBackend(started));
                var verification = worker.VerifyAsync(request, cancellationToken);
                await Task.WhenAny(started.Task, verification);
                Assert.That(started.Task.IsCompleted, Is.True, "The worker completed before backend start.");
                // The handler is registered before RunMain invokes this callback.
                // Only actual backend start permits publishing the child PID.
                await File.WriteAllLinesAsync(marker + ".pending", [token,
                    Environment.ProcessId.ToString(CultureInfo.InvariantCulture)], CancellationToken.None);
                File.Move(marker + ".pending", marker);
                return await verification;
            });
        }
        finally
        {
            Console.SetError(previousError);
        }
        var response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(resultPath))
            ?? throw new AssertionException("Termination did not produce a private response.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(4));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
            Assert.That(File.Exists(requestPath), Is.True);
            Assert.That(diagnostic.ToString(), Does.Contain("SharpProof worker run Canceled"));
        }
    }
}
