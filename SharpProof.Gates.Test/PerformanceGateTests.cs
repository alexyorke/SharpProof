using NUnit.Framework;
using SharpProof.Gates.Performance;
using SharpProof.Worker.Protocol;
using System.Diagnostics;
using System.Reflection;
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
    public void CooperativeTimeoutProbeRejectsPartialProtocolEvidence()
    {
        var location = new WorkerSourceLocation
        {
            Path = "Subject.cs",
            Start = 0,
            Length = 1,
            Line = 1,
            Column = 1
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [
                new WorkerCallableManifestEntry {
                    CallableId = "M:Subject.Verify()",
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [
                        WorkerSelectionReason.DiscoveredPostcondition
                    ],
                    Location = location,
                    ClaimIds = ["claim.verify.0"]
                }
            ],
            Claims = [
                new WorkerClaimManifestEntry {
                    ClaimId = "claim.verify.0",
                    CallableId = "M:Subject.Verify()",
                    Kind = WorkerClaimKind.Postcondition,
                    Evidence = WorkerClaimEvidence.DirectClause,
                    Location = location
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            RequestHash = WorkerProtocolVersions.EmptySha256,
            InputHash = WorkerProtocolVersions.EmptySha256,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.TimedOut,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult {
                    CallableId = "M:Subject.Verify()",
                    Coverage = WorkerCallableCoverage.Incomplete,
                    Reason = WorkerCallableCoverageReason.ProjectTimeout
                }
            ],
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = "claim.verify.0",
                    Outcome = WorkerClaimOutcome.Unknown,
                    Reason = WorkerClaimReason.ProjectTimeout
                }
            ],
            Summary = new WorkerVerificationSummary
            {
                CallableCount = 1,
                ClaimCount = 1,
                OutcomeCounts = [
                    new WorkerClaimOutcomeCount {
                        Outcome = WorkerClaimOutcome.Unknown,
                        Count = 1
                    }
                ],
                ReasonCounts = [
                    new WorkerClaimReasonCount {
                        Reason = WorkerClaimReason.ProjectTimeout,
                        Count = 1
                    }
                ],
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "test-worker",
                    ApiSpecVersion = "test-spec"
                },
                Budgets = new WorkerBudgets()
            }
        };
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        Assert.That(
            WorkerPerformanceProbe.IsCompleteProjectTimeout(response),
            Is.True);

        response.CallableResults = [];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.False);
            Assert.That(
                WorkerPerformanceProbe.IsCompleteProjectTimeout(response),
                Is.False);
        }
    }

    [Test]
    public async Task WorkerCancellationWaitObservesTheOuterBoundary()
    {
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var verification = new TaskCompletionSource<WorkerVerifyResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var registration = cancellation.Token.Register(() =>
            releaseCallback.Task.GetAwaiter().GetResult());
        using var boundary = new CancellationTokenSource(50);

        var wait = WorkerPerformanceProbe.CancelAndAwaitWorkerAsync(
            verification.Task,
            cancellation,
            boundary.Token);
        var completed = await Task.WhenAny(wait, Task.Delay(500));
        releaseCallback.SetResult();
        verification.SetResult(new WorkerVerifyResponse());
        var canceled = false;
        try
        {
            _ = await wait;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completed,
                Is.SameAs(wait),
                "The outer boundary must release a blocked cancellation wait.");
            Assert.That(canceled, Is.True);
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
        using var probe = new TempDirectory("SharpProof.Gates.Test-");
        var probeRoot = probe.FullName;
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
    public void EnabledRetentionAnalysisAcceptsAReusableAnalyzer()
    {
        var method = typeof(PerformanceGate).GetMethod(
            "AnalyzeEnabledCompilation",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Assert.That(
            method!.GetParameters().Count(static parameter =>
                typeof(Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer)
                    .IsAssignableFrom(parameter.ParameterType)),
            Is.EqualTo(1));
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

    [TestCase("conditional-override")]
    [TestCase("import")]
    [TestCase("executable-props-target")]
    public void AdvisoryPolicyRejectsUnevaluatedMsBuildBehavior(
        string mutation)
    {
        var sourceRoot = RepositoryLayout.FindRoot();
        using var temporary = new TempDirectory(
            "sharpproof-package-policy-");
        foreach (var (package, file) in new[]
             {
                     ("SharpProof.Package", "SharpProof.ConsumerContract.props"),
                     ("SharpProof.Package", "SharpProof.props"),
                         ("SharpProof.Package", "SharpProof.targets"),
                         ("SharpProof.Verifier", "SharpProof.Verifier.props"),
                         ("SharpProof.Verifier", "SharpProof.Verifier.targets")
                     })
        {
            var destination = Path.Combine(
                temporary.FullName,
                package,
                "buildTransitive");
            Directory.CreateDirectory(destination);
            File.Copy(
                Path.Combine(
                    sourceRoot,
                    package,
                    "buildTransitive",
                    file),
                Path.Combine(destination, file));
        }

        var portableRoot = Path.Combine(
            temporary.FullName,
            "SharpProof.Package",
            "buildTransitive");
        var path = mutation == "executable-props-target"
            ? Path.Combine(portableRoot, "SharpProof.props")
            : Path.Combine(portableRoot, "SharpProof.targets");
        var document = XDocument.Load(path);
        switch (mutation)
        {
            case "conditional-override":
                document.Root!.Add(new XElement(
                    "PropertyGroup",
                    new XAttribute(
                        "Condition",
                        "'$(SharpProofProfile)' == 'advisory'"),
                    new XElement("SharpProofProfile", "off")));
                break;
            case "import":
                new XDocument(
                    new XElement(
                        "Project",
                        new XElement(
                            "PropertyGroup",
                            new XElement(
                                "SharpProofProfile",
                                "off"))))
                    .Save(Path.Combine(portableRoot, "override.targets"));
                document.Root!.Add(new XElement(
                    "Import",
                    new XAttribute("Project", "override.targets")));
                break;
            case "executable-props-target":
                document.Root!.Add(new XElement(
                    "Target",
                    new XAttribute(
                        "Name",
                        "UnexpectedPackageWork"),
                    new XAttribute("BeforeTargets", "CoreCompile"),
                    new XElement(
                        "Error",
                        new XAttribute(
                            "Text",
                            "unexpected package work"))));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown mutation '{mutation}'.");
        }
        document.Save(path);

        Assert.Throws<InvalidDataException>((Action)(() =>
            PerformanceGate.ValidateAdvisoryPackagePolicy(
            temporary.FullName)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PackagePerformanceProbeRejectsUnusableAnalyzerEntryPoint(
        bool writeInvalidAnalyzer)
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var temporaryRoot = temporary.FullName;
        var project = CreatePerformanceProbeProject(
            temporaryRoot,
            "public static class Subject { }",
            RepositoryLayout.FindRoot(),
            importSharpProof: true);
        var projectDocument = XDocument.Load(project);
        var analyzerDirectory = Path.Combine(
            temporaryRoot,
            "unusable-analyzers");
        projectDocument.Descendants("SharpProofAnalyzerDirectory")
            .Single()
            .Value = analyzerDirectory;
        projectDocument.Save(project);
        if (writeInvalidAnalyzer)
        {
            Directory.CreateDirectory(analyzerDirectory);
            await File.WriteAllTextAsync(
                    Path.Combine(
                        analyzerDirectory,
                        "SharpProof.Analyzer.dll"),
                    "not an analyzer assembly")
                .ConfigureAwait(false);
        }

        _ = await RunPerformanceProbeDotnetAsync(
            project,
            restore: true,
            symbol: null);

        Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
            _ = await RunPerformanceProbeDotnetAsync(
                project,
                restore: false,
                symbol: "SHARPPROOF_MISSING_ANALYZER")));
    }

    [Test]
    public void PackagePerformanceProbeHasAnInternalWallTimeLimit()
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var temporaryRoot = temporary.FullName;
        var project = Path.Combine(temporaryRoot, "Hang.csproj");
        File.WriteAllText(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <Target Name="Hang" BeforeTargets="Restore">
                <Exec Command="sleep 30" />
              </Target>
            </Project>
            """);

        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<TimeoutException>((Func<Task>)(async () =>
            _ = await RunPerformanceProbeDotnetAsync(
                project,
                restore: true,
                symbol: null,
                timeout: TimeSpan.FromMilliseconds(250))));
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("restore"));
            Assert.That(exception.Message, Does.Contain("exceeded"));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(TimeSpan.FromSeconds(5)));
        }
    }

    [Test]
    public void AdvisoryPolicyRejectsSubstitutedAnalyzerEntryPoint()
    {
        var (portableProps, portableTargets, portableContract, verifierProps, verifierTargets) =
            LoadPolicyDocuments();
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
                    portableContract,
                    verifierProps,
                    verifierTargets)));
    }

    [Test]
    public void AdvisoryPolicyRejectsAWidenedVerifierCondition()
    {
        var (portableProps, portableTargets, portableContract, verifierProps, verifierTargets) =
            LoadPolicyDocuments();
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
                    portableContract,
                    verifierProps,
                    verifierTargets)));
    }

    [Test]
    public void AdvisoryPolicyRejectsVerifierConditionWithoutOptIn()
    {
        var (portableProps, portableTargets, portableContract, verifierProps, verifierTargets) =
            LoadPolicyDocuments();
        var verifier = verifierTargets.Descendants("Target").Single(target =>
            string.Equals(
                (string?)target.Attribute("Name"),
                "SharpProofVerify",
                StringComparison.Ordinal));
        verifier.SetAttributeValue(
            "Condition",
            ((string?)verifier.Attribute("Condition"))?.Replace(
                "'$(_SharpProofVerifyActive)' == 'true' AND ",
                string.Empty,
                StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(
            (Action)(() =>
                PerformanceGate.ValidateAdvisoryPackagePolicy(
                    portableProps,
                    portableTargets,
                    portableContract,
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
    [Explicit(
        "The release performance contract must run in isolation from a " +
        "Release build. Use the canonical performance command.")]
    [NonParallelizable]
    public async Task ReleasePerformanceContractPasses()
    {
        var assemblyConfiguration = typeof(PerformanceGate)
            .Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()
            ?.Configuration;
        Assume.That(
            assemblyConfiguration,
            Is.EqualTo("Release"),
            "Debug binaries cannot produce release performance evidence.");

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

    private static (
        XDocument PortableProps,
        XDocument PortableTargets,
        XDocument PortableContract,
        XDocument VerifierProps,
        XDocument VerifierTargets) LoadPolicyDocuments()
    {
        var root = RepositoryLayout.FindRoot();
        XDocument Load(string project, string file)
        {
            return XDocument.Load(
                Path.Combine(root, project, "buildTransitive", file));
        }
        return (
            Load("SharpProof.Package", "SharpProof.props"),
            Load("SharpProof.Package", "SharpProof.targets"),
            Load("SharpProof.Package", "SharpProof.ConsumerContract.props"),
            Load("SharpProof.Verifier", "SharpProof.Verifier.props"),
            Load("SharpProof.Verifier", "SharpProof.Verifier.targets"));
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

    private static string CreatePerformanceProbeProject(
        string directory,
        string source,
        string repositoryRoot,
        bool importSharpProof)
    {
        var method = typeof(PerformanceGate).GetMethod(
            "CreatePerformanceProbeProject",
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException(
                "Could not find the package performance probe factory.");
        return (string)method.Invoke(
            null,
            [directory, source, repositoryRoot, importSharpProof])!;
    }

    private static Task<double> RunPerformanceProbeDotnetAsync(
        string project,
        bool restore,
        string? symbol,
        TimeSpan? timeout = null)
    {
        var method = typeof(PerformanceGate).GetMethod(
            "RunDotnetAsync",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            [
                typeof(string),
                typeof(bool),
                typeof(string),
                typeof(TimeSpan),
                typeof(CancellationToken)
            ],
            modifiers: null) ??
            throw new InvalidOperationException(
                "Could not find the timeout-aware package performance " +
                "process runner.");
        return (Task<double>)method.Invoke(
            null,
            [project, restore, symbol, timeout ?? TimeSpan.FromMinutes(2),
                CancellationToken.None])!;
    }
}
