using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseCoverageBaselineTests
{
    private const string FirstPreviewBaseline =
        "8347a70187a63cc7302b35e747d484747a929f6c";
    private static readonly string[] s_upstreamNeeds =
    [
        "package",
        "container-verifier",
        "security",
        "attest",
        "portable-consumers"
    ];

    [Test]
    public void ReleaseQualificationImportsEveryUpstreamResult()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "package-consumers.yml"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(workflow, Does.Not.Contain("always() &&"));
            Assert.That(workflow, Does.Contain("- attest"));
            Assert.That(workflow, Does.Contain("portable-consumers"));
            Assert.That(workflow, Does.Not.Contain("minimum-sdk-consumer"));

            var needs = ParseJobNeeds(workflow, "release-qualification");
            Assert.That(
                needs,
                Is.EqualTo(s_upstreamNeeds),
                "The release qualification job must depend on exactly the upstream jobs.");
        }
    }

    private static string[] ParseJobNeeds(string workflow, string jobName)
    {
        var inJob = false;
        var inNeeds = false;
        var needs = new List<string>();
        foreach (var rawLine in workflow.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            if (indent == 2 && trimmed == jobName + ":")
            {
                inJob = true;
                inNeeds = false;
                continue;
            }

            if (!inJob)
            {
                continue;
            }

            if (indent == 2 && trimmed.EndsWith(':'))
            {
                break;
            }

            if (indent == 4 && trimmed == "needs:")
            {
                inNeeds = true;
                continue;
            }

            if (inNeeds && indent <= 4)
            {
                break;
            }

            if (inNeeds && indent >= 6 && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                needs.Add(trimmed[2..].Trim());
            }
        }

        return needs.ToArray();
    }

    [Test]
    public void ReleaseQualificationInitializesBeforeSdkAndAvoidsStaleExitCodes()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
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
                Does.Contain("tooling package-consumers"));
            Assert.That(qualification, Does.Contain("tooling release-plan"));
            Assert.That(
                qualification,
                Does.Contain("tooling release-qualification"));
        }
    }

    [Test]
    public void QualificationWriterRevalidatesArtifactsAndGateReceipts()
    {
        var root = RepositoryRoot();
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
            Assert.That(writer, Does.Contain("evidence.sha256"));
            Assert.That(
                writer,
                Does.Contain("targets different packages"));
            Assert.That(
                writer,
                Does.Contain("Assert-SharpProofReleaseMutationConfiguration")
                    .And.Contain("receipt.configuration -cne 'Release'"));
            Assert.That(receiptWriter, Does.Contain("status -ceq 'passed'"));
            Assert.That(receiptWriter, Does.Contain("mutationCount"));
            Assert.That(
                receiptWriter,
                Does.Contain("Assert-SharpProofReleaseMutationConfiguration")
                    .And.Contain("$receipt['configuration'] = 'Release'"));
            Assert.That(
                receiptWriter,
                Does.Contain("Test-SharpProofPilotReport")
                    .And.Contain("pilotEvidence"));
            Assert.That(receiptWriter, Does.Contain("packageArtifacts"));
        }
    }

    [Test]
    [Category("GitBound")]
    public async Task QualificationReceiptRejectsMalformedPackageIdentityEvidence()
    {
        var root = RepositoryRoot();
        var parent = Path.Combine(root, "artifacts", "qualification-fixtures");
        var workspace = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var head = (await RunAsync(root, "git", "rev-parse", "HEAD"))
                .Output.Trim();
            var evidencePath = Path.Combine(workspace, "package-consumers.json");
            var receiptDirectory = Path.Combine(workspace, "receipts");
            var packages = Enumerable.Range(0, 6)
                .Select(index => new
                {
                    fileName = $"package-{index}.nupkg",
                    bytes = 1,
                    sha256 = new string((char)('a' + index), 64)
                })
                .ToArray();
            var fixtures = new[]
            {
                (Packages: packages, Valid: true),
                (Packages: packages.Take(5).ToArray(), Valid: false),
                (Packages: packages.Select((item, index) => index == 5
                    ? new
                    {
                        fileName = packages[0].fileName,
                        item.bytes,
                        item.sha256
                    }
                    : item).ToArray(), Valid: false),
                (Packages: packages.Select((item, index) => index == 5
                    ? new
                    {
                        item.fileName,
                        item.bytes,
                        sha256 = "not-a-digest"
                    }
                    : item).ToArray(), Valid: false)
            };
            foreach (var fixture in fixtures)
            {
                await File.WriteAllTextAsync(
                    evidencePath,
                    JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        status = "passed",
                        commit = head,
                        packageArtifacts = fixture.Packages
                    }));
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
                    "package-consumers",
                    "-EvidencePath",
                    evidencePath,
                    "-ReceiptDirectory",
                    receiptDirectory);
                Assert.That(
                    result.ExitCode == 0,
                    Is.EqualTo(fixture.Valid),
                    result.Output + result.Error);
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
    [Category("GitBound")]
    public async Task QualificationReceiptRejectsMalformedFailedAndStaleEvidence()
    {
        var root = RepositoryRoot();
        var parent = Path.Combine(root, "artifacts", "qualification-fixtures");
        var workspace = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var head = (await RunAsync(root, "git", "rev-parse", "HEAD"))
                .Output.Trim();
            var evidencePath = Path.Combine(workspace, "acceptance.json");
            var receiptDirectory = Path.Combine(workspace, "receipts");
            using var contract = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "acceptance", "contract.json")));
            var phaseNames = contract.RootElement
                .GetProperty("automation")
                .GetProperty("acceptanceTimingPhases")
                .EnumerateArray()
                .Select(static phase => phase.GetString()!)
                .ToArray();
            var passingPhases = phaseNames
                .Select(static name => new { name, status = "passed" })
                .ToArray();
            var fixtures = new[]
            {
                (Value: "not-json", Valid: false),
                (Value: JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "failed",
                    commit = head
                }), Valid: false),
                (Value: JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "passed",
                    commit = new string('0', 40)
                }), Valid: false),
                (Value: JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "passed",
                    commit = head,
                    phases = passingPhases
                }), Valid: true)
            };
            foreach (var fixture in fixtures)
            {
                await File.WriteAllTextAsync(evidencePath, fixture.Value);
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
                    "acceptance-release",
                    "-EvidencePath",
                    evidencePath,
                    "-ReceiptDirectory",
                    receiptDirectory);
                Assert.That(
                    result.ExitCode == 0,
                    Is.EqualTo(fixture.Valid),
                    result.Output + result.Error);
            }
            Assert.That(
                File.Exists(Path.Combine(
                    receiptDirectory,
                    "acceptance-release.json")),
                Is.True);

            var failedPhases = passingPhases
                .Select((phase, index) => index == 2
                    ? new { phase.name, status = "failed" }
                    : phase)
                .ToArray();
            await File.WriteAllTextAsync(
                evidencePath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    command = "acceptance",
                    configuration = "Release",
                    status = "passed",
                    commit = head,
                    phases = failedPhases
                }));
            var failedPhaseResult = await RunAsync(
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
                "acceptance-release",
                "-EvidencePath",
                evidencePath,
                "-ReceiptDirectory",
                receiptDirectory);
            Assert.That(
                failedPhaseResult.ExitCode,
                Is.Not.Zero,
                failedPhaseResult.Output + failedPhaseResult.Error);
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
    public async Task CoverageReceiptRequiresAJsonBooleanTrue()
    {
        var root = RepositoryRoot();
        var parent = Path.Combine(root, "artifacts", "qualification-fixtures");
        var workspace = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            await AssertSuccessAsync(RunAsync(
                root,
                "git",
                "-C",
                workspace,
                "init",
                "--object-format=sha1"));
            await AssertSuccessAsync(RunAsync(
                root,
                "git",
                "-C",
                workspace,
                "config",
                "user.email",
                "coverage-receipt@example.invalid"));
            await AssertSuccessAsync(RunAsync(
                root,
                "git",
                "-C",
                workspace,
                "config",
                "user.name",
                "Coverage Receipt Test"));
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "anchor.txt"),
                "coverage receipt fixture\n");
            await AssertSuccessAsync(RunAsync(
                root,
                "git",
                "-C",
                workspace,
                "add",
                "."));
            await AssertSuccessAsync(RunAsync(
                root,
                "git",
                "-C",
                workspace,
                "commit",
                "-m",
                "anchor"));
            var head = (await AssertSuccessAsync(RunAsync(
                root,
                "git",
                "-C",
                workspace,
                "rev-parse",
                "HEAD"))).Output.Trim();
            var evidencePath = Path.Combine(workspace, "coverage.json");
            var receiptDirectory = Path.Combine(workspace, "receipts");
            var fixtures = new[]
            {
                (Value: $"{{\"schemaVersion\":1,\"passed\":true,\"commit\":\"{head}\"}}", Valid: true),
                (Value: $"{{\"schemaVersion\":1,\"passed\":false,\"commit\":\"{head}\"}}", Valid: false),
                (Value: $"{{\"schemaVersion\":1,\"passed\":\"false\",\"commit\":\"{head}\"}}", Valid: false),
                (Value: $"{{\"schemaVersion\":1,\"passed\":\"true\",\"commit\":\"{head}\"}}", Valid: false),
                (Value: $"{{\"schemaVersion\":1,\"passed\":1,\"commit\":\"{head}\"}}", Valid: false),
                (Value: $"{{\"schemaVersion\":1,\"passed\":null,\"commit\":\"{head}\"}}", Valid: false),
                (Value: $"{{\"schemaVersion\":1,\"commit\":\"{head}\"}}", Valid: false)
            };
            string? receiptBytes = null;
            foreach (var fixture in fixtures)
            {
                await File.WriteAllTextAsync(evidencePath, fixture.Value);
                var result = await RunAsync(
                    workspace,
                    "pwsh",
                    "-NoLogo",
                    "-NoProfile",
                    "-File",
                    Path.Combine(
                        root,
                        "scripts",
                        "Write-SharpProofQualificationReceipt.ps1"),
                    "-RepositoryRoot",
                    workspace,
                    "-Gate",
                    "coverage",
                    "-EvidencePath",
                    evidencePath,
                    "-ReceiptDirectory",
                    receiptDirectory);
                Assert.That(
                    result.ExitCode == 0,
                    Is.EqualTo(fixture.Valid),
                    result.Output + result.Error);
                var receiptPath = Path.Combine(receiptDirectory, "coverage.json");
                if (fixture.Valid)
                {
                    receiptBytes = await File.ReadAllTextAsync(receiptPath);
                }
                else
                {
                    Assert.That(
                        File.Exists(receiptPath),
                        Is.EqualTo(receiptBytes is not null));
                    if (receiptBytes is not null)
                    {
                        Assert.That(await File.ReadAllTextAsync(receiptPath),
                            Is.EqualTo(receiptBytes));
                    }
                }
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
        var root = RepositoryRoot();
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
    [Category("GitBound")]
    public async Task ResolverSelectsExactCommitsAndFailsClosed()
    {
        var root = RepositoryRoot();
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

    [Test]
    public void ReleaseDigestCanonicalStreamIncludesGitModeAndType()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Get-SharpProofReleaseDigests.ps1"));
        var digestStart = script.IndexOf(
            "function Get-CanonicalDigest",
            StringComparison.Ordinal);
        var digestEnd = script.IndexOf(
            "if ($null -eq (Get-Command git",
            digestStart,
            StringComparison.Ordinal);
        Assert.That(digestStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(digestEnd, Is.GreaterThan(digestStart));
        var digest = script[digestStart..digestEnd];
        var mode = digest.IndexOf(
            "[Text.Encoding]::ASCII.GetBytes([string]$entry.Mode)",
            StringComparison.Ordinal);
        var type = digest.IndexOf(
            "[Text.Encoding]::ASCII.GetBytes([string]$entry.Type)",
            StringComparison.Ordinal);
        var path = digest.IndexOf(
            "[Text.Encoding]::UTF8.GetBytes($path)",
            StringComparison.Ordinal);
        var content = digest.IndexOf(
            "$hash.AppendData($contentDigest)",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("Get-GitTreeEntries"));
            Assert.That(script, Does.Not.Contain("'--name-only'"));
            Assert.That(mode, Is.GreaterThanOrEqualTo(0));
            Assert.That(type, Is.GreaterThan(mode));
            Assert.That(path, Is.GreaterThan(type));
            Assert.That(content, Is.GreaterThan(path));
        }
    }

    [Test]
    public async Task ReleaseDigestsBindEntryModeAndRemainCultureStable()
    {
        var root = RepositoryRoot();
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-release-digest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "init",
                "--object-format=sha1"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "config",
                "user.email",
                "release-digest@example.invalid"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "config",
                "user.name",
                "Release Digest Test"));

            var paths = new[]
            {
                "src/I-alpha.txt",
                "src/i-beta.txt",
                "src/\u0130-gamma.txt",
                "src/\u0131-delta.txt",
                "scripts/Get-SharpProofTcbPaths.ps1"
            };
            foreach (var path in paths)
            {
                var absolutePath = Path.Combine(
                    repository,
                    path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                await File.WriteAllTextAsync(
                    absolutePath,
                    "same blob\n");
            }
            File.Copy(
                Path.Combine(
                    root,
                    "scripts",
                    "Get-SharpProofTcbPaths.ps1"),
                Path.Combine(
                    repository,
                    "scripts",
                    "Get-SharpProofTcbPaths.ps1"),
                overwrite: true);
            File.Copy(
                Path.Combine(
                    root,
                    "scripts",
                    "Get-SharpProofProductionInventory.ps1"),
                Path.Combine(
                    repository,
                    "scripts",
                    "Get-SharpProofProductionInventory.ps1"),
                overwrite: true);

            var projectDirectory = Path.Combine(repository, "Fixture");
            Directory.CreateDirectory(projectDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(projectDirectory, "Fixture.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <SharpProofProductionProject>true</SharpProofProductionProject>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(projectDirectory, "Source.cs"),
                "internal static class Source { internal static int Value => 1; }\n");
            await File.WriteAllTextAsync(
                Path.Combine(repository, "SharpProof.sln"),
                "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
                "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Fixture\", \"Fixture/Fixture.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\n" +
                "EndProject\nGlobal\nEndGlobal\n");
            await File.WriteAllTextAsync(
                Path.Combine(repository, ".gitignore"),
                "**/bin/\n**/obj/\n");
            var generatedDirectory = Path.Combine(repository, "eng", "generated");
            Directory.CreateDirectory(generatedDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(generatedDirectory, "approved-outputs.v1.json"),
                "{\"schemaVersion\":1,\"outputs\":[]}\n");
            var coverageDirectory = Path.Combine(repository, "eng", "coverage");
            Directory.CreateDirectory(coverageDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(coverageDirectory, "SharpProof.Gates.runsettings"),
                "<RunSettings />\n");

            var acceptancePath = Path.Combine(
                repository,
                "eng",
                "acceptance",
                "contract.json");
            Directory.CreateDirectory(
                Path.GetDirectoryName(acceptancePath)!);
            await File.WriteAllTextAsync(
                acceptancePath,
                JsonSerializer.Serialize(new
                {
                    trustedKernel = new
                    {
                        paths = new[] { paths[0] }
                    },
                    trustedComputingBase = new
                    {
                        components = new[]
                        {
                            new
                            {
                                paths = paths.Skip(1).ToArray()
                            }
                        }
                    }
                }));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "add",
                "--",
                "."));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "regular files"));
            var regularCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();

            var dirtyPath = Path.Combine(
                repository,
                paths[0].Replace('/', Path.DirectorySeparatorChar));
            await File.AppendAllTextAsync(dirtyPath, "dirty checkout\n");
            var dirty = await RunReleaseDigestProcessAsync(
                root,
                repository,
                regularCommit,
                "en-US");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(dirty.ExitCode, Is.Not.Zero);
                Assert.That(dirty.Error, Does.Contain("clean checkout"));
            }
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "restore",
                "--worktree",
                "--",
                paths[0]));

            var english = await RunReleaseDigestAsync(
                root,
                repository,
                regularCommit,
                "en-US");
            var turkish = await RunReleaseDigestAsync(
                root,
                repository,
                regularCommit,
                "tr-TR");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(turkish, Is.EqualTo(english));
                Assert.That(
                    english.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
            }

            var componentPath = Path.Combine(
                repository,
                paths[1].Replace('/', Path.DirectorySeparatorChar));
            await File.WriteAllTextAsync(
                componentPath,
                "changed component\n");
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "add",
                "--",
                paths[1]));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "component change"));
            var componentCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();
            var component = await RunReleaseDigestAsync(
                root,
                repository,
                componentCommit,
                "en-US");

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    Path.Combine(
                        repository,
                        paths[0].Replace('/', Path.DirectorySeparatorChar)),
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead |
                    UnixFileMode.OtherExecute);
            }
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "update-index",
                "--chmod=+x",
                "--",
                paths[0]));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "executable file"));
            var executableCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();
            var executable = await RunReleaseDigestAsync(
                root,
                repository,
                executableCommit,
                "en-US");
            var blobIdentity = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                $"{executableCommit}:{paths[0]}"))).Output.Trim();
            File.Delete(
                Path.Combine(
                    repository,
                    paths[0].Replace('/', Path.DirectorySeparatorChar)));
            File.CreateSymbolicLink(
                Path.Combine(
                    repository,
                    paths[0].Replace('/', Path.DirectorySeparatorChar)),
                "same blob\n");
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "update-index",
                "--cacheinfo",
                "120000",
                blobIdentity,
                paths[0]));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "symbolic link"));
            var symbolicLinkCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();
            var symbolicLink = await RunReleaseDigestAsync(
                root,
                repository,
                symbolicLinkCommit,
                "en-US");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    executable.ProductionDigest,
                    Is.Not.EqualTo(english.ProductionDigest));
                Assert.That(
                    executable.TrustedComputingBaseDigest,
                    Is.Not.EqualTo(english.TrustedComputingBaseDigest));
                Assert.That(
                    executable.ProductionFileCount,
                    Is.EqualTo(english.ProductionFileCount));
                Assert.That(
                    executable.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
                Assert.That(
                    component.TrustedComputingBaseDigest,
                    Is.Not.EqualTo(english.TrustedComputingBaseDigest));
                Assert.That(
                    component.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
                Assert.That(
                    symbolicLink.ProductionDigest,
                    Is.Not.EqualTo(executable.ProductionDigest));
                Assert.That(
                    symbolicLink.TrustedComputingBaseDigest,
                    Is.Not.EqualTo(
                        executable.TrustedComputingBaseDigest));
                Assert.That(
                    symbolicLink.ProductionFileCount,
                    Is.EqualTo(english.ProductionFileCount));
                Assert.That(
                    symbolicLink.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
            }
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
    }

    private static Task<ProcessResult> RunResolverAsync(
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
        var root = RepositoryRoot();
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

    private static async Task<ReleaseDigest> RunReleaseDigestAsync(
        string root,
        string repository,
        string commit,
        string culture)
    {
        var result = await RunReleaseDigestProcessAsync(
            root,
            repository,
            commit,
            culture);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var evidence = document.RootElement;
        return new ReleaseDigest(
            evidence
                .GetProperty("productionDigestSha256")
                .GetString()!,
            evidence
                .GetProperty("trustedComputingBaseDigestSha256")
                .GetString()!,
            evidence
                .GetProperty("productionFileCount")
                .GetInt32(),
            evidence
                .GetProperty("trustedComputingBaseFileCount")
                .GetInt32());
    }

    private static Task<ProcessResult> RunReleaseDigestProcessAsync(
        string root,
        string repository,
        string commit,
        string culture)
    {
        const string command =
            "$culture = [Globalization.CultureInfo]::GetCultureInfo(" +
            "$env:SHARPPROOF_TEST_CULTURE); " +
            "[Globalization.CultureInfo]::CurrentCulture = $culture; " +
            "[Globalization.CultureInfo]::CurrentUICulture = $culture; " +
            "& $env:SHARPPROOF_TEST_SCRIPT " +
            "-RepositoryPath $env:SHARPPROOF_TEST_REPOSITORY " +
            "-Commit $env:SHARPPROOF_TEST_COMMIT";
        return RunAsyncCore(
            root,
            "pwsh",
            new Dictionary<string, string>
            {
                ["SHARPPROOF_TEST_CULTURE"] = culture,
                ["SHARPPROOF_TEST_SCRIPT"] = Path.Combine(
                    root,
                    "scripts",
                    "Get-SharpProofReleaseDigests.ps1"),
                ["SHARPPROOF_TEST_REPOSITORY"] = repository,
                ["SHARPPROOF_TEST_COMMIT"] = commit
            },
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            command);
    }

    private static async Task<ProcessResult> AssertSuccessAsync(
        Task<ProcessResult> operation)
    {
        var result = await operation;
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        return result;
    }

    private static void DeleteTemporaryRepository(string repository)
    {
        if (!Directory.Exists(repository))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
            repository,
            "*",
            SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        Directory.Delete(repository, recursive: true);
    }

    private static async Task<ProcessResult> RunAsync(
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

    private static async Task<ProcessResult> RunAsyncCore(
        string workingDirectory,
        string fileName,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (environment != null)
        {
            foreach (var entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var standardOutput = await output;
        var standardError = await error;
        const string AnsiPattern =
            "\\x1B\\[[0-?]*[ -/]*[@-~]";
        return new ProcessResult(
            process.ExitCode,
            System.Text.RegularExpressions.Regex.Replace(
                standardOutput,
                AnsiPattern,
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            System.Text.RegularExpressions.Regex.Replace(
                standardError,
                AnsiPattern,
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not find the repository root.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed record ReleaseDigest(
        string ProductionDigest,
        string TrustedComputingBaseDigest,
        int ProductionFileCount,
        int TrustedComputingBaseFileCount);
}
