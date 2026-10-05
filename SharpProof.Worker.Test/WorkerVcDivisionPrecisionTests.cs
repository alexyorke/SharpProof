using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcDivisionPrecisionTests
{
    private static readonly JsonSerializerOptions EvidenceOptions = new() { WriteIndented = true };
    internal const string VariableSource = """
        using SharpProof.Attributes;
        public static class Subject { public static int Target(int x, int d) {
            Contract.Requires(d != 0);
            Contract.Ensures(Contract.Result<int>() == x / d);
            return x / d;
        } }
        """;

    [TestCase("constant")]
    [TestCase("variable")]
    public async Task OwnedDivisionQueryAndActualWorkerRecordComparablePrecision(string kind)
    {
        var source = kind == "constant"
            ? WorkerVcShadowSourceGateTests.Universe.Single(item => item.Name == "general-division-budget").Source
            : VariableSource;
        var trials = new List<DivisionTrial>();
        string[] structure = [];
        for (var trial = 0; trial < 3; trial++)
        {
            var endToEnd = Stopwatch.StartNew();
            using var project = new ShadowTestProject(source);
            var candidate = PassiveCallableArtifactAdapter.Enroll(project.Snapshot.Callables.Single())!;
            Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _), Is.True);
            using var boundary = new CancellationTokenSource(WorkerBudgets.DefaultMethodWallTimeMilliseconds);
            using var solver = new PassiveCallableSolver(plan!);
            var entry = await Query(solver.VerifyEntryAsync);
            var normal = await Query(solver.VerifyNormalCompletionAsync);
            var ensures = await Query(token => solver.VerifyEnsuresAsync(0, token));
            var standaloneMilliseconds = endToEnd.Elapsed.TotalMilliseconds;
            var query = plan!.EnsuresQuery(0);
            if (trial == 0)
            {
                var printer = new IrPrinter(plan.Factory);
                structure = [.. query.Assumptions.Select(assumption => printer.Print(assumption.Predicate)),
                    "goal: " + printer.Print(query.Goal.Predicate)];
            }
            var workerWatch = Stopwatch.StartNew();
            using var workerProject = new ShadowTestProject(source);
            var workerSetupMilliseconds = workerWatch.Elapsed.TotalMilliseconds;
            using var worker = SharpProofWorker.Create(workerProject.Request.Budgets);
            var response = await worker.VerifyAsync(workerProject.Request, workerProject.Snapshot, CancellationToken.None);
            workerWatch.Stop();
            endToEnd.Stop();
            Assert.That(WorkerProtocolJson.Validate(response, workerProject.Bind().InputHash, response.Manifest).IsValid, Is.True);
            Assert.That(response.Errors, Is.Empty);
            var result = Postconditions(response).Single();
            Assert.That(entry.Outcome, Is.EqualTo(nameof(RefutedOutcome)));
            Assert.That(normal.Outcome, Is.EqualTo(nameof(RefutedOutcome)));
            Assert.That(ensures.Outcome, Is.EqualTo(nameof(ProvenOutcome)));
            Assert.That(ensures.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            trials.Add(new(entry, normal, ensures, solver.ConsumedResourceCount, query.Assumptions.Length, query.ModelVariables.Length,
                standaloneMilliseconds, workerWatch.Elapsed.TotalMilliseconds - workerSetupMilliseconds, workerWatch.Elapsed.TotalMilliseconds,
                result.Outcome, result.Reason));

            async Task<DivisionQuery> Query(Func<CancellationToken, Task<PassiveCallableCheckResult>> check)
            {
                var startResources = solver.ConsumedResourceCount;
                var watch = Stopwatch.StartNew();
                var result = await check(boundary.Token);
                return new(result.Outcome?.GetType().Name ?? "Skipped", result.Reason,
                    solver.ConsumedResourceCount - startResources, watch.Elapsed.TotalMilliseconds);
            }
        }
        var capture = Environment.GetEnvironmentVariable("SHARPPROOF_DIVISION_CAPTURE");
        if (capture is "baseline" or "improved")
        {
            var evidence = new
            {
                schemaVersion = 2,
                authority = "native",
                kind,
                queryRlimit = WorkerBudgets.DefaultQueryRlimit,
                methodRlimit = WorkerBudgets.DefaultMethodRlimit,
                trials,
                structure
            };
            var directory = Path.Combine(TestRepository.FindRoot(), "artifacts");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, $"phase2-division-{capture}-{kind}.json"),
                JsonSerializer.Serialize(evidence, EvidenceOptions));
        }
    }

    [Test]
    public async Task CopyBeforePostIncrementKeepsItsValueAcrossADiamond()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x, bool choose) {
                Contract.Ensures(Contract.Result<int>() == unchecked((choose ? Contract.Old(x) + 1 : Contract.Old(x) - 1) * 2));
                Contract.Ensures(Contract.Result<int>() == unchecked((x - 1) * 2));
                if (choose) x++; else x--;
                return unchecked(x + x++);
            } }
            """);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        Assert.That(response.Errors, Is.Empty);
        var posts = Postconditions(response);
        Assert.That(posts, Has.Length.EqualTo(2));
        Assert.That(posts.Select(result => result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(posts.Select(result => result.Reason), Is.All.EqualTo(WorkerClaimReason.None));
    }

    private static WorkerClaimResult[] Postconditions(WorkerVerifyResponse response)
    {
        var ids = response.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition)
            .Select(claim => claim.ClaimId).ToHashSet(StringComparer.Ordinal);
        return response.ClaimResults.Where(result => ids.Contains(result.ClaimId)).ToArray();
    }

    private sealed record DivisionQuery(string Outcome, WorkerClaimReason Reason, long Resources, double Milliseconds);
    private sealed record DivisionTrial(DivisionQuery Entry, DivisionQuery Normal, DivisionQuery Ensures,
        long MethodResources, int Facts, int ModelVariables, double StandaloneMilliseconds, double WorkerMilliseconds,
        double EndToEndMilliseconds, WorkerClaimOutcome NativeOutcome, WorkerClaimReason NativeReason);
}
