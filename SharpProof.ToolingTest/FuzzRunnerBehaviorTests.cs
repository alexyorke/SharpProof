using System.Collections.Immutable;
using System.Diagnostics;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Symbolic;
using SharpProof.Tools.Fuzz;
namespace SharpProof.Test;
[TestFixture]
public sealed class FuzzRunnerBehaviorTests {
    [Test]
    public async Task ProvenInterpolatedStringHandlerCallHasMatchingEnforcePureDiagnostic() {
        var outputDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "fuzz-handler-projection-" + Guid.NewGuid().ToString("N"));
        try {
            var summary = await FuzzRunner.RunAsync(new FuzzOptions {
                Iterations = 29,
                Seed = 20260722,
                OutputDirectory = outputDirectory,
                CheckpointEvery = 0,
                Parallelism = 1,
                RepeatAnalyzer = false
            });

            Assert.That(
                summary.Findings
                    .Where(static finding => finding.Family == "InterpolatedStringHandler")
                    .Select(static finding => finding.Category),
                Does.Not.Contain("enforce_pure_projection_mismatch"));
        }
        finally {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        }
    }
    [Test]
    public async Task ProvenDelegateCallHasMatchingEnforcePureDiagnosticAfterEarlierCases() {
        var outputDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "fuzz-projection-" + Guid.NewGuid().ToString("N"));
        try {
            var summary = await FuzzRunner.RunAsync(new FuzzOptions {
                Iterations = 136,
                Seed = 20260722,
                OutputDirectory = outputDirectory,
                CheckpointEvery = 0,
                Parallelism = 1,
                RepeatAnalyzer = false
            });

            Assert.That(
                summary.Findings
                    .Where(static finding => finding.Family == "DelegateCreation")
                    .Select(static finding => finding.Category),
                Does.Not.Contain("enforce_pure_projection_mismatch"));
        }
        finally {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
        }
    }
    [Test]
    public void FailedSymbolicResultsBecomeFindings() {
        var fuzzCase = new FuzzCase("failure", "failure", "class C { }", false, FuzzExpectation.Conservative());
        var result = new SharpProofAnalysisResult(
            new SharpProofTarget(SharpProofTargetKind.AllLines),
            SharpProofQueryStatus.Failed,
            null,
            [],
            [],
            null,
            [],
            [],
            new SharpProofError(
                "SPQ1201",
                SharpProofErrorCategory.Input,
                "invalid source",
                65,
                false,
                ImmutableDictionary<string, string>.Empty));
        var findings = FuzzRunner.Evaluate(fuzzCase, result, [], []);
        Assert.That(findings, Has.One.Matches<FuzzFinding>(finding =>
            finding.Category == "symbolic_analysis_failure" && finding.Details.Contains("error=SPQ1201")));
    }
    [Test]
    public void DuplicateAnalyzerExceptionsBecomeOneFinding() {
        var fuzzCase = new FuzzCase("exception", "exception", "class C { }", false, FuzzExpectation.Conservative());
        var result = new SharpProofAnalysisResult(
            new SharpProofTarget(SharpProofTargetKind.AllLines),
            SharpProofQueryStatus.Unknown,
            new MethodEffects(SharpProofEffect.Unknown, SharpProofCapability.None, [], [], []),
            [],
            [],
            null,
            [],
            [],
            null);
        var findings = FuzzRunner.Evaluate(fuzzCase, result, [], ["boom", "boom"]);
        Assert.That(findings.Count(static finding => finding.Category == "analyzer_exception"), Is.EqualTo(1));
    }
    [Test]
    public void DeadlineCancellationInterruptsAnInFlightCaseToken() {
        var started = Stopwatch.StartNew();
        using var cancellation = FuzzRunner.CreateDeadlineCancellation(
            DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(50),
            CancellationToken.None);
        Assert.That(cancellation, Is.Not.Null);
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await Task.Delay(TimeSpan.FromSeconds(5), cancellation!.Token));
        started.Stop();
        Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }
    [Test]
    public void DeadlineCancellationPreservesUserCancellation() {
        using var userCancellation = new CancellationTokenSource();
        userCancellation.Cancel();
        using var cancellation = FuzzRunner.CreateDeadlineCancellation(
            DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1),
            userCancellation.Token);
        Assert.That(cancellation!.IsCancellationRequested, Is.True);
    }
}
