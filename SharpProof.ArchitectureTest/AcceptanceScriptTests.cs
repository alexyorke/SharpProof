using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class AcceptanceScriptTests
{
    [TestCase("canonical", true)]
    [TestCase("zero-restore", true)]
    [TestCase("nonzero-restore", true)]
    [TestCase("boundary-equality", true)]
    [TestCase("restore-failure", true)]
    [TestCase("phase-order", false)]
    [TestCase("phase-overlap", false)]
    [TestCase("before-start", false)]
    [TestCase("after-completion", false)]
    [TestCase("wrong-total", false)]
    public async Task AcceptanceTimelineIsExactAndRestoreOwned(
        string mutation,
        bool expectedSuccess)
    {
        var result = await RunAsync(
            TestRepository.FindRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "Test-SharpProofAcceptanceTimingFixtures.ps1"),
            "-Mutation",
            mutation);
        Assert.That(
            result.ExitCode == 0,
            Is.EqualTo(expectedSuccess),
            result.Output + Environment.NewLine + result.Error);
    }

    [Test]
    public async Task AcceptanceScriptOwnsRestoreInsideOuterTimeline()
    {
        var verify = await File.ReadAllTextAsync(Path.Combine(
            TestRepository.FindRoot(), "eng", "acceptance", "Verify.ps1"));
        var started = verify.IndexOf(
            "$timingStartedUtc =", StringComparison.Ordinal);
        var dotnetWrapper = verify.IndexOf(
            "SharpProof.ContainerExecution.psm1", StringComparison.Ordinal);
        var restore = verify.IndexOf(
            "Start-AcceptanceTimingPhase -Name 'restore'",
            StringComparison.Ordinal);
        var staticValidation = verify.IndexOf(
            "Start-AcceptanceTimingPhase -Name 'static-validation'",
            StringComparison.Ordinal);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dotnetWrapper, Is.GreaterThan(started));
            Assert.That(restore, Is.GreaterThan(dotnetWrapper));
            Assert.That(restore, Is.GreaterThan(started));
            Assert.That(staticValidation, Is.GreaterThan(restore));
            Assert.That(
                verify,
                Does.Contain("Test-AcceptanceTimingTimeline")
                    .And.Contain("Invoke-SharpProofRequiredDotnet"));
        }

        var dispatcher = await File.ReadAllTextAsync(Path.Combine(
            TestRepository.FindRoot(), "scripts", "Invoke-SharpProofContainer.ps1"));
        Assert.That(
            dispatcher,
            Does.Not.Contain("SHARPPROOF_ACCEPTANCE_RESTORE_MILLISECONDS"));
    }

    [TestCase(false, false, "passed", "SharpProof acceptance checks passed.")]
    [TestCase(true, false, "incomplete", "non-qualifying partial mode")]
    [TestCase(false, true, "incomplete", "non-qualifying partial mode")]
    [TestCase(true, true, "incomplete", "non-qualifying partial mode")]
    public async Task SkipModesCannotProduceQualifyingAcceptanceEvidence(
        bool skipBuild,
        bool skipTests,
        string expectedStatus,
        string expectedOutput)
    {
        using var temporary = new TempDirectory("SharpProof.Architecture.Acceptance-");
        var fixture = temporary.FullName;
        await InitializeRepositoryAsync(fixture);
        var harness = WriteHarness(fixture);
        var arguments = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            harness
        };
        if (skipBuild)
        {
            arguments.Add("-SkipBuild");
        }
        if (skipTests)
        {
            arguments.Add("-SkipTests");
        }

        var result = await RunAsync(
            fixture,
            "pwsh",
            [.. arguments]);
        var evidencePath = Path.Combine(
            fixture,
            "artifacts",
            "timings",
            "acceptance-release.json");

        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(File.Exists(evidencePath), Is.True, result.Output);
        using var evidence = JsonDocument.Parse(
            await File.ReadAllTextAsync(evidencePath));
        var root = evidence.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                root.GetProperty("status").GetString(),
                Is.EqualTo(expectedStatus));
            Assert.That(
                root.GetProperty("failure").GetString(),
                Is.Empty);
            Assert.That(result.Output, Does.Contain(expectedOutput));
            if (expectedStatus == "incomplete")
            {
                Assert.That(
                    result.Output,
                    Does.Not.Contain(
                        "SharpProof acceptance checks passed."));
            }
        }
    }

    private static string WriteHarness(string fixture)
    {
        var root = TestRepository.FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "acceptance",
            "Verify.ps1"));
        var prefixEnd = source.IndexOf(
            "Start-AcceptanceTimingPhase -Name 'restore'",
            StringComparison.Ordinal);
        Assert.That(prefixEnd, Is.GreaterThan(0));
        var completionStart = source.LastIndexOf(
            "$acceptanceStatus =",
            StringComparison.Ordinal);
        if (completionStart < 0)
        {
            completionStart = source.LastIndexOf(
                "Write-AcceptanceTimingEvidence -Status",
                StringComparison.Ordinal);
        }
        Assert.That(completionStart, Is.GreaterThan(prefixEnd));

        var acceptance = Path.Combine(fixture, "eng", "acceptance");
        Directory.CreateDirectory(acceptance);
        File.Copy(
            Path.Combine(root, "eng", "acceptance", "contract.json"),
            Path.Combine(acceptance, "contract.json"));
        var fixtureScripts = Path.Combine(fixture, "scripts");
        Directory.CreateDirectory(fixtureScripts);
        File.Copy(
            Path.Combine(
                root, "scripts", "SharpProof.FuzzEvidenceLifecycle.ps1"),
            Path.Combine(
                fixtureScripts, "SharpProof.FuzzEvidenceLifecycle.ps1"));
        File.Copy(
            Path.Combine(
                root, "scripts", "Assert-SharpProofFuzzRunnerResult.ps1"),
            Path.Combine(
                fixtureScripts, "Assert-SharpProofFuzzRunnerResult.ps1"));
        File.Copy(
            Path.Combine(
                root, "scripts", "SharpProof.ContainerExecution.psm1"),
            Path.Combine(
                fixtureScripts, "SharpProof.ContainerExecution.psm1"));
        var harnessPath = Path.Combine(acceptance, "VerifyHarness.ps1");
        var setup = """
            $contract = Get-Content -LiteralPath $contractPath -Raw |
                ConvertFrom-Json
            $testPhases = @(
                'semantic-tests',
                'package-tests',
                'fuzz',
                'corpus-and-performance')
            foreach ($name in @($contract.automation.acceptanceTimingPhases)) {
                $status = if (($SkipBuild -and $name -ceq 'build') -or
                    ($SkipTests -and $name -cin $testPhases)) {
                    'skipped'
                }
                else {
                    'passed'
                }
                Add-AcceptanceTimingPhase `
                    -Name ([string]$name) `
                    -ElapsedMilliseconds 0 `
                    -Status $status
            }

            """;
        File.WriteAllText(
            harnessPath,
            source[..prefixEnd] + setup + source[completionStart..],
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return harnessPath;
    }

    private static async Task InitializeRepositoryAsync(string repository)
    {
        await ArchitectureGitRepository.InitializeAsync(
            repository,
            "acceptance-script@example.invalid",
            "Acceptance Script Test");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "fixture.txt"),
            "fixture\n");
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "add",
            "--",
            "fixture.txt"));
        await AssertSuccessAsync(RunAsync(
            repository,
            "git",
            "commit",
            "-m",
            "fixture"));
    }

    private static async Task<ProcessRunnerResult> AssertSuccessAsync(
        Task<ProcessRunnerResult> operation)
    {
        var result = await operation;
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        return result;
    }

    private static Task<ProcessRunnerResult> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        return ProcessRunner.RunCapturedAsync(
            workingDirectory,
            fileName,
            arguments);
    }

}
