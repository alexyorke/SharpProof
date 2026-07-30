using NUnit.Framework;
using SharpProof.Gates.Performance;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class PerformanceGateTests
{
    [Test]
    public void LoadsFixedProtocolFromAcceptanceContract()
    {
        var contract = AcceptancePerformanceContract.Load(
            RepositoryLayout.FindRoot());

        using (Assert.EnterMultipleScope())
        {
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
    public void PackageBuildMedianAveragesTheMiddleEvenSamples()
    {
        var median = PackageBuildEstimator.Median([1, 9, 3, 5]);

        Assert.That(median, Is.EqualTo(4));
    }

    [Test]
    public void PackageBuildEstimatorUsesPairedRatiosAndBalancesOrder()
    {
        PackageBuildSample[] samples =
        [
            new(0, false, 100, 110),
            new(1, true, 10, 20),
            new(2, false, 10, 11),
            new(3, true, 100, 200)
        ];

        var statistics = PackageBuildEstimator.Estimate(samples);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                statistics.BaselineFirstMedianRatio,
                Is.EqualTo(1.1).Within(0.000_001));
            Assert.That(
                statistics.UnannotatedAdvisoryFirstMedianRatio,
                Is.EqualTo(2).Within(0.000_001));
            Assert.That(
                statistics.OrderBalancedMedianRatio,
                Is.EqualTo(Math.Sqrt(2.2)).Within(0.000_001));
            Assert.That(
                statistics.RawMedianRatio,
                Is.EqualTo(1.55).Within(0.000_001));
            Assert.That(
                statistics.P95Ratio,
                Is.EqualTo(2).Within(0.000_001));
            Assert.That(
                samples[0].Ratio,
                Is.EqualTo(1.1).Within(0.000_001));
            Assert.That(samples[1].Ratio, Is.EqualTo(2));
            Assert.That(
                samples[2].Ratio,
                Is.EqualTo(1.1).Within(0.000_001));
            Assert.That(samples[3].Ratio, Is.EqualTo(2));
        }
    }

    [Test]
    public void PackageBuildEstimatorCancelsReciprocalOrderBias()
    {
        PackageBuildSample[] samples =
        [
            new(0, false, 1, 0.6),
            new(1, true, 1, 2.4),
            new(2, false, 1, 0.6),
            new(3, true, 1, 2.4)
        ];

        var statistics = PackageBuildEstimator.Estimate(samples);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                statistics.OrderBalancedRatios.Length,
                Is.EqualTo(2));
            Assert.That(
                statistics.OrderBalancedRatios[0],
                Is.EqualTo(1.2).Within(0.000_001));
            Assert.That(
                statistics.OrderBalancedRatios[1],
                Is.EqualTo(1.2).Within(0.000_001));
            Assert.That(
                statistics.OrderBalancedMedianRatio,
                Is.EqualTo(1.2).Within(0.000_001));
            Assert.That(
                statistics.RawMedianRatio,
                Is.EqualTo(1.5).Within(0.000_001));
        }
    }

    [Test]
    public void PackageBuildEstimatorRetainsRawAndBalancedEvidence()
    {
        PackageBuildSample[] samples =
        [
            new(0, false, 1, 1),
            new(1, true, 1, 3),
            new(2, false, 1, 2),
            new(3, true, 1, 4),
            new(4, false, 1, 100),
            new(5, true, 1, 5)
        ];

        var statistics = PackageBuildEstimator.Estimate(samples);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                statistics.OrderBalancedMedianRatio,
                Is.EqualTo(Math.Sqrt(8)).Within(0.000_001));
            Assert.That(
                statistics.RawMedianRatio,
                Is.EqualTo(3.5));
            Assert.That(
                statistics.OrderBalancedRatios.Length,
                Is.EqualTo(3));
            Assert.That(
                statistics.OrderBalancedRatios[0],
                Is.EqualTo(Math.Sqrt(3)).Within(0.000_001));
            Assert.That(
                statistics.OrderBalancedRatios[1],
                Is.EqualTo(Math.Sqrt(8)).Within(0.000_001));
            Assert.That(
                statistics.OrderBalancedRatios[2],
                Is.EqualTo(Math.Sqrt(500)).Within(0.000_001));
        }
    }

    [Test]
    public void PackageBuildEstimatorRejectsUnbalancedExecutionOrders()
    {
        PackageBuildSample[] samples =
        [
            new(0, false, 1, 1),
            new(1, false, 1, 1)
        ];

        var exception = Assert.Throws<ArgumentException>(
            (Action)(() =>
                _ = PackageBuildEstimator.Estimate(samples)));

        Assert.That(exception!.Message, Does.Contain("balance"));
    }

    [Test]
    public void PackageBuildEstimatorRejectsNonAlternatingAdjacentOrders()
    {
        PackageBuildSample[] samples =
        [
            new(0, false, 1, 1),
            new(1, false, 1, 1),
            new(2, true, 1, 1),
            new(3, true, 1, 1)
        ];

        var exception = Assert.Throws<ArgumentException>(
            (Action)(() =>
                _ = PackageBuildEstimator.Estimate(samples)));

        Assert.That(exception!.Message, Does.Contain("opposite"));
    }

    [Test]
    public async Task PackageBuildSdkPinRetainsRepositoryIdentity()
    {
        var repositoryRoot = RepositoryLayout.FindRoot();
        var probeRoot = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Gates.Test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probeRoot);
        try
        {
            var identity = await PackageBuildSdkPin.PinAndValidateAsync(
                repositoryRoot,
                probeRoot,
                CancellationToken.None);
            var repositoryGlobalJson = await File.ReadAllBytesAsync(
                Path.Combine(repositoryRoot, "global.json"));
            var probeGlobalJson = await File.ReadAllBytesAsync(
                Path.Combine(probeRoot, "global.json"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    probeGlobalJson,
                    Is.EqualTo(repositoryGlobalJson));
                Assert.That(identity.ConfiguredVersion, Is.Not.Empty);
                Assert.That(identity.RollForward, Is.Not.Empty);
                Assert.That(identity.ResolvedVersion, Is.Not.Empty);
                Assert.That(
                    identity.GlobalJsonSha256,
                    Is.EqualTo(Convert.ToHexString(
                        SHA256.HashData(repositoryGlobalJson))));
            }
        }
        finally
        {
            Directory.Delete(probeRoot, recursive: true);
        }
    }

    [Test]
    public void EnabledAnalyzerRetentionLimitsAreEnforcedIndependently()
    {
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compilationOnly.Length, Is.EqualTo(1));
            Assert.That(compilationOnly[0], Does.Contain("compilation"));
            Assert.That(memoryOnly.Length, Is.EqualTo(1));
            Assert.That(memoryOnly[0], Does.Contain("memory"));
            Assert.That(both.Length, Is.EqualTo(2));
        }
    }

    [Test]
    public void RetainedMemoryLimitsAreEnforcedIndependently()
    {
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ratioOnly.Length, Is.EqualTo(1));
            Assert.That(ratioOnly[0], Does.Contain("ratio"));
            Assert.That(increaseOnly.Length, Is.EqualTo(1));
            Assert.That(increaseOnly[0], Does.Contain("increase"));
            Assert.That(both.Length, Is.EqualTo(2));
        }
    }

    [Test]
    public void AdvisoryMeasurementRunsTheAnalyzerAndStaysQuiet()
    {
        var measurement =
            PerformanceGate.MeasureUnannotatedAdvisoryAnalyzerBatch(
            "public static class Subject { public static int M() => 1; }",
            "UnannotatedAdvisoryProbe",
            iterations: 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.MeanMilliseconds, Is.GreaterThan(0));
            Assert.That(measurement.AnalyzerDriverRunCount, Is.EqualTo(3));
            Assert.That(measurement.DiagnosticCount, Is.Zero);
            Assert.That(measurement.AnalysisSessionCreateCount, Is.Zero);
        }
    }

    [Test]
    public void CallBearingAdvisoryMeasurementExercisesSemanticScreening()
    {
        var source =
            PerformanceGate.CreateCallBearingUnannotatedAdvisorySource(
                methodCount: 3);
        var measurement =
            PerformanceGate.MeasureUnannotatedAdvisoryAnalyzerBatch(
                source,
                "CallBearingUnannotatedAdvisoryProbe",
                iterations: 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source, Does.Contain("Normalize(value)"));
            Assert.That(source, Does.Contain("System.Math.Max"));
            Assert.That(
                source,
                Does.Not.Contain("SharpProof.Attributes"));
            Assert.That(
                measurement.AnalyzerDriverRunCount,
                Is.EqualTo(2));
            Assert.That(measurement.DiagnosticCount, Is.Zero);
            Assert.That(
                measurement.AnalysisSessionCreateCount,
                Is.EqualTo(2));
        }
    }

    [Test]
    public void AdvisoryPackagePolicyRunsAnalyzerAndOmitsVerifierWork()
    {
        PerformanceGate.ValidateAdvisoryPackagePolicy(
            RepositoryLayout.FindRoot());
    }

    [Test]
    public void AdvisoryPolicyRejectsAWidenedVerifierCondition()
    {
        var root = RepositoryLayout.FindRoot();
        var portableProps = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.props"));
        var portableTargets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.targets"));
        var verifierProps = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Verifier.Win-x64",
            "buildTransitive",
            "SharpProof.Verifier.Win-x64.props"));
        var verifierTargets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Verifier.Win-x64",
            "buildTransitive",
            "SharpProof.Verifier.Win-x64.targets"));
        var verifier = verifierTargets.Descendants("Target").Single(target =>
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
                    portableProps,
                    portableTargets,
                    verifierProps,
                    verifierTargets)));
    }

    [Test]
    public void AdvisoryPolicyRejectsVerifierConditionWithoutOptIn()
    {
        var root = RepositoryLayout.FindRoot();
        var portableProps = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.props"));
        var portableTargets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Package",
            "buildTransitive",
            "SharpProof.targets"));
        var verifierProps = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Verifier.Win-x64",
            "buildTransitive",
            "SharpProof.Verifier.Win-x64.props"));
        var verifierTargets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Verifier.Win-x64",
            "buildTransitive",
            "SharpProof.Verifier.Win-x64.targets"));
        var verifier = verifierTargets.Descendants("Target").Single(target =>
            string.Equals(
                (string?)target.Attribute("Name"),
                "SharpProofVerify",
                StringComparison.Ordinal));
        verifier.SetAttributeValue(
            "Condition",
            ((string?)verifier.Attribute("Condition"))?.Replace(
                "'$(SharpProofVerify)' == 'true' AND ",
                string.Empty,
                StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(
            (Action)(() =>
                PerformanceGate.ValidateAdvisoryPackagePolicy(
                    portableProps,
                    portableTargets,
                    verifierProps,
                    verifierTargets)));
    }

    [Test]
    [Category("Performance")]
    [NonParallelizable]
    public async Task ForcedTerminationDeadlineIsStableAcrossLaunches()
    {
        var root = RepositoryLayout.FindRoot();
        var contract = AcceptancePerformanceContract.Load(root);
        for (var sample = 0; sample < 5; sample++)
        {
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
    [Category("Performance")]
    [NonParallelizable]
    public async Task ReleasePerformanceContractPasses()
    {
        var result = await PerformanceGate.RunAsync(
            RepositoryLayout.FindRoot());

        Assert.That(
            result.Failures,
            Is.Empty,
            string.Join(Environment.NewLine, result.Failures));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Passed, Is.True);
            Assert.That(
                result.UnannotatedAdvisoryAnalyzerDriverRunCount,
                Is.EqualTo(1));
            Assert.That(
                result.UnannotatedAdvisoryAnalysisSessionCreateCount,
                Is.EqualTo(1));
            Assert.That(result.BaselineRetainedBytes, Is.GreaterThan(0));
            Assert.That(
                result.UnannotatedAdvisoryRetainedBytes,
                Is.GreaterThan(0));
            Assert.That(
                result.PackageBuildEstimatorVersion,
                Is.EqualTo(PackageBuildEstimator.Version));
            Assert.That(
                result.PackageBuildSamples.Length,
                Is.EqualTo(30));
            Assert.That(
                result.OrderBalancedRatios.Length,
                Is.EqualTo(15));
            Assert.That(
                result.PackageBuildSamples.Count(
                    static sample => sample.UnannotatedAdvisoryFirst),
                Is.EqualTo(15));
            Assert.That(
                result.PackageBuildSdk.ResolvedVersion,
                Is.Not.Empty);
            Assert.That(
                result.PackageBuildSdk.GlobalJsonSha256,
                Has.Length.EqualTo(64));
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
