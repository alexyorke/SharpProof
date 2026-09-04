using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class ReleaseCoverageBaselineTests
{
    private const string FirstPreviewBaseline =
        "8347a70187a63cc7302b35e747d484747a929f6c";
    private static readonly string[] s_upstreamJobs =
    [
        "      - package",
        "      - container-verifier",
        "      - security"
    ];

    [Test]
    public void ReleaseQualificationImportsEveryUpstreamResult()
    {
        var root = TestRepository.FindRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "package-consumers.yml"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(workflow, Does.Not.Contain("always() &&"));
            Assert.That(workflow, Does.Not.Contain("actions/attest"));
            foreach (var job in s_upstreamJobs)
            {
                Assert.That(workflow, Does.Contain(job), job);
            }
            Assert.That(workflow, Does.Contain("portable-consumers"));
            Assert.That(workflow, Does.Not.Contain("minimum-sdk-consumer"));
        }
    }

    [Test]
    public void ReleaseQualificationInitializesBeforeSdkAndAvoidsStaleExitCodes()
    {
        var workflow = File.ReadAllText(Path.Combine(
            TestRepository.FindRoot(),
            ".github",
            "workflows",
            "package-consumers.yml"));
        var qualificationStart = workflow.IndexOf(
            "  release-qualification:",
            StringComparison.Ordinal);
        var qualificationEnd = workflow.IndexOf(
            "  publish-private-preview:",
            qualificationStart,
            StringComparison.Ordinal);
        Assert.That(qualificationStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(qualificationEnd, Is.GreaterThan(qualificationStart));
        var qualification = workflow[
            qualificationStart..qualificationEnd];
        var tagValidation = qualification.IndexOf(
            "Require an annotated exact tag in-container",
            StringComparison.Ordinal);
        var setup = qualification.IndexOf(
            "Build the pinned toolchain",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tagValidation, Is.GreaterThanOrEqualTo(0));
            Assert.That(setup, Is.LessThan(tagValidation));
            Assert.That(
                qualification,
                Does.Contain("tooling release-tag"));
            Assert.That(
                qualification,
                Does.Not.Contain("Setup required .NET SDKs"));
            Assert.That(
                qualification.Split(
                    "docker compose run --rm tooling",
                    StringSplitOptions.None),
                Has.Length.GreaterThanOrEqualTo(5));
            Assert.That(
                qualification,
                Does.Contain("tooling acceptance"));
            Assert.That(
                qualification.Split(
                    "tooling mutation",
                    StringSplitOptions.None),
                Has.Length.EqualTo(2));
            Assert.That(
                qualification,
                Does.Contain("tooling coverage"));
            Assert.That(
                qualification,
                Does.Contain("Download package-consumer qualification evidence"));
            Assert.That(qualification, Does.Contain("tooling release-plan"));
            Assert.That(
                qualification,
                Does.Contain("tooling release-qualification"));
        }
    }

    [Test]
    public void QualificationWriterRevalidatesArtifactsAndGateReceipts()
    {
        var root = TestRepository.FindRoot();
        var writer = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofReleaseContainer.ps1"));
        var receiptWriter = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Write-SharpProofQualificationReceipt.ps1"));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "package-consumers.yml"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                writer,
                Does.Contain("Test-SharpProofReleaseArtifacts.ps1"));
            Assert.That(writer, Does.Contain("packages.Count -ne 6"));
            Assert.That(writer, Does.Contain("does not match checkout HEAD"));
            Assert.That(writer, Does.Contain("requires a clean checkout"));
            Assert.That(
                writer,
                Does.Contain("Resolve-SharpProofContainedPath.ps1"));
            Assert.That(
                writer,
                Does.Contain("Resolve-SharpProofContainedPath"));
            Assert.That(writer, Does.Contain("$allUntrackedChanges"));
            Assert.That(writer, Does.Contain("$packagePrefix"));
            Assert.That(writer, Does.Not.Contain(":(exclude)"));
            Assert.That(writer, Does.Contain("annotated tag at checkout HEAD"));
            foreach (var gate in new[]
                     {
                         "coverage",
                         "mutation",
                         "package-consumers",
                         "pilots"
                     })
            {
                Assert.That(
                    workflow,
                    Does.Contain("tooling " + gate),
                    gate);
            }
            Assert.That(
                writer,
                Does.Contain("releaseQualificationMatrix")
                    .And.Contain("requiredGates"));
            Assert.That(writer, Does.Contain("status -cne 'passed'"));
            Assert.That(writer, Does.Not.Contain("sha256"));
            Assert.That(
                writer,
                Does.Contain("targets different packages"));
            Assert.That(receiptWriter, Does.Contain("status -ceq 'passed'"));
            Assert.That(receiptWriter, Does.Contain("mutationCount"));
            Assert.That(
                receiptWriter,
                Does.Contain("Test-SharpProofPilotReport")
                    .And.Contain("pilotEvidence"));
            Assert.That(receiptWriter, Does.Contain("packageArtifacts"));
        }
    }

    [Test]
    public async Task QualificationReceiptRejectsMalformedPackageIdentityEvidence()
    {
        await RunReceiptFixturesAsync(
            "package-consumers",
            "package-consumers.json",
            head =>
            {
                var packages = Enumerable.Range(0, 6)
                    .Select(index => new
                    {
                        fileName = $"package-{index}.nupkg",
                        bytes = 1
                    })
                    .ToArray();
                string Evidence(object packageArtifacts)
                {
                    return JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        status = "passed",
                        commit = head,
                        packageArtifacts
                    });
                }
                return
                [
                    (Evidence(packages), true),
                    (Evidence(packages.Take(5).ToArray()), false),
                    (Evidence(packages.Select((item, index) => index == 5
                        ? new
                        {
                            fileName = packages[0].fileName,
                            item.bytes
                        }
                        : item).ToArray()), false),
                    (Evidence(packages.Select((item, index) => index == 5
                        ? new
                        {
                            item.fileName,
                            bytes = 0
                        }
                        : item).ToArray()), false)
                ];
            });
    }

    [Test]
    public async Task QualificationReceiptRejectsMalformedFailedAndStaleEvidence()
    {
        await RunReceiptFixturesAsync(
            "acceptance-release",
            "acceptance.json",
            head =>
            {
                return
                [
                    ("not-json", false),
                    (JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        status = "failed",
                        commit = head
                    }), false),
                    (JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        status = "passed",
                        commit = new string('0', 40)
                    }), false),
                    (JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        command = "acceptance",
                        configuration = "release",
                        status = "passed",
                        commit = head
                    }), true)
                ];
            },
            expectedReceipt: "acceptance-release.json");
    }

    private static async Task RunReceiptFixturesAsync(
        string gate,
        string evidenceFileName,
        Func<string, (string Content, bool Valid)[]> createFixtures,
        string? expectedReceipt = null)
    {
        var root = TestRepository.FindRoot();
        var workspace = Path.Combine(
            root,
            "artifacts",
            "qualification-fixtures",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var head = (await RunAsync(root, "git", "rev-parse", "HEAD"))
                .Output.Trim();
            var evidencePath = Path.Combine(workspace, evidenceFileName);
            var receiptDirectory = Path.Combine(workspace, "receipts");
            foreach (var fixture in createFixtures(head))
            {
                await File.WriteAllTextAsync(evidencePath, fixture.Content);
                var result = await RunAsync(
                    root,
                    "pwsh",
                    "-NoLogo",
                    "-NoProfile",
                    "-File",
                    Path.Combine(
                        root,
                        "scripts",
                        "Write-SharpProofQualificationReceipt.ps1"),
                    "-Gate",
                    gate,
                    "-EvidencePath",
                    evidencePath,
                    "-ReceiptDirectory",
                    receiptDirectory);
                Assert.That(
                    result.ExitCode == 0,
                    Is.EqualTo(fixture.Valid),
                    result.Output + result.Error);
            }

            if (expectedReceipt is not null)
            {
                Assert.That(
                    File.Exists(Path.Combine(receiptDirectory, expectedReceipt)),
                    Is.True);
            }
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Test]
    public void ReleaseWorkflowUsesTheAllowlistedImmutableBaseline()
    {
        var root = TestRepository.FindRoot();
        var resolver = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Resolve-SharpProofReleaseCoverageBaseline.ps1"));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "package-consumers.yml"));
        var containerRelease = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofReleaseContainer.ps1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolver, Does.Contain(FirstPreviewBaseline));
            Assert.That(
                resolver,
                Does.Contain(
                    "'v1.0.0-preview.2' = 'v1.0.0-preview.1'"));
            Assert.That(
                resolver,
                Does.Contain("'v1.0.0-rc.1' = 'v1.0.0-preview.2'"));
            Assert.That(
                resolver,
                Does.Contain("'v1.0.0' = 'v1.0.0-rc.1'"));
            Assert.That(resolver, Does.Contain("merge-base"));
            Assert.That(resolver, Does.Contain("--is-ancestor"));
            Assert.That(resolver, Does.Contain("checked-out HEAD"));

            Assert.That(
                containerRelease.Split(
                    "Resolve-SharpProofReleaseCoverageBaseline.ps1",
                    StringSplitOptions.None),
                Has.Length.EqualTo(2));
            Assert.That(
                workflow,
                Does.Contain(
                    "SHARPPROOF_COVERAGE_COMPARISON_REF"));
            Assert.That(
                workflow,
                Does.Not.Contain("-ComparisonRef HEAD^"));
            Assert.That(
                containerRelease,
                Does.Contain("-ReleaseCommit $commit"));
            Assert.That(workflow, Does.Contain("tooling release-baseline"));
            Assert.That(workflow, Does.Contain("tooling coverage"));
        }
    }

    [Test]
    public async Task ResolverSelectsExactCommitsAndFailsClosed()
    {
        var root = TestRepository.FindRoot();
        var head = await RunAsync(
            root,
            "git",
            "rev-parse",
            "HEAD");
        Assert.That(head.ExitCode, Is.Zero, head.Error);
        var headCommit = head.Output.Trim();

        var selected = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            headCommit);
        Assert.That(selected.ExitCode, Is.Zero, selected.Error);
        using (var document = JsonDocument.Parse(selected.Output))
        {
            var evidence = document.RootElement;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    evidence.GetProperty("schemaVersion").GetInt32(),
                    Is.EqualTo(1));
                Assert.That(
                    evidence.GetProperty("coverageBaselineCommit").GetString(),
                    Is.EqualTo(FirstPreviewBaseline));
                Assert.That(
                    evidence.GetProperty("releaseCommit").GetString(),
                    Is.EqualTo(headCommit));
            }
        }

        var unknown = await RunResolverAsync(
            root,
            "v1.0.0-preview.99",
            headCommit);
        Assert.That(unknown.ExitCode, Is.Not.Zero);
        Assert.That(
            unknown.Error,
            Does.Contain("is not allowlisted"));

        var sameCommit = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            FirstPreviewBaseline);
        Assert.That(sameCommit.ExitCode, Is.Not.Zero);
        Assert.That(
            sameCommit.Error,
            Does.Contain("must precede the release commit"));

        var parent = await RunAsync(
            root,
            "git",
            "rev-parse",
            FirstPreviewBaseline + "^");
        Assert.That(parent.ExitCode, Is.Zero, parent.Error);
        var nonDescendant = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            parent.Output.Trim());
        Assert.That(nonDescendant.ExitCode, Is.Not.Zero);
        using (Assert.EnterMultipleScope())
        {
            var normalizedError =
                System.Text.RegularExpressions.Regex.Replace(
                    nonDescendant.Error,
                    @"\s+",
                    " ",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            Assert.That(
                normalizedError,
                Does.Contain("Coverage baseline"));
            Assert.That(
                normalizedError,
                Does.Contain("ancestor of release"));
            Assert.That(
                normalizedError,
                Does.Contain("commit"));
        }

        var releaseAncestor = await RunAsync(
            root,
            "git",
            "rev-list",
            "--ancestry-path",
            "--reverse",
            FirstPreviewBaseline + "..HEAD");
        Assert.That(
            releaseAncestor.ExitCode,
            Is.Zero,
            releaseAncestor.Error);
        var nonHeadReleaseCommit = releaseAncestor.Output
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .First(commit => commit != headCommit);
        var wrongCheckout = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            nonHeadReleaseCommit);
        Assert.That(wrongCheckout.ExitCode, Is.Not.Zero);
        Assert.That(
            wrongCheckout.Error,
            Does.Contain("does not"));
        Assert.That(
            wrongCheckout.Error,
            Does.Contain("identify the"));
        Assert.That(
            wrongCheckout.Error,
            Does.Contain("checked-out HEAD"));
    }

    private static Task<ProcessRunnerResult> RunResolverAsync(
        string root,
        string tag,
        string releaseCommit)
    {
        return RunAsync(
            root,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                root,
                "scripts",
                "Resolve-SharpProofReleaseCoverageBaseline.ps1"),
            "-Tag",
            tag,
            "-ReleaseCommit",
            releaseCommit);
    }

    [TestCase("src/./trusted.cs")]
    [TestCase("src/part/../trusted.cs")]
    [TestCase("src\\trusted.cs")]
    [TestCase("/src/trusted.cs")]
    [TestCase("src//trusted.cs")]
    [TestCase("src/trusted.cs/")]
    public async Task TrustedComputingBaseRejectsNoncanonicalPaths(
        string path)
    {
        var root = TestRepository.FindRoot();
        const string command = """
            . $env:SHARPPROOF_TCB_HELPER
            $contract = [pscustomobject]@{
                trustedKernel = [pscustomobject]@{ paths = @(
                    $env:SHARPPROOF_TCB_PATH) }
                trustedComputingBase = [pscustomobject]@{ components = @() }
            }
            Get-SharpProofTcbPaths -Contract $contract | Out-Null
            """;
        var result = await RunAsyncCore(
            root,
            "pwsh",
            new Dictionary<string, string>
            {
                ["SHARPPROOF_TCB_HELPER"] = Path.Combine(
                    root,
                    "scripts",
                    "Get-SharpProofTcbPaths.ps1"),
                ["SHARPPROOF_TCB_PATH"] = path
            },
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            command);

        Assert.That(result.ExitCode, Is.Not.Zero, path);
    }
    private static async Task<ProcessRunnerResult> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        return await RunAsyncCore(
            workingDirectory,
            fileName,
            environment: null,
            arguments);
    }

    private static async Task<ProcessRunnerResult> RunAsyncCore(
        string workingDirectory,
        string fileName,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = ProcessRunner.CreateStartInfo(
            workingDirectory,
            fileName,
            arguments);
        if (environment != null)
        {
            foreach (var entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }
        var result = await ProcessRunner.RunCapturedAsync(
            startInfo,
            CancellationToken.None);
        const string AnsiPattern =
            "\\x1B\\[[0-?]*[ -/]*[@-~]";
        return new ProcessRunnerResult(
            result.ExitCode,
            System.Text.RegularExpressions.Regex.Replace(
                result.Output,
                AnsiPattern,
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            System.Text.RegularExpressions.Regex.Replace(
                result.Error,
                AnsiPattern,
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant));
    }

}
