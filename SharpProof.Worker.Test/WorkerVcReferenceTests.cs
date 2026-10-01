using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcReferenceTests
{
    [TestCase("string", "x != null && x.Length == 3", "return x.Length;", 3)]
    [TestCase("int[]", "x != null && x.Length == 3", "return x.Length;", 3)]
    [TestCase("object", "x != null", "object copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("object", "x == null", "object copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("int[]", "x != null && x.Length == 3", "int[] copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("int[]", "x == null", "int[] copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("string", "x == null", "try { return Length(x); } catch (System.NullReferenceException) when (++seen == 1) { return seen; } finally { seen += 10; }", 1)]
    public async Task ReferenceLengthProofAndRefutationSurviveArtifactAndCache(string type, string requires, string body, int expected)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target({{type}} x) {
                    Contract.Requires({{requires}});
                    Contract.Ensures(Contract.Result<int>() == {{expected}});
                    Contract.Ensures(Contract.Result<int>() == {{expected + 1}});
                    int seen = 0;
                    {{body}}
                }
                private static int Length(string value) { return value.Length; }
            }
            """;
        using var project = new ShadowTestProject(source, cacheEnabled: true);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(target.Total!.Parameters.Select(parameter => parameter.Entry)));
        var replay = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
        using var environment = new ShadowEnvironment("shadow");
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(invocation == 0 ? WorkerCacheStatus.Written : WorkerCacheStatus.Hit));
            var ordinals = project.Snapshot.CompilerManifest.Manifest.Claims.ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
            var rows = report!.Rows.Where(row => row.CallableId == target.Entry.CallableId).OrderBy(row => ordinals[row.ClaimId]).ToArray();
            Assert.That(rows.Select(row => row.NewOutcome), Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
            Assert.That(rows.All(row => row.Checked && row.TotalPresent), Is.True);
            Assert.That(report.SoundnessDisagreements, Is.Zero);
        }
    }

    [Test]
    public async Task NullLengthInPostconditionRetainsUndefinednessAbstention()
    {
        var subject = PassiveSourceSubject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(string x) {
                    Contract.Requires(x == null);
                    Contract.Ensures(x.Length == x.Length);
                    return 0;
                }
            }
            """);
        var candidate = subject.Enroll();
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.PostconditionMayBeUndefined));
    }

    [TestCase("bool Target(string x, string y) { Contract.Ensures(true); return x == y; }")]
    [TestCase("object Target(string x) { Contract.Ensures(true); return x; }")]
    [TestCase("int Target(int[] x) { Contract.Ensures(true); return x[0]; }")]
    [TestCase("int[] Target() { Contract.Ensures(true); return new int[1]; }")]
    public void UnsupportedReferenceOperationsRemainUnenrolled(string member)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class Subject { public static " + member + " }");
        Assert.That(artifact.Callables.Single().Total, Is.Null);
    }
}
