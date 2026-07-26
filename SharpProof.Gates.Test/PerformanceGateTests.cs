using NUnit.Framework;
using SharpProof.Gates.Performance;
using System.Xml.Linq;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class PerformanceGateTests {
    [Test]
    public void LoadsFixedProtocolFromAcceptanceContract() {
        var contract = AcceptancePerformanceContract.Load(
            RepositoryLayout.FindRoot());

        using (Assert.EnterMultipleScope()) {
            Assert.That(contract.Warmups, Is.EqualTo(5));
            Assert.That(contract.Samples, Is.EqualTo(30));
            Assert.That(contract.IdeEdits, Is.EqualTo(200));
            Assert.That(contract.MaximumMedianRatio, Is.EqualTo(1.10));
            Assert.That(contract.MaximumP95Ratio, Is.EqualTo(1.20));
            Assert.That(contract.MaximumRetainedMemoryRatio, Is.EqualTo(1.05));
            Assert.That(contract.MaximumRetainedMemoryIncreaseMiB, Is.EqualTo(32));
            Assert.That(contract.MaximumEnabledRetainedCompilations, Is.Zero);
            Assert.That(
                contract.MaximumEnabledRetainedMemoryIncreaseMiB,
                Is.EqualTo(32));
            Assert.That(contract.IdeEditP95Milliseconds, Is.EqualTo(100));
            Assert.That(contract.IdeEditMaximumMilliseconds, Is.EqualTo(250));
            Assert.That(contract.CancellationP95Milliseconds, Is.EqualTo(250));
            Assert.That(contract.ForcedTerminationMilliseconds, Is.EqualTo(1000));
        }
    }

    [Test]
    public void EnabledAnalyzerRetentionLimitsAreEnforcedIndependently() {
        var contract = AcceptancePerformanceContract.Load(
            RepositoryLayout.FindRoot());

        var compilationOnly =
            PerformanceGate.EvaluateEnabledAnalyzerRetentionLimits(
                contract.MaximumEnabledRetainedCompilations + 1,
                contract.MaximumEnabledRetainedMemoryIncreaseMiB,
                contract);
        var memoryOnly =
            PerformanceGate.EvaluateEnabledAnalyzerRetentionLimits(
                contract.MaximumEnabledRetainedCompilations,
                contract.MaximumEnabledRetainedMemoryIncreaseMiB + 1,
                contract);
        var both =
            PerformanceGate.EvaluateEnabledAnalyzerRetentionLimits(
                contract.MaximumEnabledRetainedCompilations + 1,
                contract.MaximumEnabledRetainedMemoryIncreaseMiB + 1,
                contract);

        using (Assert.EnterMultipleScope()) {
            Assert.That(compilationOnly.Length, Is.EqualTo(1));
            Assert.That(compilationOnly[0], Does.Contain("compilation"));
            Assert.That(memoryOnly.Length, Is.EqualTo(1));
            Assert.That(memoryOnly[0], Does.Contain("memory"));
            Assert.That(both.Length, Is.EqualTo(2));
        }
    }

    [Test]
    public void RetainedMemoryLimitsAreEnforcedIndependently() {
        var contract = AcceptancePerformanceContract.Load(
            RepositoryLayout.FindRoot());

        var ratioOnly = PerformanceGate.EvaluateRetainedMemoryLimits(
            contract.MaximumRetainedMemoryRatio + 0.01,
            contract.MaximumRetainedMemoryIncreaseMiB,
            contract);
        var increaseOnly = PerformanceGate.EvaluateRetainedMemoryLimits(
            contract.MaximumRetainedMemoryRatio,
            contract.MaximumRetainedMemoryIncreaseMiB + 1,
            contract);
        var both = PerformanceGate.EvaluateRetainedMemoryLimits(
            contract.MaximumRetainedMemoryRatio + 0.01,
            contract.MaximumRetainedMemoryIncreaseMiB + 1,
            contract);

        using (Assert.EnterMultipleScope()) {
            Assert.That(ratioOnly.Length, Is.EqualTo(1));
            Assert.That(ratioOnly[0], Does.Contain("ratio"));
            Assert.That(increaseOnly.Length, Is.EqualTo(1));
            Assert.That(increaseOnly[0], Does.Contain("increase"));
            Assert.That(both.Length, Is.EqualTo(2));
        }
    }

    [Test]
    public void AdvisoryMeasurementRunsTheAnalyzerAndStaysQuiet() {
        var measurement = PerformanceGate.MeasureDefaultOffAnalyzerBatch(
            "public static class Subject { public static int M() => 1; }",
            "DefaultOffProbe",
            iterations: 3);

        using (Assert.EnterMultipleScope()) {
            Assert.That(measurement.MeanMilliseconds, Is.GreaterThan(0));
            Assert.That(measurement.AnalyzerDriverRunCount, Is.EqualTo(3));
            Assert.That(measurement.DiagnosticCount, Is.Zero);
            Assert.That(measurement.AnalysisSessionCreateCount, Is.EqualTo(3));
        }
    }

    [Test]
    public void AdvisoryPackagePolicyRunsAnalyzerAndOmitsVerifierWork() =>
        PerformanceGate.ValidateAdvisoryPackagePolicy(
            RepositoryLayout.FindRoot());

    [Test]
    public void AdvisoryPolicyRejectsAWidenedVerifierCondition() {
        var root = RepositoryLayout.FindRoot();
        var props = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.props"));
        var targets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.targets"));
        var verifier = targets.Descendants("Target").Single(target =>
            string.Equals(
                (string?)target.Attribute("Name"),
                "SharpProofVerify",
                StringComparison.Ordinal));
        verifier.SetAttributeValue(
            "Condition",
            (string?)verifier.Attribute("Condition") +
            " OR 'true' == 'true'");

        Assert.Throws<InvalidDataException>(
            (Action)(() =>
                PerformanceGate.ValidateAdvisoryPackagePolicy(
                    props,
                    targets)));
    }

    [Test]
    public async Task ForcedTerminationDeadlineIsStableAcrossLaunches() {
        var root = RepositoryLayout.FindRoot();
        var contract = AcceptancePerformanceContract.Load(root);
        for (var sample = 0; sample < 5; sample++) {
            var elapsed =
                await WorkerPerformanceProbe.MeasureForcedTerminationAsync(
                    root,
                    contract);
            Assert.That(
                elapsed,
                Is.LessThanOrEqualTo(
                    contract.ForcedTerminationMilliseconds),
                $"Sample {sample + 1}: {elapsed:F3} ms");
        }
    }

    [Test]
    public async Task ReleasePerformanceContractPasses() {
        var result = await PerformanceGate.RunAsync(
            RepositoryLayout.FindRoot());

        Assert.That(
            result.Failures,
            Is.Empty,
            string.Join(Environment.NewLine, result.Failures));
        using (Assert.EnterMultipleScope()) {
            Assert.That(result.Passed, Is.True);
            Assert.That(
                result.DefaultOffAnalyzerDriverRunCount,
                Is.EqualTo(1));
            Assert.That(result.BaselineRetainedBytes, Is.GreaterThan(0));
            Assert.That(result.DefaultOffRetainedBytes, Is.GreaterThan(0));
            Assert.That(result.EnabledRetainedCompilationCount, Is.Zero);
            Assert.That(
                result.EnabledRetainedMemoryIncreaseMiB,
                Is.LessThanOrEqualTo(32));
            Assert.That(result.IdeDiagnosticReplayFailureCount, Is.Zero);
            Assert.That(
                result.CancellationP95Milliseconds,
                Is.LessThanOrEqualTo(250));
            Assert.That(
                result.ForcedTerminationMilliseconds,
                Is.LessThanOrEqualTo(1000));
        }
    }
}
