using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed partial class HumanReleaseGateScriptTests
{
    private const string ProductCommit =
        "1111111111111111111111111111111111111111";
    private const string BaselineCommit =
        "2222222222222222222222222222222222222222";
    private const string OtherCommit =
        "3333333333333333333333333333333333333333";
    private const string QualifiedRcCommit =
        "4444444444444444444444444444444444444444";
    private const string QualifiedRcVersion = "1.0.0-rc.7";
    private const string GitHubApiToken = "fixture-github-token";
    private const int UnixRegularFileAttributes =
        unchecked((int)0x81A40000u);
    private static readonly string Digest = new('a', 64);
    private static readonly string GitHubApiMock =
        """
        function global:Invoke-RestMethod {
            [CmdletBinding()]
            param(
                [Parameter(Mandatory = $true)]
                [object]$Uri,

                [Parameter(Mandatory = $true)]
                [string]$Method,

                [Parameter(Mandatory = $true)]
                [hashtable]$Headers,

                [Parameter(Mandatory = $true)]
                [int]$MaximumRedirection,

                [Parameter(Mandatory = $true)]
                [int]$TimeoutSec
            )

            if ($Method -cne 'Get' -or
                $MaximumRedirection -ne 0 -or
                $Headers.Authorization -cne
                    'Bearer fixture-github-token' -or
                $Headers.Accept -cne
                    'application/vnd.github+json' -or
                $Headers['X-GitHub-Api-Version'] -cne '2022-11-28') {
                throw 'GitHub API request was not authenticated as expected.'
            }
            $uriText = [string]$Uri
            $artifactDirectory = if (
                -not [string]::IsNullOrWhiteSpace(
                    $env:SHARPPROOF_TEST_GITHUB_ARTIFACT_DIRECTORY)) {
                $env:SHARPPROOF_TEST_GITHUB_ARTIFACT_DIRECTORY
            }
            else {
                $global:SharpProofTestGitHubArtifactDirectory
            }
            if ($uriText -match
                    '^https://api[.]github[.]com/repos/(?<owner>[A-Za-z0-9-]+)/(?<repo>[A-Za-z0-9_.-]+)/actions/runs/(?<run>[0-9]+)/artifacts[?]name=(?<name>[^&]+)&per_page=100$') {
                $account = $Matches.owner
                $repositoryName = $Matches.repo
                $runId = [int64]$Matches.run
                $artifactName = [Uri]::UnescapeDataString($Matches.name)
                $artifactKind = if (
                    $artifactName.StartsWith(
                        'release-qualification-',
                        [StringComparison]::Ordinal)) {
                    1
                }
                elseif ($artifactName.StartsWith(
                        'nuget-packages-',
                        [StringComparison]::Ordinal)) {
                    2
                }
                elseif ($artifactName.StartsWith(
                        'sharpproof-pilot-evidence-',
                        [StringComparison]::Ordinal)) {
                    3
                }
                else {
                    throw "Unexpected artifact name '$artifactName'."
                }
                $artifactId = [int64]($artifactKind * 1000000 + $runId)
                $archivePath = Join-Path `
                    $artifactDirectory `
                    "$artifactId.zip"
                if (-not [IO.File]::Exists($archivePath)) {
                    return [pscustomobject][ordered]@{
                        total_count = 0
                        artifacts = @()
                    }
                }
                if ($artifactName -match
                        '(?<sha>[0-9a-f]{40})(?:-[0-9]+){1,2}$') {
                    $sourceCommit = $Matches.sha
                }
                else {
                    throw "Artifact name '$artifactName' has no source SHA."
                }
                $digest = (Get-FileHash `
                    -LiteralPath $archivePath `
                    -Algorithm SHA256).Hash.ToLowerInvariant()
                $artifactTimestamp = if ($runId -eq 201) {
                    [DateTimeOffset]::Parse(
                        '2020-01-02T01:30:00Z',
                        [Globalization.CultureInfo]::InvariantCulture)
                }
                elseif ($runId -ge 1 -and $runId -le 4) {
                    [DateTimeOffset]::Parse(
                        '2020-01-12T01:30:00Z',
                        [Globalization.CultureInfo]::InvariantCulture).
                        AddDays(7 * ($runId - 1))
                }
                elseif ($runId -ge 101 -and $runId -le 104) {
                    [DateTimeOffset]::Parse(
                        '2020-01-12T01:30:00Z',
                        [Globalization.CultureInfo]::InvariantCulture).
                        AddDays(7 * ($runId - 101))
                }
                elseif ($runId -eq 908) {
                    [DateTimeOffset]::Parse(
                        '2020-01-11T01:30:00Z',
                        [Globalization.CultureInfo]::InvariantCulture)
                }
                else {
                    [DateTimeOffset]::Parse(
                        '2020-01-12T01:30:00Z',
                        [Globalization.CultureInfo]::InvariantCulture)
                }
                $artifactTimestampText = $artifactTimestamp.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    [Globalization.CultureInfo]::InvariantCulture)
                return [pscustomobject][ordered]@{
                    total_count = 1
                    artifacts = @(
                        [pscustomobject][ordered]@{
                            id = $artifactId
                            name = $artifactName
                            expired = $false
                            size_in_bytes =
                                ([IO.FileInfo]::new($archivePath)).Length
                            digest = "sha256:$digest"
                            created_at = $artifactTimestampText
                            updated_at = $artifactTimestampText
                            archive_download_url = (
                                "https://api.github.com/repos/$account/" +
                                "$repositoryName/actions/artifacts/" +
                                "$artifactId/zip")
                            workflow_run = [pscustomobject][ordered]@{
                                id = $runId
                                head_sha = $sourceCommit
                            }
                        }
                    )
                }
            }
            if ($uriText -notmatch
                    '^https://api[.]github[.]com/repos/(?<owner>[A-Za-z0-9-]+)/(?<repo>[A-Za-z0-9_.-]+)/actions/runs/(?<run>[0-9]+)/attempts/(?<attempt>[0-9]+)$') {
                throw "Unexpected GitHub API URL '$uriText'."
            }
            $account = $Matches.owner
            $repositoryName = $Matches.repo
            $runId = [int64]$Matches.run
            $runAttempt = [int]$Matches.attempt
            if ($runId -eq 999) {
                throw 'GitHub API returned 404 Not Found.'
            }

            if ($runId -eq 201) {
                $timestamp = [DateTimeOffset]::Parse(
                    '2020-01-02T00:00:00Z',
                    [Globalization.CultureInfo]::InvariantCulture)
                $workflowName = 'Cross-platform package consumers'
                $workflowPath =
                    '.github/workflows/package-consumers.yml'
                $workflowEvent = 'push'
                $responseSourceCommit = (
                    Get-Content `
                        -LiteralPath (
                            Join-Path `
                                $artifactDirectory `
                                'qualified-rc.txt') `
                        -Raw).Trim()
            }
            else {
                $weekIndex = if ($runId -ge 1 -and $runId -le 4) {
                [int]($runId - 1)
                }
                elseif ($runId -ge 101 -and $runId -le 104) {
                    [int]($runId - 101)
                }
                elseif ($runId -ge 901 -and $runId -le 908) {
                    0
                }
                else {
                    throw "Unexpected GitHub Actions run ID '$runId'."
                }
                $timestamp = [DateTimeOffset]::Parse(
                    '2020-01-12T00:00:00Z',
                    [Globalization.CultureInfo]::InvariantCulture).AddDays(
                        7 * $weekIndex)
                $workflowName = 'SharpProof strict weekly'
                $workflowPath =
                    '.github/workflows/sharpproof-strict-weekly.yml'
                $workflowEvent = 'workflow_dispatch'
                $responseSourceCommit = '__OTHER_COMMIT__'
            }
            $responseRunId = $runId
            $responseRunAttempt = $runAttempt
            $responseRepository = "$account/$repositoryName"
            $responseConclusion = 'success'
            $updatedAt = $timestamp.AddHours(2)
            switch ($runId) {
                901 {
                    $updatedAt = $updatedAt.AddDays(1)
                }
                902 {
                    $responseConclusion = 'failure'
                }
                903 {
                    $responseRepository = 'owner/api-mismatch'
                }
                904 {
                    $responseRunAttempt++
                }
                905 {
                    $responseSourceCommit =
                        '5555555555555555555555555555555555555555'
                }
                906 {
                    $responseRunId++
                }
                907 {
                    $workflowPath = '.github/workflows/other.yml'
                }
            }
            $format = "yyyy-MM-dd'T'HH:mm:ss'Z'"
            return [pscustomobject][ordered]@{
                id = $responseRunId
                run_attempt = $responseRunAttempt
                repository = [pscustomobject][ordered]@{
                    full_name = $responseRepository
                }
                head_repository = [pscustomobject][ordered]@{
                    full_name = $responseRepository
                }
                name = $workflowName
                path = $workflowPath
                event = $workflowEvent
                head_sha = $responseSourceCommit
                status = 'completed'
                conclusion = $responseConclusion
                html_url = (
                    "https://github.com/$account/$repositoryName/" +
                    "actions/runs/$runId")
                created_at = $timestamp.AddMinutes(90).ToString(
                    $format,
                    [Globalization.CultureInfo]::InvariantCulture)
                run_started_at = $timestamp.AddHours(1).ToString(
                    $format,
                    [Globalization.CultureInfo]::InvariantCulture)
                updated_at = $updatedAt.ToString(
                    $format,
                    [Globalization.CultureInfo]::InvariantCulture)
            }
        }

        function global:Invoke-WebRequest {
            [CmdletBinding()]
            param(
                [Parameter(Mandatory = $true)]
                [object]$Uri,

                [Parameter(Mandatory = $true)]
                [string]$Method,

                [Parameter(Mandatory = $true)]
                [hashtable]$Headers,

                [Parameter(Mandatory = $true)]
                [int]$MaximumRedirection,

                [Parameter(Mandatory = $true)]
                [int]$TimeoutSec,

                [Parameter(Mandatory = $true)]
                [string]$OutFile
            )

            if ($Method -cne 'Get' -or
                $MaximumRedirection -ne 5 -or
                $Headers.Authorization -cne
                    'Bearer fixture-github-token') {
                throw 'Artifact request was not authenticated as expected.'
            }
            $uriText = [string]$Uri
            if ($uriText -notmatch
                    '^https://api[.]github[.]com/repos/[A-Za-z0-9-]+/[A-Za-z0-9_.-]+/actions/artifacts/(?<artifact>[0-9]+)/zip$') {
                throw "Unexpected artifact download URL '$uriText'."
            }
            $artifactDirectory = if (
                -not [string]::IsNullOrWhiteSpace(
                    $env:SHARPPROOF_TEST_GITHUB_ARTIFACT_DIRECTORY)) {
                $env:SHARPPROOF_TEST_GITHUB_ARTIFACT_DIRECTORY
            }
            else {
                $global:SharpProofTestGitHubArtifactDirectory
            }
            $source = Join-Path `
                $artifactDirectory `
                "$($Matches.artifact).zip"
            if (-not [IO.File]::Exists($source)) {
                throw 'GitHub API returned 404 Not Found.'
            }
            [IO.File]::Copy($source, $OutFile, $true)
            return [pscustomobject]@{
                StatusCode = 200
            }
        }
        """.Replace(
            "__OTHER_COMMIT__",
            OtherCommit,
            StringComparison.Ordinal);
    private static readonly JsonSerializerOptions s_indentedJsonOptions =
        new()
        {
            WriteIndented = true
        };

    [Test]
    public async Task AnnotatedExternalEvidencePassesAndBindsResolvedCommit()
    {
        using var workspace = await EvidenceWorkspace.CreateAsync(
            CreateEvidenceJson(),
            annotatedTag: true);
        var validationPath = Path.Combine(
            workspace.Root,
            "human-validation.json");

        var result = await RunHumanGateAsync(
            workspace.Repository,
            validationPath,
            workspace.ProductCommit);

        Assert.That(result.ExitCode, Is.Zero, result.Output);
        using var validation = JsonDocument.Parse(
            await File.ReadAllBytesAsync(validationPath));
        Assert.That(
            validation.RootElement.GetProperty("status").GetString(),
            Is.EqualTo("passed"));
        Assert.That(
            validation.RootElement.GetProperty("productCommit").GetString(),
            Is.EqualTo(workspace.ProductCommit));
        Assert.That(
            validation.RootElement.GetProperty("evidenceCommit").GetString(),
            Is.EqualTo(workspace.EvidenceCommit));
        Assert.That(
            validation.RootElement.GetProperty("evidenceTagObject")
                .GetString(),
            Has.Length.EqualTo(40));
        Assert.That(
            validation.RootElement.GetProperty("evidenceDocumentSha256")
                .GetString(),
            Has.Length.EqualTo(64));
        Assert.That(
            validation.RootElement.GetProperty("qualifiedRc")
                .GetProperty("productCommit")
                .GetString(),
            Is.EqualTo(workspace.QualifiedRcCommit));
        Assert.That(
            validation.RootElement.GetProperty("qualifiedRc")
                .GetProperty("packageVersion")
                .GetString(),
            Is.EqualTo(QualifiedRcVersion));
    }

    [TestCase(
        "fixtureMissingPilotArtifact",
        "exactly one current-attempt GitHub artifact")]
    [TestCase(
        "fixturePilotArtifactMismatch",
        "authenticated pilot artifact does not exactly match")]
    [TestCase(
        "fixtureFailedQualificationArtifact",
        "must be a passed schema-5 record")]
    [TestCase(
        "fixtureCorruptQualificationArchive",
        "artifact metadata must match")]
    [TestCase(
        "fixturePackageManifestMismatch",
        "does not match its manifest")]
    [TestCase(
        "fixtureMissingPackageEntry",
        "archive must contain exactly")]
    [TestCase(
        "fixtureMissingQualificationReceipt",
        "qualification archive is missing gate receipt")]
    [TestCase(
        "fixtureMalformedQualificationReceipt",
        "is missing required property")]
    [TestCase(
        "fixtureMissingQualificationGateEvidence",
        "qualification archive is missing gate evidence")]
    [TestCase(
        "fixtureUnsafePilotArchive",
        "archive contains an unsafe ZIP entry")]
    public async Task AuthenticatedArtifactsCannotBeMissingOrContradictClaims(
        string fixtureFlag,
        string expectedMessage)
    {
        var evidence = JsonNode.Parse(CreateEvidenceJson())!.AsObject();
        evidence[fixtureFlag] = true;
        using var workspace = await EvidenceWorkspace.CreateAsync(
            evidence.ToJsonString(s_indentedJsonOptions),
            annotatedTag: true);

        var result = await RunHumanGateAsync(
            workspace.Repository,
            Path.Combine(workspace.Root, "human-validation.json"),
            workspace.ProductCommit);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        AssertOutputContains(result.Output, expectedMessage);
    }

    [Test]
    public void WrappedCliXmlDiagnosticsRemainSearchable()
    {
        AssertOutputContains(
            """
            #< CLIXML
            <Objs Version="1.1.0.1" xmlns="http://schemas.microsoft.com/powershell/2004/04"><S S="Error">_x001B_[31;1mPilot artifact does not_x000A_</S><S S="Error">_x001B_[31;1m     | exactly match the declared evidence._x001B_[0m</S></Objs>
            """,
            "Pilot artifact does not exactly match");
    }

    [Test]
    public async Task ApprovedDocumentationOnlyChangePreservesReleaseIdentity()
    {
        var evidence = JsonNode.Parse(CreateEvidenceJson())!.AsObject();
        evidence["fixtureStableDocumentationChange"] = true;
        using var workspace = await EvidenceWorkspace.CreateAsync(
            evidence.ToJsonString(s_indentedJsonOptions),
            annotatedTag: true);

        var result = await RunHumanGateAsync(
            workspace.Repository,
            Path.Combine(workspace.Root, "human-validation.json"),
            workspace.ProductCommit);

        Assert.That(result.ExitCode, Is.Zero, result.Output);
    }

    [Test]
    public async Task ReleaseDigestsAreOrdinalAcrossProcessCultures()
    {
        using var workspace = await EvidenceWorkspace.CreateAsync(
            CreateEvidenceJson(),
            annotatedTag: true);

        var english = await RunDigestWithCultureAsync(
            workspace.Repository,
            workspace.ProductCommit,
            "en-US");
        var turkish = await RunDigestWithCultureAsync(
            workspace.Repository,
            workspace.ProductCommit,
            "tr-TR");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(english.ExitCode, Is.Zero, english.Output);
            Assert.That(turkish.ExitCode, Is.Zero, turkish.Output);
            Assert.That(
                turkish.Output.Trim(),
                Is.EqualTo(english.Output.Trim()));
        }
    }

    [TestCase("package")]
    [TestCase("runtime")]
    [TestCase("tool")]
    [TestCase("policy")]
    [TestCase("outcomes")]
    [TestCase("result")]
    [TestCase("workflow")]
    [TestCase("workflow-duplicate")]
    [TestCase("workflow-provider")]
    [TestCase("workflow-url-repository")]
    [TestCase("workflow-url-run")]
    [TestCase("workflow-url-attempt")]
    [TestCase("workflow-url-query")]
    [TestCase("workflow-api-run")]
    [TestCase("workflow-api-attempt")]
    [TestCase("workflow-api-repository")]
    [TestCase("workflow-api-source")]
    [TestCase("workflow-api-conclusion")]
    [TestCase("workflow-api-week")]
    [TestCase("workflow-api-not-found")]
    [TestCase("workflow-api-path")]
    [TestCase("workflow-api-artifact-time")]
    [TestCase("qualification")]
    [TestCase("reason-counts")]
    [TestCase("reason-name")]
    [TestCase("evidence-use")]
    [TestCase("compiler-input")]
    [TestCase("claim-manifest")]
    [TestCase("pilot-source")]
    [TestCase("pilot-repository")]
    [TestCase("duplicate-pilot-repository")]
    [TestCase("zero-placeholder")]
    [TestCase("rc-commit")]
    [TestCase("rc-tag")]
    [TestCase("lightweight-rc-tag")]
    [TestCase("future-cycle")]
    [TestCase("pre-qualification-cycle")]
    [TestCase("pre-rc-qualified-at")]
    [TestCase("computed-digest")]
    [TestCase("stable-product-change")]
    [TestCase("stable-release-control-change")]
    [TestCase("stable-test-change")]
    [TestCase("placeholder-url")]
    [TestCase("trailing-dot-url")]
    [TestCase("localhost-url")]
    [TestCase("ipv4-loopback-url")]
    [TestCase("ipv4-private-url")]
    [TestCase("ipv4-link-local-url")]
    [TestCase("ipv6-loopback-url")]
    [TestCase("ipv6-private-url")]
    [TestCase("ipv6-link-local-url")]
    public async Task InvalidBoundPilotEvidenceFails(string invalidSection)
    {
        var evidence = JsonNode.Parse(CreateEvidenceJson())!.AsObject();
        CorruptEvidence(evidence, invalidSection);
        using var workspace = await EvidenceWorkspace.CreateAsync(
            evidence.ToJsonString(s_indentedJsonOptions),
            annotatedTag: true);
        var validationPath = Path.Combine(
            workspace.Root,
            "human-validation.json");

        var result = await RunHumanGateAsync(
            workspace.Repository,
            validationPath,
            workspace.ProductCommit);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        Assert.That(File.Exists(validationPath), Is.False);
    }

    [Test]
    public async Task LightweightEvidenceTagIsRejected()
    {
        using var workspace = await EvidenceWorkspace.CreateAsync(
            CreateEvidenceJson(),
            annotatedTag: false);

        var result = await RunHumanGateAsync(
            workspace.Repository,
            Path.Combine(workspace.Root, "human-validation.json"),
            workspace.ProductCommit);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        AssertOutputContains(result.Output, "must be an annotated tag");
    }

    [Test]
    public async Task QualificationStatesDoNotClaimSuccessEarly()
    {
        using var workspace = TemporaryWorkspace.Create(
            "SharpProof.ReleaseQualification");
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");

        var running = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1");
        Assert.That(running.ExitCode, Is.Zero, running.Output);
        using (var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath)))
        {
            Assert.That(
                document.RootElement.GetProperty("status").GetString(),
                Is.EqualTo("running"));
            Assert.That(
                document.RootElement.GetProperty("gates")
                    .GetProperty("acceptance")
                    .GetString(),
                Is.EqualTo("pending"));
        }

        var repeatedInitialization = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1");
        Assert.That(
            repeatedInitialization.ExitCode,
            Is.Not.Zero,
            repeatedInitialization.Output);

        var directPackagePass = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1",
            "-Gate",
            "package",
            "-GateStatus",
            "passed");
        Assert.That(
            directPackagePass.ExitCode,
            Is.Not.Zero,
            directPackagePass.Output);
        var packageRunning = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1",
            "-Gate",
            "package",
            "-GateStatus",
            "running");
        Assert.That(packageRunning.ExitCode, Is.Zero, packageRunning.Output);
        var packagePassed = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1",
            "-Gate",
            "package",
            "-GateStatus",
            "passed");
        Assert.That(packagePassed.ExitCode, Is.Zero, packagePassed.Output);
        var acceptanceRunning = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1",
            "-Gate",
            "acceptance",
            "-GateStatus",
            "running");
        Assert.That(
            acceptanceRunning.ExitCode,
            Is.Zero,
            acceptanceRunning.Output);

        var prematurePass = await RunQualificationAsync(
            qualificationPath,
            "passed",
            "v1.0.0-preview.1",
            "-CoverageBaselineCommit",
            BaselineCommit);
        Assert.That(
            prematurePass.ExitCode,
            Is.Not.Zero,
            prematurePass.Output);

        var failed = await RunQualificationAsync(
            qualificationPath,
            "failed",
            "v1.0.0-preview.1",
            "-FailureKind",
            "test-failure");
        Assert.That(failed.ExitCode, Is.Zero, failed.Output);
        var repeatedFailure = await RunQualificationAsync(
            qualificationPath,
            "failed",
            "v1.0.0-preview.1",
            "-FailureKind",
            "generic-workflow-failure");
        Assert.That(
            repeatedFailure.ExitCode,
            Is.Zero,
            repeatedFailure.Output);
        using (var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath)))
        {
            Assert.That(
                document.RootElement.GetProperty("status").GetString(),
                Is.EqualTo("failed"));
            Assert.That(
                document.RootElement.GetProperty("gates")
                    .GetProperty("package")
                    .GetString(),
                Is.EqualTo("passed"));
            Assert.That(
                document.RootElement.GetProperty("gates")
                    .GetProperty("acceptance")
                    .GetString(),
                Is.EqualTo("failed"));
            Assert.That(
                document.RootElement.GetProperty("gates")
                    .GetProperty("coverage")
                    .GetString(),
                Is.EqualTo("not-run"));
            Assert.That(
                document.RootElement.GetProperty("gates")
                    .GetProperty("humanEvidence")
                    .GetString(),
                Is.EqualTo("not-required"));
        }
    }

    [Test]
    public async Task FailedFinalQualificationKeepsHumanStateConsistent()
    {
        using var workspace = TemporaryWorkspace.Create(
            "SharpProof.FailedFinalQualification");
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");
        var initialized = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0");
        Assert.That(initialized.ExitCode, Is.Zero, initialized.Output);
        var acceptanceRunning = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0",
            "-Gate",
            "acceptance",
            "-GateStatus",
            "running");
        Assert.That(
            acceptanceRunning.ExitCode,
            Is.Zero,
            acceptanceRunning.Output);

        var failed = await RunQualificationAsync(
            qualificationPath,
            "failed",
            "v1.0.0",
            "-FailureKind",
            "test-failure");
        Assert.That(failed.ExitCode, Is.Zero, failed.Output);
        using var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath));
        Assert.That(
            document.RootElement.GetProperty("gates")
                .GetProperty("humanEvidence")
                .GetString(),
            Is.EqualTo("not-run"));
        Assert.That(
            document.RootElement.GetProperty("humanEvidence")
                .GetProperty("status")
                .GetString(),
            Is.EqualTo("not-run"));
    }

    [Test]
    public async Task FinalQualificationRequiresValidatedExternalEvidence()
    {
        using var workspace = await EvidenceWorkspace.CreateAsync(
            CreateEvidenceJson(),
            annotatedTag: true);
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");
        var initialized = await RunQualificationForCommitAsync(
            qualificationPath,
            "running",
            "v1.0.0",
            workspace.ProductCommit);
        Assert.That(initialized.ExitCode, Is.Zero, initialized.Output);
        await PassAutomatedQualificationGatesAsync(
            qualificationPath,
            "v1.0.0",
            workspace.ProductCommit);

        var missingEvidence = await RunQualificationForCommitAsync(
            qualificationPath,
            "passed",
            "v1.0.0",
            workspace.ProductCommit,
            "-CoverageBaselineCommit",
            BaselineCommit);
        Assert.That(
            missingEvidence.ExitCode,
            Is.Not.Zero,
            missingEvidence.Output);

        var humanValidationPath = Path.Combine(
            workspace.Root,
            "human-validation.json");
        var validation = await RunHumanGateAsync(
            workspace.Repository,
            humanValidationPath,
            workspace.ProductCommit);
        Assert.That(validation.ExitCode, Is.Zero, validation.Output);
        using var validationDocument = JsonDocument.Parse(
            await File.ReadAllBytesAsync(humanValidationPath));
        var documentSha256 = validationDocument.RootElement
            .GetProperty("evidenceDocumentSha256")
            .GetString();

        var humanRunning = await RunQualificationForCommitAsync(
            qualificationPath,
            "running",
            "v1.0.0",
            workspace.ProductCommit,
            "-Gate",
            "humanEvidence",
            "-GateStatus",
            "running");
        Assert.That(humanRunning.ExitCode, Is.Zero, humanRunning.Output);
        var humanPassed = await RunQualificationForCommitAsync(
            qualificationPath,
            "running",
            "v1.0.0",
            workspace.ProductCommit,
            "-Gate",
            "humanEvidence",
            "-GateStatus",
            "passed",
            "-HumanEvidencePath",
            humanValidationPath);
        Assert.That(humanPassed.ExitCode, Is.Zero, humanPassed.Output);

        var passed = await RunQualificationForCommitAsync(
            qualificationPath,
            "passed",
            "v1.0.0",
            workspace.ProductCommit,
            "-CoverageBaselineCommit",
            BaselineCommit,
            "-HumanEvidencePath",
            humanValidationPath,
            "-HumanEvidenceRepository",
            workspace.Repository);
        Assert.That(passed.ExitCode, Is.Zero, passed.Output);
        using var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath));
        Assert.That(
            document.RootElement.GetProperty("status").GetString(),
            Is.EqualTo("passed"));
        Assert.That(
            document.RootElement.GetProperty("gates")
                .EnumerateObject()
                .Select(property => property.Value.GetString()),
            Is.All.EqualTo("passed"));
        Assert.That(
            document.RootElement.GetProperty("humanEvidence")
                .GetProperty("status")
                .GetString(),
            Is.EqualTo("passed"));
        Assert.That(
            document.RootElement.GetProperty("humanEvidence")
                .GetProperty("documentSha256")
                .GetString(),
            Is.EqualTo(documentSha256));
        Assert.That(
            document.RootElement.GetProperty("humanEvidence")
                .GetProperty("qualifiedRc")
                .GetProperty("packageVersion")
                .GetString(),
            Is.EqualTo(QualifiedRcVersion));
    }

    [Test]
    public async Task ForgedQualificationProgressCannotPass()
    {
        using var workspace = await EvidenceWorkspace.CreateAsync(
            CreateEvidenceJson(),
            annotatedTag: true);
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");
        var initialized = await RunQualificationForCommitAsync(
            qualificationPath,
            "running",
            "v1.0.0",
            workspace.ProductCommit);
        Assert.That(initialized.ExitCode, Is.Zero, initialized.Output);

        var humanValidationPath = Path.Combine(
            workspace.Root,
            "human-validation.json");
        var validation = await RunHumanGateAsync(
            workspace.Repository,
            humanValidationPath,
            workspace.ProductCommit);
        Assert.That(validation.ExitCode, Is.Zero, validation.Output);
        foreach (var status in new[] { "running", "passed" })
        {
            var transitionArguments = new List<string>
            {
                "-Gate",
                "humanEvidence",
                "-GateStatus",
                status
            };
            if (status == "passed")
            {
                transitionArguments.Add("-HumanEvidencePath");
                transitionArguments.Add(humanValidationPath);
            }
            var transition = await RunQualificationForCommitAsync(
                qualificationPath,
                "running",
                "v1.0.0",
                workspace.ProductCommit,
                transitionArguments.ToArray());
            Assert.That(transition.ExitCode, Is.Zero, transition.Output);
        }

        var forged = JsonNode.Parse(
            await File.ReadAllTextAsync(qualificationPath))!.AsObject();
        foreach (var gateName in forged["gates"]!
                     .AsObject()
                     .Select(gate => gate.Key)
                     .Where(gate => gate != "humanEvidence")
                     .ToArray())
        {
            forged["gates"]![gateName] = "passed";
            forged["gateReceipts"]![gateName] = Digest;
        }
        await File.WriteAllTextAsync(
            qualificationPath,
            forged.ToJsonString(s_indentedJsonOptions));

        var result = await RunQualificationForCommitAsync(
            qualificationPath,
            "passed",
            "v1.0.0",
            workspace.ProductCommit,
            "-CoverageBaselineCommit",
            BaselineCommit,
            "-HumanEvidencePath",
            humanValidationPath,
            "-HumanEvidenceRepository",
            workspace.Repository);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        AssertOutputContains(result.Output, "receipt");
    }

    [Test]
    public async Task CoordinatedLocalReceiptForgeryCannotReplaceSealedArtifact()
    {
        using var workspace = TemporaryWorkspace.Create(
            "SharpProof.SealedQualificationReceipt");
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");
        var initialized = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1");
        Assert.That(initialized.ExitCode, Is.Zero, initialized.Output);
        await PassAutomatedQualificationGatesAsync(
            qualificationPath,
            "v1.0.0-preview.1");
        var immutableReceipts = SnapshotImmutableReceipts(
            qualificationPath);

        var receiptPath = Path.Combine(
            workspace.Root,
            "qualification-receipts",
            "package.json");
        var receipt = JsonNode.Parse(
            await File.ReadAllTextAsync(receiptPath))!.AsObject();
        receipt["evidence"]!["sha256"] = Digest;
        await File.WriteAllTextAsync(
            receiptPath,
            receipt.ToJsonString(s_indentedJsonOptions) + "\n",
            new System.Text.UTF8Encoding(false));
        var forgedReceiptDigest = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(receiptPath)));
        var qualification = JsonNode.Parse(
            await File.ReadAllTextAsync(qualificationPath))!.AsObject();
        qualification["gateReceipts"]!["package"] =
            forgedReceiptDigest;
        await File.WriteAllTextAsync(
            qualificationPath,
            qualification.ToJsonString(s_indentedJsonOptions) + "\n",
            new System.Text.UTF8Encoding(false));

        var result = await RunQualificationAsync(
            qualificationPath,
            "passed",
            "v1.0.0-preview.1",
            "-CoverageBaselineCommit",
            BaselineCommit,
            "-ImmutableReceiptDirectory",
            immutableReceipts);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        AssertOutputContains(
            result.Output,
            "immutable GitHub Actions artifact");
    }

    [Test]
    public async Task FailureCanBeRecordedBeforeInitialization()
    {
        using var workspace = TemporaryWorkspace.Create(
            "SharpProof.PreInitializationFailure");
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");

        var result = await RunQualificationAsync(
            qualificationPath,
            "failed",
            "v1.0.0-preview.1",
            "-FailureKind",
            "setup-failed");

        Assert.That(result.ExitCode, Is.Zero, result.Output);
        using var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath));
        Assert.That(
            document.RootElement.GetProperty("status").GetString(),
            Is.EqualTo("failed"));
        Assert.That(
            document.RootElement.GetProperty("gates")
                .GetProperty("package")
                .GetString(),
            Is.EqualTo("not-run"));
        Assert.That(
            document.RootElement.GetProperty("gates")
                .GetProperty("humanEvidence")
                .GetString(),
            Is.EqualTo("not-required"));
    }

    private static async Task PassAutomatedQualificationGatesAsync(
        string qualificationPath,
        string tag,
        string releaseCommit = ProductCommit)
    {
        var gates = new[]
        {
            "package",
            "packageConsumers",
            "minimumSdkConsumer",
            "security",
            "attestation",
            "coverageBaseline",
            "lockedRestore",
            "acceptance",
            "fuzz",
            "mutations",
            "corpus",
            "performance",
            "coverage",
            "dependencyAudit"
        };
        foreach (var gate in gates)
        {
            var running = await RunQualificationForCommitAsync(
                qualificationPath,
                "running",
                tag,
                releaseCommit,
                "-Gate",
                gate,
                "-GateStatus",
                "running",
                "-CoverageBaselineCommit",
                BaselineCommit);
            Assert.That(running.ExitCode, Is.Zero, running.Output);
            var passed = await RunQualificationForCommitAsync(
                qualificationPath,
                "running",
                tag,
                releaseCommit,
                "-Gate",
                gate,
                "-GateStatus",
                "passed",
                "-CoverageBaselineCommit",
                BaselineCommit);
            Assert.That(passed.ExitCode, Is.Zero, passed.Output);
        }
    }

    private static string SnapshotImmutableReceipts(
        string qualificationPath)
    {
        var outputDirectory = Path.GetDirectoryName(qualificationPath)!;
        var receiptDirectory = Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(qualificationPath) + "-receipts");
        var immutableDirectory = Path.Combine(
            outputDirectory,
            "immutable-receipts");
        Directory.CreateDirectory(immutableDirectory);
        if (!Directory.Exists(receiptDirectory))
        {
            return immutableDirectory;
        }
        foreach (var receiptPath in Directory.GetFiles(
                     receiptDirectory,
                     "*.json"))
        {
            File.Copy(
                receiptPath,
                Path.Combine(
                    immutableDirectory,
                    Path.GetFileName(receiptPath)),
                overwrite: true);
        }
        return immutableDirectory;
    }

    private static void CorruptEvidence(
        JsonObject evidence,
        string invalidSection)
    {
        var pilot = evidence["pilots"]![0]!.AsObject();
        var cycle = pilot["weeklyCycles"]![0]!.AsObject();
        switch (invalidSection)
        {
            case "package":
                pilot["package"]!["artifacts"]![0]!["sha256"] = "invalid";
                break;
            case "runtime":
                pilot["runtime"]!["architecture"] = "arm64";
                break;
            case "tool":
                pilot["tool"]!["productCommit"] = OtherCommit;
                break;
            case "policy":
                pilot["policy"]!["profile"] = "advisory";
                break;
            case "outcomes":
                cycle["outcomes"]!["proven"] = 49;
                cycle["outcomes"]!["unknown"] = 1;
                break;
            case "result":
                cycle["result"]!["runStatus"] = "Failed";
                break;
            case "workflow":
                cycle["workflow"]!["runId"] = 0;
                break;
            case "workflow-duplicate":
                pilot["weeklyCycles"]![1]!["workflow"] =
                    cycle["workflow"]!.DeepClone();
                break;
            case "workflow-provider":
                cycle["workflow"]!["provider"] = "other";
                break;
            case "workflow-url-repository":
                cycle["workflow"]!["evidenceUrl"] =
                    "https://github.com/owner/other/actions/runs/1/" +
                    "attempts/1";
                break;
            case "workflow-url-run":
                cycle["workflow"]!["evidenceUrl"] =
                    "https://github.com/owner/pilot-a/actions/runs/2/" +
                    "attempts/1";
                break;
            case "workflow-url-attempt":
                cycle["workflow"]!["evidenceUrl"] =
                    "https://github.com/owner/pilot-a/actions/runs/1/" +
                    "attempts/2";
                break;
            case "workflow-url-query":
                cycle["workflow"]!["evidenceUrl"] =
                    "https://github.com/owner/pilot-a/actions/runs/1/" +
                    "attempts/1?check=1";
                break;
            case "workflow-api-run":
                SetWorkflowRun(cycle, 906);
                break;
            case "workflow-api-attempt":
                SetWorkflowRun(cycle, 904);
                break;
            case "workflow-api-repository":
                SetWorkflowRun(cycle, 903);
                break;
            case "workflow-api-source":
                SetWorkflowRun(cycle, 905);
                break;
            case "workflow-api-conclusion":
                SetWorkflowRun(cycle, 902);
                break;
            case "workflow-api-week":
                SetWorkflowRun(cycle, 901);
                break;
            case "workflow-api-not-found":
                SetWorkflowRun(cycle, 999);
                break;
            case "workflow-api-path":
                SetWorkflowRun(cycle, 907);
                break;
            case "workflow-api-artifact-time":
                SetWorkflowRun(cycle, 908);
                break;
            case "qualification":
                evidence["qualification"]!["stableCandidate"]![
                    "trustedComputingBaseDigestSha256"] = new string('b', 64);
                break;
            case "reason-counts":
                cycle["outcomes"]!["reasonCounts"]![0]!["count"] = 49;
                break;
            case "reason-name":
                cycle["outcomes"]!["reasonCounts"]![0]!["reason"] =
                    "Proven";
                break;
            case "evidence-use":
                cycle["evidenceUse"]!["trustedEvidence"] = new JsonArray(
                    new JsonObject
                    {
                        ["identity"] = "trusted-boundary"
                    });
                break;
            case "compiler-input":
                pilot["weeklyCycles"]![1]!["result"]!["inputSha256"] =
                    new string('b', 64);
                break;
            case "claim-manifest":
                pilot["weeklyCycles"]![1]!["result"]![
                    "claimManifestSha256"] = new string('b', 64);
                break;
            case "pilot-source":
                pilot["weeklyCycles"]![1]!["workflow"]!["sourceCommit"] =
                    ProductCommit;
                break;
            case "pilot-repository":
                pilot["weeklyCycles"]![1]!["workflow"]!["repository"] =
                    "owner/other-pilot";
                break;
            case "duplicate-pilot-repository":
                {
                    foreach (var secondCycle in
                        evidence["pilots"]![1]!["weeklyCycles"]!.AsArray())
                    {
                        secondCycle!["workflow"]!["repository"] =
                            "owner/pilot-a";
                    }
                    break;
                }
            case "zero-placeholder":
                pilot["package"]!["releaseManifestSha256"] =
                    new string('0', 64);
                break;
            case "rc-commit":
                evidence["qualification"]!["qualifiedRc"]![
                    "productCommit"] = OtherCommit;
                break;
            case "rc-tag":
                evidence["qualification"]!["qualifiedRc"]!["releaseTag"] =
                    "v1.0.0-rc.999";
                evidence["qualification"]!["qualifiedRc"]![
                    "packageVersion"] = "1.0.0-rc.999";
                break;
            case "lightweight-rc-tag":
                evidence["fixtureLightweightRcTag"] = true;
                break;
            case "future-cycle":
                cycle["weekEnding"] = "2100-01-01";
                break;
            case "pre-qualification-cycle":
                cycle["weekEnding"] = "2019-12-31";
                break;
            case "pre-rc-qualified-at":
                evidence["qualification"]!["qualifiedRc"]![
                    "qualifiedAtUtc"] = "2019-01-01T00:00:00Z";
                break;
            case "computed-digest":
                evidence["qualification"]!["qualifiedRc"]![
                    "productionDigestSha256"] = new string('b', 64);
                evidence["qualification"]!["qualifiedRc"]![
                    "trustedComputingBaseDigestSha256"] =
                    new string('b', 64);
                evidence["qualification"]!["stableCandidate"]![
                    "productionDigestSha256"] = new string('b', 64);
                evidence["qualification"]!["stableCandidate"]![
                    "trustedComputingBaseDigestSha256"] =
                    new string('b', 64);
                break;
            case "stable-product-change":
                evidence["fixtureStableProductChange"] = true;
                break;
            case "stable-release-control-change":
                evidence["fixtureStableReleaseControlChange"] = true;
                break;
            case "stable-test-change":
                evidence["fixtureStableTestChange"] = true;
                break;
            case "placeholder-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://evidence.example.org/governance";
                break;
            case "trailing-dot-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://example.com./governance";
                break;
            case "localhost-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://localhost/governance";
                break;
            case "ipv4-loopback-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://127.0.0.1/governance";
                break;
            case "ipv4-private-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://192.168.1.1/governance";
                break;
            case "ipv4-link-local-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://169.254.1.1/governance";
                break;
            case "ipv6-loopback-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://[::1]/governance";
                break;
            case "ipv6-private-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://[fc00::1]/governance";
                break;
            case "ipv6-link-local-url":
                evidence["governance"]!["evidenceUrl"] =
                    "https://[fe80::1]/governance";
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(invalidSection),
                    invalidSection,
                    "Unknown evidence mutation.");
        }
    }

    private static void SetWorkflowRun(JsonObject cycle, long runId)
    {
        var workflow = cycle["workflow"]!.AsObject();
        workflow["runId"] = runId;
        workflow["evidenceUrl"] =
            "https://github.com/owner/pilot-a/actions/runs/" +
            $"{runId}/attempts/1";
    }

    private static string CreateEvidenceJson()
    {
        var evidence = new
        {
            schemaVersion = 4,
            releaseTag = "v1.0.0",
            productCommit = ProductCommit,
            evidenceRef = "refs/tags/evidence/v1.0.0",
            qualification = new
            {
                qualifiedRc = new
                {
                    releaseTag = $"v{QualifiedRcVersion}",
                    productCommit = QualifiedRcCommit,
                    packageVersion = QualifiedRcVersion,
                    productionDigestSha256 = Digest,
                    trustedComputingBaseDigestSha256 = Digest,
                    qualifiedAtUtc = "2020-01-02T02:00:00Z",
                    package = CreatePackageEvidence(),
                    workflow = new
                    {
                        provider = "github-actions",
                        repository = "alexyorke/SharpProof",
                        name = "Cross-platform package consumers",
                        path = ".github/workflows/package-consumers.yml",
                        @event = "push",
                        runId = 201,
                        runAttempt = 1,
                        evidenceUrl =
                            "https://github.com/alexyorke/SharpProof/" +
                            "actions/runs/201/attempts/1",
                        qualificationArtifactSha256 = Digest,
                        qualificationRecordSha256 = Digest,
                        packageArtifactSha256 = Digest
                    }
                },
                stableCandidate = new
                {
                    productCommit = ProductCommit,
                    packageVersion = "1.0.0",
                    productionDigestSha256 = Digest,
                    trustedComputingBaseDigestSha256 = Digest,
                    approvedMetadataDifferences = new[]
                    {
                        "version",
                        "changelog",
                        "release-metadata"
                    }
                }
            },
            pilots = new[]
            {
                CreatePilot("pilot-a", 1),
                CreatePilot("pilot-b", 101)
            },
            openDefects = new
            {
                p0 = 0,
                p1 = 0,
                evidenceUrl =
                    "https://github.com/alexyorke/SharpProof/issues"
            },
            soundnessReviews = new[]
            {
                new
                {
                    reviewer = "reviewer-a",
                    independent = true,
                    productCommit = ProductCommit,
                    disposition = "approved",
                    evidenceUrl =
                        "https://github.com/alexyorke/SharpProof/pull/1"
                },
                new
                {
                    reviewer = "reviewer-b",
                    independent = true,
                    productCommit = ProductCommit,
                    disposition = "approved",
                    evidenceUrl =
                        "https://github.com/alexyorke/SharpProof/pull/2"
                }
            },
            governance = new
            {
                protectedDefaultBranch = true,
                protectedReleaseTags = true,
                protectedPublishingEnvironments = true,
                requiredChecks = true,
                independentReviewRequired = true,
                evidenceUrl =
                    "https://github.com/alexyorke/SharpProof/settings/rules"
            }
        };
        return JsonSerializer.Serialize(
            evidence,
            s_indentedJsonOptions);
    }

    private static object CreatePilot(string id, int firstRunId)
    {
        return new
        {
            id,
            selectedClaims = 50,
            package = CreatePackageEvidence(),
            runtime = new
            {
                operatingSystem = "windows",
                architecture = "x64",
                dotnetSdkVersion = "9.0.300",
                dotnetRuntimeVersion = "9.0.0",
                roslynVersion = "4.14.0"
            },
            tool = new
            {
                productCommit = QualifiedRcCommit,
                workerVersion = QualifiedRcVersion,
                protocolVersion = "9",
                manifestSchemaVersion = 4,
                compilerArtifactSchemaVersion = 9,
                workerAssemblySha256 = Digest,
                runtimeClosureSha256 = Digest,
                specificationCatalogSha256 = Digest
            },
            policy = new
            {
                profile = "strict",
                features = "all",
                verifyPolicy = "require-proven",
                assumptionPolicy = "error"
            },
            weeklyCycles = Enumerable.Range(0, 4)
                .Select(index => new
                {
                    weekEnding = new DateOnly(2020, 1, 12)
                        .AddDays(index * 7)
                        .ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture),
                    outcomes = new
                    {
                        selectedClaims = 50,
                        proven = 50,
                        refuted = 0,
                        unknown = 0,
                        assumptions = 0,
                        trustedEvidence = 0,
                        infrastructureFailures = 0,
                        reasonCounts = new[]
                        {
                            new
                            {
                                reason = "None",
                                count = 50
                            }
                        }
                    },
                    evidenceUse = new
                    {
                        assumptions = Array.Empty<object>(),
                        trustedEvidence = Array.Empty<object>()
                    },
                    result = new
                    {
                        format = "SharpProof.WorkerVerifyResponse",
                        runStatus = "Complete",
                        sha256 = Digest,
                        requestHash = Digest,
                        inputSha256 = Digest,
                        claimManifestSha256 = Digest
                    },
                    workflow = new
                    {
                        provider = "github-actions",
                        repository = $"owner/{id}",
                        name = "SharpProof strict weekly",
                        path =
                            ".github/workflows/sharpproof-strict-weekly.yml",
                        @event = "workflow_dispatch",
                        runId = firstRunId + index,
                        runAttempt = 1,
                        sourceCommit = OtherCommit,
                        evidenceUrl =
                            "https://github.com/owner/" +
                            $"{id}/actions/runs/{firstRunId + index}/" +
                            "attempts/1",
                        artifactSha256 = Digest,
                        recordSha256 = Digest
                    }
                })
                .ToArray()
        };
    }

    private static object CreatePackageEvidence()
    {
        return new
        {
            version = QualifiedRcVersion,
            releaseManifestSha256 = Digest,
            artifacts = new[]
            {
                new
                {
                    id = "SharpProof.Attributes",
                    sha256 = Digest
                },
                new
                {
                    id = "SharpProof",
                    sha256 = Digest
                },
                new
                {
                    id = "SharpProof.Verifier.Win-x64",
                    sha256 = Digest
                }
            }
        };
    }

    private static Task<ProcessResult> RunHumanGateAsync(
        string evidenceRepository,
        string validationPath,
        string expectedProductCommit)
    {
        return RunPowerShellScriptWithGitHubMockAsync(
            FindRepositoryRoot(),
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Test-SharpProofHumanReleaseGates.ps1"),
            "-ExpectedProductCommit",
            expectedProductCommit,
            "-EvidenceRepository",
            evidenceRepository,
            "-OutputPath",
            validationPath);
    }

    private static Task<ProcessResult> RunDigestWithCultureAsync(
        string repository,
        string commit,
        string culture)
    {
        var digestScript = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "Get-SharpProofReleaseDigests.ps1");
        var command =
            "[Globalization.CultureInfo]::CurrentCulture = " +
            $"[Globalization.CultureInfo]::GetCultureInfo({Ps(culture)}); " +
            $"& {Ps(digestScript)} " +
            $"-RepositoryPath {Ps(repository)} " +
            $"-Commit {Ps(commit)}";
        var encodedCommand = Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(command));
        return RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-EncodedCommand",
            encodedCommand);
    }

    private static string Ps(string value)
    {
        return "'" + value.Replace(
            "'",
            "''",
            StringComparison.Ordinal) + "'";
    }

    private static Task<ProcessResult> RunQualificationAsync(
        string outputPath,
        string status,
        string tag,
        params string[] additionalArguments)
    {
        return RunQualificationForCommitAsync(
            outputPath,
            status,
            tag,
            ProductCommit,
            additionalArguments);
    }

    private static Task<ProcessResult> RunQualificationForCommitAsync(
        string outputPath,
        string status,
        string tag,
        string releaseCommit,
        params string[] additionalArguments)
    {
        var effectiveArguments = PrepareQualificationArguments(
            outputPath,
            status,
            releaseCommit,
            additionalArguments);
        var arguments = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Write-SharpProofReleaseQualification.ps1"),
            "-Status",
            status,
            "-Tag",
            tag,
            "-ReleaseCommit",
            releaseCommit,
            "-OutputPath",
            outputPath
        };
        arguments.AddRange(effectiveArguments);
        return RunPowerShellScriptWithGitHubMockAsync(
            FindRepositoryRoot(),
            arguments[3],
            arguments.Skip(4).ToArray());
    }

    private static List<string> PrepareQualificationArguments(
        string outputPath,
        string status,
        string releaseCommit,
        string[] additionalArguments)
    {
        var effectiveArguments = additionalArguments.ToList();
        var gateStatusIndex = effectiveArguments.IndexOf("-GateStatus");
        if (gateStatusIndex >= 0 &&
            gateStatusIndex + 1 < effectiveArguments.Count &&
            effectiveArguments[gateStatusIndex + 1] == "passed" &&
            !effectiveArguments.Contains(
                "-GateEvidencePath",
                StringComparer.Ordinal))
        {
            var gateIndex = effectiveArguments.IndexOf("-Gate");
            Assert.That(gateIndex, Is.GreaterThanOrEqualTo(0));
            var outputDirectory = Path.GetDirectoryName(outputPath)!;
            var evidenceDirectory = Path.Combine(
                outputDirectory,
                "gate-evidence");
            Directory.CreateDirectory(evidenceDirectory);
            var evidencePath = Path.Combine(
                evidenceDirectory,
                effectiveArguments[gateIndex + 1] + ".json");
            File.WriteAllText(
                evidencePath,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    gate = effectiveArguments[gateIndex + 1],
                    releaseCommit
                }) + "\n",
                new System.Text.UTF8Encoding(false));
            effectiveArguments.Add("-GateEvidencePath");
            effectiveArguments.Add(evidencePath);
        }
        if (status == "passed" &&
            !effectiveArguments.Contains(
                "-ImmutableReceiptDirectory",
                StringComparer.Ordinal))
        {
            var immutableDirectory =
                SnapshotImmutableReceipts(outputPath);
            effectiveArguments.Add("-ImmutableReceiptDirectory");
            effectiveArguments.Add(immutableDirectory);
        }
        return effectiveArguments;
    }

    private static Task<ProcessResult> RunPowerShellScriptWithGitHubMockAsync(
        string workingDirectory,
        string scriptPath,
        params string[] arguments)
    {
        var scriptInvocation =
            "& " + Ps(scriptPath) + " " +
            string.Join(
                " ",
                arguments.Select(argument =>
                    argument.Length > 0 &&
                    argument[0] == '-'
                        ? argument
                        : Ps(argument)));
        var command = GitHubApiMock + Environment.NewLine +
            "$env:SHARPPROOF_GITHUB_TOKEN = " +
            Ps(GitHubApiToken) + Environment.NewLine;
        var evidenceRepositoryIndex = Array.FindIndex(
            arguments,
            argument => argument.Equals(
                "-EvidenceRepository",
                StringComparison.OrdinalIgnoreCase));
        string? artifactDirectory = null;
        if (evidenceRepositoryIndex >= 0 &&
            evidenceRepositoryIndex + 1 < arguments.Length)
        {
            var evidenceRepository = arguments[
                evidenceRepositoryIndex + 1];
            artifactDirectory = Path.Combine(
                Path.GetDirectoryName(evidenceRepository)!,
                "github-artifacts");
        }
        var humanEvidencePathIndex = Array.FindIndex(
            arguments,
            argument => argument.Equals(
                "-HumanEvidencePath",
                StringComparison.OrdinalIgnoreCase));
        if (artifactDirectory == null &&
            humanEvidencePathIndex >= 0 &&
            humanEvidencePathIndex + 1 < arguments.Length)
        {
            artifactDirectory = Path.Combine(
                Path.GetDirectoryName(
                    arguments[humanEvidencePathIndex + 1])!,
                "github-artifacts");
        }
        if (artifactDirectory != null)
        {
            command +=
                "$env:SHARPPROOF_TEST_GITHUB_ARTIFACT_DIRECTORY = " +
                Ps(artifactDirectory) +
                Environment.NewLine +
                "$global:SharpProofTestGitHubArtifactDirectory = " +
                Ps(artifactDirectory) +
                Environment.NewLine;
        }
        var tagIndex = Array.IndexOf(arguments, "-Tag");
        var releaseCommitIndex = Array.IndexOf(arguments, "-ReleaseCommit");
        if (tagIndex >= 0 &&
            tagIndex + 1 < arguments.Length &&
            releaseCommitIndex >= 0 &&
            releaseCommitIndex + 1 < arguments.Length)
        {
            var tag = arguments[tagIndex + 1];
            var releaseCommit = arguments[releaseCommitIndex + 1];
            var environment = new Dictionary<string, string>
            {
                ["GITHUB_ACTIONS"] = "true",
                ["GITHUB_REPOSITORY"] = "alexyorke/SharpProof",
                ["GITHUB_RUN_ID"] = "123456",
                ["GITHUB_RUN_ATTEMPT"] = "1",
                ["GITHUB_WORKFLOW_REF"] =
                    "alexyorke/SharpProof/.github/workflows/" +
                    $"package-consumers.yml@refs/tags/{tag}",
                ["GITHUB_JOB"] = "release-qualification",
                ["GITHUB_REF"] = $"refs/tags/{tag}",
                ["GITHUB_REF_NAME"] = tag,
                ["GITHUB_SHA"] = releaseCommit
            };
            command += string.Join(
                Environment.NewLine,
                environment.Select(pair =>
                    $"$env:{pair.Key} = {Ps(pair.Value)}")) +
                Environment.NewLine;
        }
        command += scriptInvocation;
        var encodedCommand = Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(command));
        return RunProcessAsync(
            workingDirectory,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-EncodedCommand",
            encodedCommand);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        return await RunProcessWithEnvironmentAsync(
            workingDirectory,
            fileName,
            environment: null,
            arguments);
    }

    private static async Task<ProcessResult> RunProcessWithEnvironmentAsync(
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
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (environment != null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + Environment.NewLine +
            (await standardError));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            typeof(HumanReleaseGateScriptTests).Assembly.Location);
        while (directory != null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "SharpProof.Release.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Repository root was not found.");
    }

    private static void AssertOutputContains(
        string output,
        string expectedMessage)
    {
        Assert.That(
            NormalizeProcessOutput(output),
            Does.Contain(expectedMessage),
            output);
    }

    private static string NormalizeProcessOutput(string output)
    {
        var normalized = output;
        var clixmlStart = output.IndexOf(
            "<Objs ",
            StringComparison.Ordinal);
        if (clixmlStart >= 0)
        {
            try
            {
                var document = XDocument.Parse(
                    output[clixmlStart..],
                    LoadOptions.None);
                normalized = string.Join(
                    " ",
                    document
                        .Descendants()
                        .Where(element =>
                            element.Name.LocalName == "S")
                        .Select(element => element.Value));
            }
            catch (System.Xml.XmlException)
            {
                // Preserve the raw process output for assertion diagnostics.
            }
        }

        normalized = normalized
            .Replace(
                "_x000A_",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                "_x000D_",
                "\r",
                StringComparison.Ordinal)
            .Replace(
                "_x001B_",
                "\u001B",
                StringComparison.Ordinal);
        normalized = AnsiEscapeSequence().Replace(
            normalized,
            string.Empty);
        return string.Join(
            " ",
            normalized
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(token =>
                    !string.Equals(
                        token,
                        "|",
                        StringComparison.Ordinal)));
    }

    [GeneratedRegex(
        "\u001B\\[[0-?]*[ -/]*[@-~]",
        RegexOptions.CultureInvariant)]
    private static partial Regex AnsiEscapeSequence();

    private sealed class EvidenceWorkspace : IDisposable
    {
        private const string QualifiedRcCommitDate =
            "2019-12-31T00:00:00Z";
        private const string QualifiedRcTagDate =
            "2020-01-01T00:00:00Z";
        private const string StableCommitDate =
            "2020-02-10T00:00:00Z";
        private const string EvidenceCommitDate =
            "2020-02-11T00:00:00Z";
        private const string FixtureAcceptance =
            """
            {
              "trustedComputingBase": {
                "components": [
                  {
                    "name": "fixture",
                    "paths": [
                      "SharpProof.Worker/ProductMarker.cs"
                    ]
                  }
                ]
              }
            }
            """;
        private readonly TemporaryWorkspace _temporary;

        private EvidenceWorkspace(
            TemporaryWorkspace temporary,
            string repository,
            string evidenceCommit,
            string productCommit,
            string qualifiedRcCommit)
        {
            _temporary = temporary;
            Repository = repository;
            EvidenceCommit = evidenceCommit;
            ProductCommit = productCommit;
            QualifiedRcCommit = qualifiedRcCommit;
        }

        internal string Root => _temporary.Root;

        internal string Repository
        {
            get;
        }

        internal string EvidenceCommit
        {
            get;
        }

        internal string ProductCommit
        {
            get;
        }

        internal string QualifiedRcCommit
        {
            get;
        }

        internal static async Task<EvidenceWorkspace> CreateAsync(
            string evidenceJson,
            bool annotatedTag)
        {
            var fixtureEvidence = JsonNode.Parse(
                evidenceJson)!.AsObject();
            var stableProductChange =
                fixtureEvidence["fixtureStableProductChange"]?
                    .GetValue<bool>() == true;
            var stableReleaseControlChange =
                fixtureEvidence["fixtureStableReleaseControlChange"]?
                    .GetValue<bool>() == true;
            var stableTestChange =
                fixtureEvidence["fixtureStableTestChange"]?
                    .GetValue<bool>() == true;
            var stableDocumentationChange =
                fixtureEvidence["fixtureStableDocumentationChange"]?
                    .GetValue<bool>() == true;
            var lightweightRcTag =
                fixtureEvidence["fixtureLightweightRcTag"]?
                    .GetValue<bool>() == true;
            var missingPilotArtifact =
                fixtureEvidence["fixtureMissingPilotArtifact"]?
                    .GetValue<bool>() == true;
            var pilotArtifactMismatch =
                fixtureEvidence["fixturePilotArtifactMismatch"]?
                    .GetValue<bool>() == true;
            var failedQualificationArtifact =
                fixtureEvidence["fixtureFailedQualificationArtifact"]?
                    .GetValue<bool>() == true;
            var corruptQualificationArchive =
                fixtureEvidence["fixtureCorruptQualificationArchive"]?
                    .GetValue<bool>() == true;
            var packageManifestMismatch =
                fixtureEvidence["fixturePackageManifestMismatch"]?
                    .GetValue<bool>() == true;
            var missingPackageEntry =
                fixtureEvidence["fixtureMissingPackageEntry"]?
                    .GetValue<bool>() == true;
            var missingQualificationReceipt =
                fixtureEvidence["fixtureMissingQualificationReceipt"]?
                    .GetValue<bool>() == true;
            var malformedQualificationReceipt =
                fixtureEvidence["fixtureMalformedQualificationReceipt"]?
                    .GetValue<bool>() == true;
            var missingQualificationGateEvidence =
                fixtureEvidence["fixtureMissingQualificationGateEvidence"]?
                    .GetValue<bool>() == true;
            var unsafePilotArchive =
                fixtureEvidence["fixtureUnsafePilotArchive"]?
                    .GetValue<bool>() == true;
            fixtureEvidence.Remove("fixtureStableProductChange");
            fixtureEvidence.Remove("fixtureStableReleaseControlChange");
            fixtureEvidence.Remove("fixtureStableTestChange");
            fixtureEvidence.Remove("fixtureStableDocumentationChange");
            fixtureEvidence.Remove("fixtureLightweightRcTag");
            fixtureEvidence.Remove("fixtureMissingPilotArtifact");
            fixtureEvidence.Remove("fixturePilotArtifactMismatch");
            fixtureEvidence.Remove("fixtureFailedQualificationArtifact");
            fixtureEvidence.Remove("fixtureCorruptQualificationArchive");
            fixtureEvidence.Remove("fixturePackageManifestMismatch");
            fixtureEvidence.Remove("fixtureMissingPackageEntry");
            fixtureEvidence.Remove("fixtureMissingQualificationReceipt");
            fixtureEvidence.Remove("fixtureMalformedQualificationReceipt");
            fixtureEvidence.Remove("fixtureMissingQualificationGateEvidence");
            fixtureEvidence.Remove("fixtureUnsafePilotArchive");
            evidenceJson = fixtureEvidence.ToJsonString(
                s_indentedJsonOptions);
            var temporary = TemporaryWorkspace.Create(
                "SharpProof.HumanEvidenceTests");
            try
            {
                var repository = Path.Combine(
                    temporary.Root,
                    "evidence-repository");
                Directory.CreateDirectory(repository);
                await AssertGitAsync(
                    repository,
                    "init",
                    "--quiet",
                    "--initial-branch=master");
                await AssertGitAsync(
                    repository,
                    "config",
                    "user.name",
                    "SharpProof Tests");
                await AssertGitAsync(
                    repository,
                    "config",
                    "user.email",
                    "sharpproof-tests@example.com");
                var acceptanceTarget = Path.Combine(
                    repository,
                    "eng",
                    "acceptance",
                    "contract.json");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(acceptanceTarget)!);
                await File.WriteAllTextAsync(
                    acceptanceTarget,
                    FixtureAcceptance);
                var productDirectory = Path.Combine(
                    repository,
                    "SharpProof.Worker");
                Directory.CreateDirectory(productDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(productDirectory, "ProductMarker.cs"),
                    "namespace SharpProof.Worker;" +
                    Environment.NewLine);
                var releaseControlPath = Path.Combine(
                    repository,
                    "scripts",
                    "ReleaseControl.ps1");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(releaseControlPath)!);
                await File.WriteAllTextAsync(
                    releaseControlPath,
                    "Write-Host 'release control'\n");
                var releaseTestPath = Path.Combine(
                    repository,
                    "SharpProof.Package.Test",
                    "ReleaseControlTests.cs");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(releaseTestPath)!);
                await File.WriteAllTextAsync(
                    releaseTestPath,
                    "namespace SharpProof.Package.Test;\n");
                var unicodeInputDirectory = Path.Combine(
                    repository,
                    "inputs");
                Directory.CreateDirectory(unicodeInputDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(unicodeInputDirectory, "Alpha.txt"),
                    "alpha\n");
                await File.WriteAllTextAsync(
                    Path.Combine(unicodeInputDirectory, "input-Z.txt"),
                    "zeta\n");
                await File.WriteAllTextAsync(
                    Path.Combine(unicodeInputDirectory, "\u0130nput.txt"),
                    "unicode\n");
                await File.WriteAllTextAsync(
                    Path.Combine(repository, "README.md"),
                    "# Fixture documentation\n");
                await File.WriteAllTextAsync(
                    Path.Combine(
                        repository,
                        "SharpProof.Release.props"),
                    "<SharpProofPackageVersion>" +
                    QualifiedRcVersion +
                    "</SharpProofPackageVersion>\n");
                await AssertGitAsync(repository, "add", ".");
                await AssertGitAtAsync(
                    repository,
                    QualifiedRcCommitDate,
                    "commit",
                    "--quiet",
                    "-m",
                    "Create qualified RC product");
                var qualifiedRcCommit = await AssertGitAsync(
                    repository,
                    "rev-parse",
                    "HEAD");
                if (lightweightRcTag)
                {
                    await AssertGitAsync(
                        repository,
                        "tag",
                        $"v{QualifiedRcVersion}");
                }
                else
                {
                    await AssertGitAtAsync(
                        repository,
                        QualifiedRcTagDate,
                        "tag",
                        "-a",
                        $"v{QualifiedRcVersion}",
                        "-m",
                        "Qualified SharpProof RC");
                }
                if (stableProductChange)
                {
                    await File.AppendAllTextAsync(
                        Path.Combine(
                            productDirectory,
                            "ProductMarker.cs"),
                        "// semantic change\n");
                }
                if (stableReleaseControlChange)
                {
                    await File.AppendAllTextAsync(
                        releaseControlPath,
                        "# release-control change\n");
                }
                if (stableTestChange)
                {
                    await File.AppendAllTextAsync(
                        releaseTestPath,
                        "// test change\n");
                }
                if (stableDocumentationChange)
                {
                    await File.AppendAllTextAsync(
                        Path.Combine(repository, "README.md"),
                        "Stable documentation correction.\n");
                }
                await File.WriteAllTextAsync(
                    Path.Combine(
                        repository,
                        "SharpProof.Release.props"),
                    "<SharpProofPackageVersion>" +
                    "1.0.0" +
                    "</SharpProofPackageVersion>\n");
                await File.WriteAllTextAsync(
                    Path.Combine(repository, "CHANGELOG.md"),
                    "# Stable release metadata" +
                    Environment.NewLine);
                await AssertGitAsync(repository, "add", ".");
                await AssertGitAtAsync(
                    repository,
                    StableCommitDate,
                    "commit",
                    "--quiet",
                    "-m",
                    "Prepare stable release metadata");
                var productCommit = await AssertGitAsync(
                    repository,
                    "rev-parse",
                    "HEAD");
                var qualifiedRcDigests = await ReadDigestsAsync(
                    repository,
                    qualifiedRcCommit.Output.Trim());
                var stableDigests = await ReadDigestsAsync(
                    repository,
                    productCommit.Output.Trim());
                await AssertGitAsync(
                    repository,
                    "switch",
                    "--orphan",
                    "release-evidence");
                await AssertGitAsync(
                    repository,
                    "rm",
                    "-rf",
                    "--ignore-unmatch",
                    ".");
                var evidenceDirectory = Path.Combine(
                    repository,
                    "releases");
                Directory.CreateDirectory(evidenceDirectory);
                var boundEvidenceJson = evidenceJson
                    .Replace(
                        HumanReleaseGateScriptTests.QualifiedRcCommit,
                        qualifiedRcCommit.Output.Trim(),
                        StringComparison.Ordinal)
                    .Replace(
                        HumanReleaseGateScriptTests.ProductCommit,
                        productCommit.Output.Trim(),
                        StringComparison.Ordinal);
                var boundEvidence = JsonNode.Parse(
                    boundEvidenceJson)!.AsObject();
                BindQualificationDigests(
                    boundEvidence,
                    qualifiedRcDigests,
                    stableDigests);
                BindGitHubArtifactEvidence(
                    temporary.Root,
                    boundEvidence,
                    qualifiedRcCommit.Output.Trim(),
                    missingPilotArtifact,
                    pilotArtifactMismatch,
                    failedQualificationArtifact,
                    corruptQualificationArchive,
                    packageManifestMismatch,
                    missingPackageEntry,
                    missingQualificationReceipt,
                    malformedQualificationReceipt,
                    missingQualificationGateEvidence,
                    unsafePilotArchive);
                await File.WriteAllTextAsync(
                    Path.Combine(
                        evidenceDirectory,
                        "v1.0.0.json"),
                    boundEvidence.ToJsonString(s_indentedJsonOptions));
                await AssertGitAsync(repository, "add", ".");
                await AssertGitAtAsync(
                    repository,
                    EvidenceCommitDate,
                    "commit",
                    "--quiet",
                    "-m",
                    "Record external release evidence");
                var commit = await AssertGitAsync(
                    repository,
                    "rev-parse",
                    "HEAD");
                var tagArguments = annotatedTag
                    ? new[]
                    {
                        "tag",
                        "-a",
                        "evidence/v1.0.0",
                        "-m",
                        "SharpProof 1.0 human evidence"
                    }
                    : new[]
                    {
                        "tag",
                        "evidence/v1.0.0"
                    };
                await AssertGitAsync(repository, tagArguments);
                return new EvidenceWorkspace(
                    temporary,
                    repository,
                    commit.Output.Trim(),
                    productCommit.Output.Trim(),
                    qualifiedRcCommit.Output.Trim());
            }
            catch
            {
                temporary.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _temporary.Dispose();
        }

        private static async Task<ProcessResult> AssertGitAsync(
            string repository,
            params string[] arguments)
        {
            var result = await RunProcessAsync(
                repository,
                "git",
                arguments);
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            return result;
        }

        private static async Task<ProcessResult> AssertGitAtAsync(
            string repository,
            string timestamp,
            params string[] arguments)
        {
            var result = await RunProcessWithEnvironmentAsync(
                repository,
                "git",
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_DATE"] = timestamp,
                    ["GIT_COMMITTER_DATE"] = timestamp
                },
                arguments);
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            return result;
        }

        private static async Task<ReleaseDigests> ReadDigestsAsync(
            string repository,
            string commit)
        {
            var result = await RunProcessAsync(
                FindRepositoryRoot(),
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(
                    FindRepositoryRoot(),
                    "scripts",
                    "Get-SharpProofReleaseDigests.ps1"),
                "-RepositoryPath",
                repository,
                "-Commit",
                commit);
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            using var document = JsonDocument.Parse(result.Output);
            return new ReleaseDigests(
                document.RootElement.GetProperty(
                    "productionDigestSha256").GetString()!,
                document.RootElement.GetProperty(
                    "trustedComputingBaseDigestSha256").GetString()!);
        }

        private static void BindQualificationDigests(
            JsonObject evidence,
            ReleaseDigests qualifiedRc,
            ReleaseDigests stable)
        {
            var qualification = evidence["qualification"]!;
            BindDigest(
                qualification["qualifiedRc"]!,
                "productionDigestSha256",
                qualifiedRc.Production);
            BindDigest(
                qualification["qualifiedRc"]!,
                "trustedComputingBaseDigestSha256",
                qualifiedRc.TrustedComputingBase);
            BindDigest(
                qualification["stableCandidate"]!,
                "productionDigestSha256",
                stable.Production);
            BindDigest(
                qualification["stableCandidate"]!,
                "trustedComputingBaseDigestSha256",
                stable.TrustedComputingBase);
        }

        private static void BindGitHubArtifactEvidence(
            string workspaceRoot,
            JsonObject evidence,
            string qualifiedRcCommit,
            bool missingPilotArtifact,
            bool pilotArtifactMismatch,
            bool failedQualificationArtifact,
            bool corruptQualificationArchive,
            bool packageManifestMismatch,
            bool missingPackageEntry,
            bool missingQualificationReceipt,
            bool malformedQualificationReceipt,
            bool missingQualificationGateEvidence,
            bool unsafePilotArchive)
        {
            var artifactDirectory = Path.Combine(
                workspaceRoot,
                "github-artifacts");
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllText(
                Path.Combine(artifactDirectory, "qualified-rc.txt"),
                qualifiedRcCommit + "\n",
                new UTF8Encoding(false));

            var qualifiedRc = evidence["qualification"]!["qualifiedRc"]!
                .AsObject();
            var workflow = qualifiedRc["workflow"]!.AsObject();
            var package = qualifiedRc["package"]!.AsObject();
            var runId = workflow["runId"]!.GetValue<long>();
            var runAttempt = workflow["runAttempt"]!.GetValue<int>();
            var packageVersion = qualifiedRc["packageVersion"]!
                .GetValue<string>();
            var packageArtifacts = package["artifacts"]!.AsArray();
            var packageEntries = new Dictionary<string, byte[]>(
                StringComparer.Ordinal);
            foreach (var artifact in packageArtifacts)
            {
                var packageId = artifact!["id"]!.GetValue<string>();
                var fileName = $"{packageId}.{packageVersion}.nupkg";
                var bytes = new UTF8Encoding(false).GetBytes(
                    $"fixture package {packageId} {packageVersion}\n");
                var sha256 = Convert.ToHexStringLower(
                    SHA256.HashData(bytes));
                artifact["sha256"] = sha256;
                packageEntries.Add(fileName, bytes);
                foreach (var pilot in evidence["pilots"]!.AsArray())
                {
                    foreach (var pilotArtifact in
                             pilot!["package"]!["artifacts"]!.AsArray())
                    {
                        if (pilotArtifact!["id"]!.GetValue<string>() ==
                            packageId &&
                            pilotArtifact["sha256"]!.GetValue<string>() ==
                            Digest)
                        {
                            pilotArtifact["sha256"] = sha256;
                        }
                    }
                }
            }
            var releaseManifest = new JsonObject
            {
                ["schemaVersion"] = 2,
                ["packageVersion"] = packageVersion,
                ["repository"] = new JsonObject
                {
                    ["type"] = "git",
                    ["url"] =
                        "https://github.com/alexyorke/SharpProof",
                    ["commit"] = qualifiedRcCommit
                },
                ["hashAlgorithm"] = "SHA256",
                ["artifacts"] = new JsonArray(
                    packageArtifacts.Select(
                        artifact =>
                        {
                            var packageId = artifact!["id"]!
                                .GetValue<string>();
                            return new JsonObject
                            {
                                ["fileName"] =
                                    $"{packageId}.{packageVersion}.nupkg",
                                ["kind"] = "package",
                                ["packageId"] = packageId,
                                ["bytes"] = packageEntries[
                                    $"{packageId}.{packageVersion}.nupkg"].
                                    LongLength,
                                ["sha256"] = artifact["sha256"]!
                                    .GetValue<string>()
                            };
                        }).ToArray()),
                ["thirdPartyComponents"] = new JsonArray()
            };
            var releaseArtifacts = releaseManifest["artifacts"]!.AsArray();
            foreach (var artifact in packageArtifacts)
            {
                var packageId = artifact!["id"]!.GetValue<string>();
                var fileName = $"{packageId}.{packageVersion}.snupkg";
                var bytes = new UTF8Encoding(false).GetBytes(
                    $"fixture symbols {packageId} {packageVersion}\n");
                var sha256 = Convert.ToHexStringLower(
                    SHA256.HashData(bytes));
                packageEntries.Add(fileName, bytes);
                releaseArtifacts.Add(new JsonObject
                {
                    ["fileName"] = fileName,
                    ["kind"] = "symbols",
                    ["packageId"] = packageId,
                    ["bytes"] = bytes.LongLength,
                    ["sha256"] = sha256
                });
            }
            var sbomBytes = new UTF8Encoding(false).GetBytes(
                "{\"spdxVersion\":\"SPDX-2.3\"}\n");
            packageEntries.Add("SharpProof.spdx.json", sbomBytes);
            releaseArtifacts.Add(new JsonObject
            {
                ["fileName"] = "SharpProof.spdx.json",
                ["kind"] = "sbom",
                ["packageId"] = null,
                ["bytes"] = sbomBytes.LongLength,
                ["sha256"] = Convert.ToHexStringLower(
                    SHA256.HashData(sbomBytes))
            });
            if (packageManifestMismatch)
            {
                releaseManifest["artifacts"]![0]!["sha256"] =
                    new string('b', 64);
            }
            var sums = string.Join(
                "\n",
                releaseManifest["artifacts"]!.AsArray().Select(
                    artifact =>
                        $"{artifact!["sha256"]!.GetValue<string>()}  " +
                        artifact["fileName"]!.GetValue<string>())) +
                "\n";
            packageEntries.Add(
                "SHA256SUMS",
                new UTF8Encoding(false).GetBytes(sums));
            if (missingPackageEntry)
            {
                packageEntries.Remove(packageEntries.Keys.First(
                    name => name.EndsWith(
                        ".nupkg",
                        StringComparison.Ordinal)));
            }
            var packageEvidence = WriteArtifact(
                artifactDirectory,
                checked(2_000_000 + runId),
                "SharpProof.release.json",
                releaseManifest,
                packageEntries);
            package["releaseManifestSha256"] =
                packageEvidence.RecordSha256;
            workflow["packageArtifactSha256"] =
                packageEvidence.ArchiveSha256;
            foreach (var pilot in evidence["pilots"]!.AsArray())
            {
                var pilotManifest = pilot!["package"]![
                    "releaseManifestSha256"]!.GetValue<string>();
                if (pilotManifest == Digest)
                {
                    pilot["package"]!["releaseManifestSha256"] =
                        packageEvidence.RecordSha256;
                }
            }

            var gateNames = new[]
            {
                "package",
                "packageConsumers",
                "minimumSdkConsumer",
                "security",
                "attestation",
                "coverageBaseline",
                "lockedRestore",
                "acceptance",
                "fuzz",
                "mutations",
                "corpus",
                "performance",
                "coverage",
                "dependencyAudit",
                "humanEvidence"
            };
            var releaseTag = qualifiedRc["releaseTag"]!
                .GetValue<string>();
            var runRecord = new JsonObject
            {
                ["provider"] = "github-actions",
                ["repository"] = "alexyorke/SharpProof",
                ["runId"] = runId,
                ["runAttempt"] = runAttempt,
                ["workflowRef"] =
                    "alexyorke/SharpProof/.github/workflows/" +
                    $"package-consumers.yml@refs/tags/{releaseTag}",
                ["job"] = "release-qualification",
                ["ref"] = $"refs/tags/{releaseTag}",
                ["sha"] = qualifiedRcCommit
            };
            var gates = new JsonObject();
            var receipts = new JsonObject();
            var receiptEntries = new Dictionary<string, byte[]>(
                StringComparer.Ordinal);
            foreach (var gateName in gateNames)
            {
                var required = gateName != "humanEvidence";
                gates[gateName] = required ? "passed" : "not-required";
                if (required)
                {
                    var evidenceBytes = new UTF8Encoding(false).GetBytes(
                        $"{{\"gate\":\"{gateName}\",\"status\":\"passed\"}}\n");
                    var evidenceSha256 = Convert.ToHexStringLower(
                        SHA256.HashData(evidenceBytes));
                    var evidencePath =
                        $"gate-evidence/{gateName}.json";
                    receiptEntries.Add(evidencePath, evidenceBytes);
                    var receipt = new JsonObject
                    {
                        ["schemaVersion"] = 2,
                        ["tag"] = releaseTag,
                        ["releaseCommit"] = qualifiedRcCommit,
                        ["gate"] = gateName,
                        ["status"] = "passed",
                        ["run"] = runRecord.DeepClone(),
                        ["evidence"] = new JsonObject
                        {
                            ["path"] =
                                "artifacts/release-qualification/" +
                                evidencePath,
                            ["sha256"] = evidenceSha256
                        },
                        ["humanEvidenceDocumentSha256"] = null
                    };
                    if (malformedQualificationReceipt &&
                        gateName == "package")
                    {
                        receipt = new JsonObject
                        {
                            ["gate"] = gateName
                        };
                    }
                    var receiptBytes = new UTF8Encoding(false).GetBytes(
                        receipt.ToJsonString(s_indentedJsonOptions) + "\n");
                    receipts[gateName] = Convert.ToHexStringLower(
                        SHA256.HashData(receiptBytes));
                    receiptEntries.Add(
                        $"qualification-receipts/{gateName}.json",
                        receiptBytes);
                }
                else
                {
                    receipts[gateName] = null;
                }
            }
            if (missingQualificationReceipt)
            {
                receiptEntries.Remove(receiptEntries.Keys.First(
                    path => path.StartsWith(
                        "qualification-receipts/",
                        StringComparison.Ordinal)));
            }
            if (missingQualificationGateEvidence)
            {
                receiptEntries.Remove("gate-evidence/package.json");
            }
            var qualificationRecord = new JsonObject
            {
                ["schemaVersion"] = 5,
                ["status"] = failedQualificationArtifact
                    ? "failed"
                    : "passed",
                ["failureKind"] = null,
                ["tag"] = releaseTag,
                ["releaseCommit"] = qualifiedRcCommit,
                ["run"] = runRecord,
                ["coverageBaselineCommit"] = BaselineCommit,
                ["humanEvidence"] = new JsonObject
                {
                    ["status"] = "not-required"
                },
                ["gates"] = gates,
                ["gateReceipts"] = receipts
            };
            var qualificationEvidence = WriteArtifact(
                artifactDirectory,
                checked(1_000_000 + runId),
                "qualification.json",
                qualificationRecord,
                receiptEntries);
            workflow["qualificationArtifactSha256"] =
                qualificationEvidence.ArchiveSha256;
            workflow["qualificationRecordSha256"] =
                qualificationEvidence.RecordSha256;
            if (corruptQualificationArchive)
            {
                File.AppendAllText(
                    Path.Combine(
                        artifactDirectory,
                        $"{1_000_000 + runId}.zip"),
                    "tampered");
            }

            var isFirstPilotCycle = true;
            foreach (var pilotNode in evidence["pilots"]!.AsArray())
            {
                var pilot = pilotNode!.AsObject();
                var pilotId = pilot["id"]!.GetValue<string>();
                var selectedClaims = pilot["selectedClaims"]!
                    .GetValue<int>();
                foreach (var cycleNode in pilot["weeklyCycles"]!.AsArray())
                {
                    var cycle = cycleNode!.AsObject();
                    var pilotWorkflow = cycle["workflow"]!.AsObject();
                    var pilotRunId = pilotWorkflow["runId"]!
                        .GetValue<long>();
                    var pilotRunAttempt = pilotWorkflow["runAttempt"]!
                        .GetValue<int>();
                    var pilotRecord = new JsonObject
                    {
                        ["schemaVersion"] = 1,
                        ["pilotId"] = pilotId,
                        ["selectedClaims"] = selectedClaims,
                        ["package"] = pilot["package"]!.DeepClone(),
                        ["runtime"] = pilot["runtime"]!.DeepClone(),
                        ["tool"] = pilot["tool"]!.DeepClone(),
                        ["policy"] = pilot["policy"]!.DeepClone(),
                        ["cycle"] = new JsonObject
                        {
                            ["weekEnding"] =
                                cycle["weekEnding"]!.DeepClone(),
                            ["outcomes"] =
                                cycle["outcomes"]!.DeepClone(),
                            ["evidenceUse"] =
                                cycle["evidenceUse"]!.DeepClone(),
                            ["result"] = cycle["result"]!.DeepClone()
                        },
                        ["workflow"] = new JsonObject
                        {
                            ["provider"] =
                                pilotWorkflow["provider"]!.DeepClone(),
                            ["repository"] =
                                pilotWorkflow["repository"]!.DeepClone(),
                            ["name"] =
                                pilotWorkflow["name"]!.DeepClone(),
                            ["path"] =
                                pilotWorkflow["path"]!.DeepClone(),
                            ["event"] =
                                pilotWorkflow["event"]!.DeepClone(),
                            ["runId"] =
                                pilotWorkflow["runId"]!.DeepClone(),
                            ["runAttempt"] =
                                pilotWorkflow["runAttempt"]!.DeepClone(),
                            ["sourceCommit"] =
                                pilotWorkflow["sourceCommit"]!.DeepClone(),
                            ["evidenceUrl"] =
                                pilotWorkflow["evidenceUrl"]!.DeepClone()
                        }
                    };
                    if (pilotArtifactMismatch && isFirstPilotCycle)
                    {
                        pilotRecord["cycle"]!["outcomes"]!["proven"] = 49;
                        pilotRecord["cycle"]!["outcomes"]!["unknown"] = 1;
                    }
                    if (missingPilotArtifact && isFirstPilotCycle)
                    {
                        isFirstPilotCycle = false;
                        continue;
                    }
                    var pilotEvidence = WriteArtifact(
                        artifactDirectory,
                        checked(3_000_000 + pilotRunId),
                        "sharpproof-pilot-evidence.json",
                        pilotRecord);
                    var pilotArchiveSha256 =
                        pilotEvidence.ArchiveSha256;
                    if (unsafePilotArchive && isFirstPilotCycle)
                    {
                        var pilotArchivePath = Path.Combine(
                            artifactDirectory,
                            $"{3_000_000 + pilotRunId}.zip");
                        using (var stream = File.Open(
                                   pilotArchivePath,
                                   FileMode.Open,
                                   FileAccess.ReadWrite,
                                   FileShare.None))
                        using (var archive = new ZipArchive(
                                   stream,
                                   ZipArchiveMode.Update,
                                   leaveOpen: false))
                        {
                            var unsafeEntry = archive.CreateEntry(
                                "../escape.json");
                            using var writer = new StreamWriter(
                                unsafeEntry.Open(),
                                new UTF8Encoding(false));
                            writer.Write("{}");
                        }
                        pilotArchiveSha256 = Convert.ToHexStringLower(
                            SHA256.HashData(
                                File.ReadAllBytes(pilotArchivePath)));
                    }
                    pilotWorkflow["artifactSha256"] = pilotArchiveSha256;
                    pilotWorkflow["recordSha256"] =
                        pilotEvidence.RecordSha256;
                    isFirstPilotCycle = false;
                }
            }
        }

        private static ArtifactDigests WriteArtifact(
            string artifactDirectory,
            long artifactId,
            string entryName,
            JsonObject record,
            IReadOnlyDictionary<string, byte[]>? additionalEntries = null)
        {
            var recordBytes = new UTF8Encoding(false).GetBytes(
                record.ToJsonString(s_indentedJsonOptions) + "\n");
            var archivePath = Path.Combine(
                artifactDirectory,
                $"{artifactId}.zip");
            using (var stream = File.Create(archivePath))
            using (var archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Create,
                       leaveOpen: false))
            {
                var entry = archive.CreateEntry(
                    entryName,
                    CompressionLevel.Optimal);
                entry.ExternalAttributes = UnixRegularFileAttributes;
                entry.LastWriteTime = new DateTimeOffset(
                    2000,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero);
                using (var entryStream = entry.Open())
                {
                    entryStream.Write(recordBytes);
                }
                if (additionalEntries != null)
                {
                    foreach (var additionalEntry in additionalEntries)
                    {
                        var extra = archive.CreateEntry(
                            additionalEntry.Key,
                            CompressionLevel.Optimal);
                        extra.ExternalAttributes = UnixRegularFileAttributes;
                        extra.LastWriteTime = new DateTimeOffset(
                            2000,
                            1,
                            1,
                            0,
                            0,
                            0,
                            TimeSpan.Zero);
                        using var extraStream = extra.Open();
                        extraStream.Write(additionalEntry.Value);
                    }
                }
            }
            return new ArtifactDigests(
                Convert.ToHexStringLower(SHA256.HashData(
                    File.ReadAllBytes(archivePath))),
                Convert.ToHexStringLower(SHA256.HashData(recordBytes)));
        }

        private static void BindDigest(
            JsonNode owner,
            string property,
            string value)
        {
            if (owner[property]!.GetValue<string>() == Digest)
            {
                owner[property] = value;
            }
        }

        private sealed record ReleaseDigests(
            string Production,
            string TrustedComputingBase);

        private sealed record ArtifactDigests(
            string ArchiveSha256,
            string RecordSha256);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _expectedParent;

        private TemporaryWorkspace(string root, string expectedParent)
        {
            Root = root;
            _expectedParent = expectedParent;
        }

        internal string Root
        {
            get;
        }

        internal static TemporaryWorkspace Create(string name)
        {
            var parent = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), name));
            Directory.CreateDirectory(parent);
            var root = Path.Combine(
                parent,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryWorkspace(root, parent);
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var relative = Path.GetRelativePath(_expectedParent, resolved);
            if (Path.IsPathRooted(relative) ||
                relative == "." ||
                relative == ".." ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                foreach (var file in Directory.EnumerateFiles(
                             resolved,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(resolved, recursive: true);
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
