using NUnit.Framework;
using SharpProof.Gates.Performance;
using System.Security.Cryptography;
using System.Text;
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
            Assert.That(contract.SmokeWarmups, Is.EqualTo(1));
            Assert.That(contract.SmokeSamples, Is.EqualTo(4));
            Assert.That(contract.SmokeMaximumRatio, Is.EqualTo(2.0));
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
    public void WorkerCancellationMeasurementExcludesWarmups()
    {
        var measured = WorkerPerformanceProbe.SelectMeasuredLatencies(
            [1d, 2d, 10d, 11d],
            warmups: 2,
            samples: 2);

        Assert.That(measured, Is.EqualTo([10d, 11d]));
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
            Assert.That(samples[0].BaselineMilliseconds, Is.EqualTo(100));
            Assert.That(
                samples[0].UnannotatedAdvisoryMilliseconds,
                Is.EqualTo(110));
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
    public void PackageBuildSamplesRejectInvalidTimingEvidence()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => _ = new PackageBuildSample(
                    0,
                    false,
                    0,
                    1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => _ = new PackageBuildSample(
                    0,
                    false,
                    1,
                    double.NaN)));
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => _ = new PackageBuildSample(
                    0,
                    false,
                    double.Epsilon,
                    double.MaxValue)));
        }
    }

    [Test]
    public void PackageBuildEstimatorRejectsIncompleteNumericEvidence()
    {
        var noncontiguous = new[] {
            new PackageBuildSample(1, false, 1, 1),
            new PackageBuildSample(2, true, 1, 1)
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(
                (Action)(() =>
                    _ = PackageBuildEstimator.Estimate(
                        Array.Empty<PackageBuildSample>())));
            Assert.Throws<ArgumentException>(
                (Action)(() =>
                    _ = PackageBuildEstimator.Estimate(noncontiguous)));
            Assert.Throws<ArgumentException>(
                (Action)(() =>
                    _ = PackageBuildEstimator.Median(
                        Array.Empty<double>())));
            Assert.Throws<ArgumentException>(
                (Action)(() =>
                    _ = PackageBuildEstimator.Median([0])));
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
#pragma warning disable CA1308
                    Is.EqualTo(Convert.ToHexString(
                        SHA256.HashData(repositoryGlobalJson))
                        .ToLowerInvariant()));
#pragma warning restore CA1308
            }
        }
        finally
        {
            Directory.Delete(probeRoot, recursive: true);
        }
    }

    [Test]
    public async Task PackageBuildSdkPinAcceptsGlobalJsonCommentsAndBom()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Gates.Test",
            Guid.NewGuid().ToString("N"));
        var probeRoot = Path.Combine(root, "probe");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(probeRoot);
        var globalJson =
            "// SDK pin used by the performance gate\n" +
            "{\n" +
            "  \"sdk\": {\n" +
            "    \"version\": \"9.0.316\",\n" +
            "    \"rollForward\": \"disable\",\n" +
            "  },\n" +
            "}\n";
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(globalJson);
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(root, "global.json"),
                bytes);

            var identity = await PackageBuildSdkPin.PinAndValidateAsync(
                root,
                probeRoot,
                CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(identity.ConfiguredVersion, Is.EqualTo("9.0.316"));
                Assert.That(identity.RollForward, Is.EqualTo("disable"));
                Assert.That(
                    await File.ReadAllBytesAsync(
                        Path.Combine(probeRoot, "global.json")),
                    Is.EqualTo(bytes));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
            Assert.That(measurement.ApiSpecCreateCount, Is.Zero);
            Assert.That(measurement.EffectAnalysisCreateCount, Is.Zero);
        }
    }

    [Test]
    public void CallBearingAdvisoryMeasurementSkipsUnneededSemanticScreening()
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
                Is.Zero);
            Assert.That(measurement.ApiSpecCreateCount, Is.Zero);
            Assert.That(measurement.EffectAnalysisCreateCount, Is.Zero);
        }
    }

    [Test]
    public void AdvisoryPackagePolicyRunsAnalyzerAndOmitsVerifierWork()
    {
        PerformanceGate.ValidateAdvisoryPackagePolicy(
            RepositoryLayout.FindRoot());
    }

    [Test]
    public void AdvisoryPolicyRejectsSubstitutedAnalyzerEntryPoint()
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
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.props"));
        var verifierTargets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.targets"));
        var entryPoint = portableTargets.Descendants("Analyzer")
            .Single(analyzer => string.Equals(
                analyzer.Element("SharpProofAnalyzerRole")?.Value,
                "EntryPoint",
                StringComparison.Ordinal));
        entryPoint.SetAttributeValue(
            "Include",
            "$(_SharpProofContractForGeneratorPath)");

        Assert.Throws<InvalidDataException>(
            (Action)(() =>
                PerformanceGate.ValidateAdvisoryPackagePolicy(
                    portableProps,
                    portableTargets,
                    verifierProps,
                    verifierTargets)));
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
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.props"));
        var verifierTargets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.targets"));
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
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.props"));
        var verifierTargets = XDocument.Load(Path.Combine(
            root,
            "SharpProof.Verifier",
            "buildTransitive",
            "SharpProof.Verifier.targets"));
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
    public void WorkerProbeUsesOnlyItsActiveBuildConfiguration()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.PerformanceProbe." + Guid.NewGuid().ToString("N"));
        var configuration = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)).Parent!.Name;
        try
        {
            var project = Path.Combine(
                root, "SharpProof.Worker", "bin", "Release", "net9.0");
            Directory.CreateDirectory(project);
            File.WriteAllText(
                Path.Combine(project, "SharpProof.Worker.dll"), "stale");

            Assert.Throws<FileNotFoundException>((Action)(() =>
                WorkerPerformanceProbe.FindBuiltAssembly(
                    root, "SharpProof.Worker")));

            var active = Path.Combine(
                root, "SharpProof.Worker", "bin", configuration, "net9.0");
            Directory.CreateDirectory(active);
            var expected = Path.Combine(active, "SharpProof.Worker.dll");
            File.WriteAllText(expected, "active");

            Assert.That(
                WorkerPerformanceProbe.FindBuiltAssembly(
                    root, "SharpProof.Worker"),
                Is.EqualTo(expected));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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
        Assert.That(result.Passed, Is.True);
        AssertProtocolEvidence(result);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.CancellationP95Milliseconds,
                Is.LessThanOrEqualTo(250));
            Assert.That(
                result.ForcedTerminationMilliseconds,
                Is.LessThanOrEqualTo(1000));
        }
    }

    [Test]
    [Category("Coverage")]
    [NonParallelizable]
    public async Task ReleasePerformanceProtocolProducesStructuralEvidence()
    {
        var root = RepositoryLayout.FindRoot();
        var smoke = await PerformanceGate.RunSmokeAsync(root);
        var result = await PerformanceGate.RunStructuralCoverageAsync(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(smoke.Passed, Is.True);
            Assert.That(smoke.Failures, Is.Empty);
            Assert.That(smoke.PackageBuildSamples, Has.Length.EqualTo(4));
            Assert.That(smoke.ForcedTerminationMilliseconds, Is.Positive);
            Assert.That(result.Warmups, Is.EqualTo(1));
            Assert.That(result.Samples, Is.EqualTo(2));
            Assert.That(result.IdeEdits, Is.EqualTo(2));
        }
        AssertProtocolEvidence(result, expectedSamples: 2);
    }

    private static void AssertProtocolEvidence(
        PerformanceGateResult result,
        int expectedSamples = 30)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.UnannotatedAdvisoryAnalyzerDriverRunCount,
                Is.EqualTo(1));
            Assert.That(
                result.UnannotatedAdvisoryAnalysisSessionCreateCount,
                Is.Zero);
            Assert.That(result.BaselineRetainedBytes, Is.GreaterThan(0));
            Assert.That(
                result.UnannotatedAdvisoryRetainedBytes,
                Is.GreaterThan(0));
            Assert.That(
                result.PackageBuildEstimatorVersion,
                Is.EqualTo(PackageBuildEstimator.Version));
            Assert.That(
                result.PackageBuildSamples.Length,
                Is.EqualTo(expectedSamples));
            Assert.That(
                result.OrderBalancedRatios.Length,
                Is.EqualTo(expectedSamples / 2));
            Assert.That(
                result.PackageBuildSamples.Count(
                    static sample => sample.UnannotatedAdvisoryFirst),
                Is.EqualTo(expectedSamples / 2));
            Assert.That(
                result.PackageBuildSdk.ResolvedVersion,
                Is.Not.Empty);
            Assert.That(
                result.PackageBuildSdk.GlobalJsonSha256,
                Has.Length.EqualTo(64));
            Assert.That(result.EnabledRetainedCompilationCount, Is.Zero);
            Assert.That(
                result.EnabledRetainedMemoryIncreaseMiB,
                Is.GreaterThanOrEqualTo(0));
            Assert.That(result.IdeDiagnosticReplayFailureCount, Is.Zero);
            Assert.That(
                result.CancellationP95Milliseconds,
                Is.GreaterThan(0));
            Assert.That(
                result.ForcedTerminationMilliseconds,
                Is.GreaterThan(0));
        }
    }
}
