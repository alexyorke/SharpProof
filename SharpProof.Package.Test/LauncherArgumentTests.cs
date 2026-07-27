using NUnit.Framework;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class LauncherArgumentTests {
    [Test]
    public void UnknownOptionIsRejected() {
        string[] arguments = [
            .. ValidArguments(),
            "--project-wall-milliseconds",
            "100"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out _),
            Is.False);
    }

    [Test]
    public void CombinedTimeoutOverflowIsRejectedBeforeStartingWorker() {
        Action action = () => _ = Program.ComputeHardLimit(
            int.MaxValue,
            WorkerLauncherDefaults.TerminationGraceMilliseconds);

        Assert.That(action, Throws.TypeOf<OverflowException>());
    }

    [TestCase(1_000, 1_000, 1_900)]
    [TestCase(1_000, 100, 1_001)]
    public void CombinedTimeoutReservesCleanupTime(
        int projectMilliseconds, int graceMilliseconds, int expected) =>
        Assert.That(
            Program.ComputeHardLimit(projectMilliseconds, graceMilliseconds),
            Is.EqualTo(expected));

    [TestCase("input", "response.input_mismatch")]
    [TestCase("budgets", "response.budgets_mismatch")]
    public void BoundResultValidationRejectsMismatches(
        string mismatch, string expectedError) {
        var request = new WorkerVerifyRequest();
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        const string inputHash =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var response = new WorkerVerifyResponse {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = inputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary {
                    WorkerVersion = "test",
                    ApiSpecVersion = "test"
                },
                Budgets = new WorkerBudgets()
            }
        };
        if (mismatch == "input") response.InputHash = new('b', 64);
        else response.Summary.Budgets.QueryRlimit++;
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var error = Console.Error;
        using var capture = new StringWriter();
        try {
            Console.SetError(capture);
            File.WriteAllText(
                path, WorkerProtocolJson.SerializeResponse(response));
            Assert.That(
                Program.ValidateAndReport(
                    path, request, inputHash, manifest, out var valid),
                Is.EqualTo(3));
            Assert.That(valid, Is.False);
            Assert.That(capture.ToString(), Does.Contain(expectedError));
        }
        finally {
            Console.SetError(error);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string[] ValidArguments() => [
        "verify",
        "--worker", "worker.dll",
        "--request", "request.json",
        "--result", "result.json",
        "--compiler-manifest", "compiler-manifest.json",
        "--verify-policy", "advisory",
        "--assumption-policy", "allow"
    ];
}
