[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedProductCommit,

    [Parameter()]
    [ValidatePattern('^refs/tags/evidence/v1\.0\.0$')]
    [string]$EvidenceRef = 'refs/tags/evidence/v1.0.0',

    [Parameter()]
    [ValidatePattern('^[^\r\n]+$')]
    [string]$EvidenceRepository =
        'https://github.com/alexyorke/SharpProof.git',

    [Parameter()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$evidencePath = 'releases/v1.0.0.json'
$evidenceBranchRef = 'refs/heads/release-evidence'
$productBranchRef = 'refs/heads/master'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'GitHubEvidenceArtifact.ps1')
$acceptance = Get-Content `
    -LiteralPath (Join-Path $repositoryRoot 'eng/acceptance/contract.json') `
    -Raw |
    ConvertFrom-Json
$expectedPackages = @(
    'SharpProof.Attributes',
    'SharpProof',
    'SharpProof.Verifier.Win-x64'
)

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Owner is missing required property '$Name'."
    }
    return $property.Value
}

function Test-NonPublicIpAddress {
    param(
        [Parameter(Mandatory = $true)]
        [Net.IPAddress]$Address
    )

    if ($Address.IsIPv4MappedToIPv6) {
        $Address = $Address.MapToIPv4()
    }
    if ([Net.IPAddress]::IsLoopback($Address) -or
        $Address.Equals([Net.IPAddress]::Any) -or
        $Address.Equals([Net.IPAddress]::IPv6Any)) {
        return $true
    }

    $bytes = $Address.GetAddressBytes()
    if ($Address.AddressFamily -eq
        [Net.Sockets.AddressFamily]::InterNetwork) {
        return $bytes[0] -eq 0 -or
            $bytes[0] -eq 10 -or
            ($bytes[0] -eq 100 -and
                ($bytes[1] -band 0xc0) -eq 0x40) -or
            ($bytes[0] -eq 169 -and $bytes[1] -eq 254) -or
            ($bytes[0] -eq 172 -and
                ($bytes[1] -band 0xf0) -eq 16) -or
            ($bytes[0] -eq 192 -and $bytes[1] -eq 168) -or
            $bytes[0] -ge 224
    }

    return $Address.IsIPv6LinkLocal -or
        $Address.IsIPv6SiteLocal -or
        $Address.IsIPv6Multicast -or
        ($bytes[0] -band 0xfe) -eq 0xfc
}

function ConvertTo-CanonicalEvidenceUri {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $uri = $null
    if (-not [Uri]::TryCreate(
            $Value,
            [UriKind]::Absolute,
            [ref]$uri) -or
        $uri.Scheme -ne 'https' -or
        -not $uri.IsDefaultPort -or
        -not [string]::IsNullOrEmpty($uri.UserInfo)) {
        throw (
            "$Owner must contain a canonical absolute HTTPS evidence URL " +
            'without credentials or a nondefault port.')
    }
    $canonicalHost = $uri.IdnHost.ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($canonicalHost) -or
        $canonicalHost.EndsWith(
            '.',
            [StringComparison]::Ordinal)) {
        throw "$Owner evidence URL host is not canonical."
    }

    $reservedDomains = @(
        'example',
        'example.com',
        'example.net',
        'example.org',
        'invalid',
        'local',
        'localhost',
        'localhost.localdomain',
        'test'
    )
    foreach ($domain in $reservedDomains) {
        if ($canonicalHost.Equals(
                $domain,
                [StringComparison]::OrdinalIgnoreCase) -or
            $canonicalHost.EndsWith(
                ".$domain",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw (
                "$Owner must contain an absolute HTTPS evidence URL and " +
                'cannot use a reserved placeholder domain.')
        }
    }
    $address = $null
    if ([Net.IPAddress]::TryParse($canonicalHost, [ref]$address) -and
        (Test-NonPublicIpAddress -Address $address)) {
        throw "$Owner evidence URL cannot use a nonpublic IP address."
    }
    if ($null -eq $address -and
        $canonicalHost.IndexOf('.') -lt 0) {
        throw "$Owner evidence URL cannot use a single-label host."
    }
    if ($Value -cne $uri.AbsoluteUri) {
        throw "$Owner evidence URL must use its canonical absolute form."
    }

    return $uri
}

function Assert-EvidenceUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $null = ConvertTo-CanonicalEvidenceUri `
        -Value $Value `
        -Owner $Owner
}

function Assert-Nonblank {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -match '[\r\n]') {
        throw "$Owner must be nonblank and single-line."
    }
}

function Assert-SemanticVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($Value -notmatch
        '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
        throw "$Owner must be a semantic version."
    }
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($Value -notmatch '^[0-9a-f]{64}$' -or
        $Value -eq ('0' * 64)) {
        throw (
            "$Owner must be a nonzero lowercase SHA-256 digest.")
    }
}

function Assert-Commit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($Value -notmatch '^[0-9a-f]{40}$' -or
        $Value -eq ('0' * 40)) {
        throw "$Owner must be a nonzero lowercase Git commit SHA."
    }
}

function ConvertTo-UtcTimestamp {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset]$Value).ToUniversalTime()
    }
    if ($Value -is [DateTime]) {
        return [DateTimeOffset]::new(
            ([DateTime]$Value).ToUniversalTime())
    }

    $text = [string]$Value
    $parsed = [DateTimeOffset]::MinValue
    if ($text -match
            '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' -and
        [DateTimeOffset]::TryParse(
            $text,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor
                [Globalization.DateTimeStyles]::AdjustToUniversal,
            [ref]$parsed)) {
        return $parsed
    }
    return $null
}

function Assert-GitHubRepository {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($Value -notmatch
            '^(?<account>[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?)/(?<repository>[A-Za-z0-9_.-]{1,100})$' -or
        $Matches.repository -in @('.', '..')) {
        throw "$Owner must identify one canonical GitHub owner/repository."
    }
}

function Get-AuthenticatedGitHubWorkflowRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string]$WorkflowName,

        [Parameter(Mandatory = $true)]
        [string]$WorkflowPath,

        [Parameter(Mandatory = $true)]
        [string]$Event,

        [Parameter(Mandatory = $true)]
        [int64]$RunId,

        [Parameter(Mandatory = $true)]
        [int]$RunAttempt,

        [Parameter(Mandatory = $true)]
        [string]$SourceCommit,

        [Parameter(Mandatory = $true)]
        [string]$EvidenceUrl,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    Assert-GitHubRepository `
        -Value $Repository `
        -Owner "$Owner repository"
    $evidenceUri = ConvertTo-CanonicalEvidenceUri `
        -Value $EvidenceUrl `
        -Owner $Owner
    $expectedEvidenceUrl = (
        "https://github.com/$Repository/actions/runs/$RunId/attempts/" +
        "$RunAttempt")
    if ($EvidenceUrl -cne $expectedEvidenceUrl -or
        $evidenceUri.IdnHost -cne 'github.com' -or
        -not [string]::IsNullOrEmpty($evidenceUri.Query) -or
        -not [string]::IsNullOrEmpty($evidenceUri.Fragment)) {
        throw (
            "$Owner evidence URL must identify the declared GitHub " +
            'repository, run ID, and run attempt exactly.')
    }

    $token = if (-not [string]::IsNullOrWhiteSpace(
            $env:SHARPPROOF_GITHUB_TOKEN)) {
        $env:SHARPPROOF_GITHUB_TOKEN
    }
    else {
        $env:GITHUB_TOKEN
    }
    if ([string]::IsNullOrWhiteSpace($token) -or
        $token -match '[\r\n]') {
        throw (
            "$Owner requires SHARPPROOF_GITHUB_TOKEN or GITHUB_TOKEN for " +
            'authenticated GitHub workflow evidence.')
    }

    $apiUri = (
        "https://api.github.com/repos/$Repository/actions/runs/$RunId/" +
        "attempts/$RunAttempt")
    $headers = @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $token"
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'SharpProof-release-evidence-validator'
    }
    try {
        $run = Invoke-RestMethod `
            -Uri $apiUri `
            -Method Get `
            -Headers $headers `
            -MaximumRedirection 0 `
            -TimeoutSec 30 `
            -ErrorAction Stop
    }
    catch {
        throw (
            "$Owner GitHub workflow evidence could not be authenticated: " +
            $_.Exception.Message)
    }

    $apiRunId = [int64](Get-RequiredProperty `
        $run `
        'id' `
        "$Owner GitHub API response")
    $apiRunAttempt = [int](Get-RequiredProperty `
        $run `
        'run_attempt' `
        "$Owner GitHub API response")
    $apiRepository = Get-RequiredProperty `
        $run `
        'repository' `
        "$Owner GitHub API response"
    $apiRepositoryName = [string](Get-RequiredProperty `
        $apiRepository `
        'full_name' `
        "$Owner GitHub API repository")
    $apiWorkflowName = [string](Get-RequiredProperty `
        $run `
        'name' `
        "$Owner GitHub API response")
    $apiWorkflowPath = [string](Get-RequiredProperty `
        $run `
        'path' `
        "$Owner GitHub API response")
    $apiEvent = [string](Get-RequiredProperty `
        $run `
        'event' `
        "$Owner GitHub API response")
    $apiHeadRepository = Get-RequiredProperty `
        $run `
        'head_repository' `
        "$Owner GitHub API response"
    $apiHeadRepositoryName = [string](Get-RequiredProperty `
        $apiHeadRepository `
        'full_name' `
        "$Owner GitHub API head repository")
    $apiSourceCommit = [string](Get-RequiredProperty `
        $run `
        'head_sha' `
        "$Owner GitHub API response")
    $apiStatus = [string](Get-RequiredProperty `
        $run `
        'status' `
        "$Owner GitHub API response")
    $apiConclusion = [string](Get-RequiredProperty `
        $run `
        'conclusion' `
        "$Owner GitHub API response")
    $apiEvidenceUrl = [string](Get-RequiredProperty `
        $run `
        'html_url' `
        "$Owner GitHub API response")
    $expectedApiEvidenceUrl = (
        "https://github.com/$Repository/actions/runs/$RunId")
    if ($apiRunId -ne $RunId -or
        $apiRunAttempt -ne $RunAttempt -or
        $apiRepositoryName -cne $Repository -or
        $apiHeadRepositoryName -cne $Repository -or
        $apiWorkflowName -cne $WorkflowName -or
        $apiWorkflowPath -cne $WorkflowPath -or
        $apiEvent -cne $Event -or
        $apiSourceCommit -cne $SourceCommit -or
        $apiStatus -cne 'completed' -or
        $apiConclusion -cne 'success' -or
        $apiEvidenceUrl -cne $expectedApiEvidenceUrl) {
        throw (
            "$Owner authenticated GitHub response must match the declared " +
            'repository, workflow path/event, commit, successful run, and ' +
            'attempt.')
    }

    $createdAtValue = Get-RequiredProperty `
        $run `
        'created_at' `
        "$Owner GitHub API response"
    $startedAtValue = Get-RequiredProperty `
        $run `
        'run_started_at' `
        "$Owner GitHub API response"
    $updatedAtValue = Get-RequiredProperty `
        $run `
        'updated_at' `
        "$Owner GitHub API response"
    $createdAt = ConvertTo-UtcTimestamp -Value $createdAtValue
    $startedAt = ConvertTo-UtcTimestamp -Value $startedAtValue
    $updatedAt = ConvertTo-UtcTimestamp -Value $updatedAtValue
    if ($null -eq $createdAt -or
        $null -eq $startedAt -or
        $null -eq $updatedAt -or
        $createdAt -gt $updatedAt -or
        $startedAt -gt $updatedAt) {
        throw (
            "$Owner authenticated GitHub timestamps must be exact UTC " +
            'timestamps in chronological order.')
    }

    return [pscustomobject][ordered]@{
        createdAt = $createdAt
        startedAt = $startedAt
        updatedAt = $updatedAt
    }
}

function Assert-QualificationArtifactRecord {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Record,

        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [long]$RunId,

        [Parameter(Mandatory = $true)]
        [int]$RunAttempt,

        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag,

        [Parameter(Mandatory = $true)]
        [string]$ProductCommit,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ([int](Get-RequiredProperty `
            $Record `
            'schemaVersion' `
            "$Owner qualification record") -ne 5 -or
        [string](Get-RequiredProperty `
            $Record `
            'status' `
            "$Owner qualification record") -cne 'passed' -or
        [string](Get-RequiredProperty `
            $Record `
            'tag' `
            "$Owner qualification record") -cne $ReleaseTag -or
        [string](Get-RequiredProperty `
            $Record `
            'releaseCommit' `
            "$Owner qualification record") -cne $ProductCommit) {
        throw (
            "$Owner qualification artifact must be a passed schema-5 " +
            'record for the exact tag and product commit.')
    }
    $failureKind = Get-RequiredProperty `
        $Record `
        'failureKind' `
        "$Owner qualification record"
    if ($null -ne $failureKind) {
        throw "$Owner passed qualification record contains a failure kind."
    }

    $run = Get-RequiredProperty `
        $Record `
        'run' `
        "$Owner qualification record"
    $expectedWorkflowRef = (
        "$Repository/.github/workflows/package-consumers.yml@" +
        "refs/tags/$ReleaseTag")
    if ([string](Get-RequiredProperty `
            $run `
            'provider' `
            "$Owner qualification run") -cne 'github-actions' -or
        [string](Get-RequiredProperty `
            $run `
            'repository' `
            "$Owner qualification run") -cne $Repository -or
        [long](Get-RequiredProperty `
            $run `
            'runId' `
            "$Owner qualification run") -ne $RunId -or
        [int](Get-RequiredProperty `
            $run `
            'runAttempt' `
            "$Owner qualification run") -ne $RunAttempt -or
        [string](Get-RequiredProperty `
            $run `
            'workflowRef' `
            "$Owner qualification run") -cne $expectedWorkflowRef -or
        [string](Get-RequiredProperty `
            $run `
            'job' `
            "$Owner qualification run") -cne 'release-qualification' -or
        [string](Get-RequiredProperty `
            $run `
            'ref' `
            "$Owner qualification run") -cne
                "refs/tags/$ReleaseTag" -or
        [string](Get-RequiredProperty `
            $run `
            'sha' `
            "$Owner qualification run") -cne $ProductCommit) {
        throw (
            "$Owner qualification artifact run identity does not match " +
            'the authenticated release workflow.')
    }

    $coverageBaseline = [string](Get-RequiredProperty `
            $Record `
            'coverageBaselineCommit' `
            "$Owner qualification record")
    Assert-Commit `
        -Value $coverageBaseline `
        -Owner "$Owner coverage baseline"
    if ($coverageBaseline -ceq $ProductCommit) {
        throw "$Owner coverage baseline cannot equal the product commit."
    }

    $gates = Get-RequiredProperty `
        $Record `
        'gates' `
        "$Owner qualification record"
    $gateReceipts = Get-RequiredProperty `
        $Record `
        'gateReceipts' `
        "$Owner qualification record"
    $gateNames = @(
        'package',
        'packageConsumers',
        'minimumSdkConsumer',
        'security',
        'attestation',
        'coverageBaseline',
        'lockedRestore',
        'acceptance',
        'fuzz',
        'mutations',
        'corpus',
        'performance',
        'coverage',
        'dependencyAudit',
        'humanEvidence'
    )
    foreach ($gateName in $gateNames) {
        $expectedStatus = if ($gateName -ceq 'humanEvidence') {
            'not-required'
        }
        else {
            'passed'
        }
        if ([string](Get-RequiredProperty `
                $gates `
                $gateName `
                "$Owner qualification gates") -cne $expectedStatus) {
            throw (
                "$Owner qualification artifact gate '$gateName' must be " +
                "'$expectedStatus'.")
        }
        $receipt = Get-RequiredProperty `
            $gateReceipts `
            $gateName `
            "$Owner qualification receipts"
        if ($expectedStatus -ceq 'passed') {
            Assert-Sha256 `
                -Value ([string]$receipt) `
                -Owner "$Owner qualification gate '$gateName' receipt"
        }
        elseif ($null -ne $receipt) {
            throw (
                "$Owner non-required qualification gate '$gateName' " +
                'cannot contain a receipt.')
        }
    }
    $humanEvidence = Get-RequiredProperty `
        $Record `
        'humanEvidence' `
        "$Owner qualification record"
    if ([string](Get-RequiredProperty `
            $humanEvidence `
            'status' `
            "$Owner qualification human evidence") -cne 'not-required') {
        throw (
            "$Owner pre-stable qualification artifact cannot claim final " +
            'human evidence.')
    }
}

function Get-AuthenticatedQualificationEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Qualification,

        [Parameter(Mandatory = $true)]
        [string]$ReleaseTag,

        [Parameter(Mandatory = $true)]
        [string]$ProductCommit,

        [Parameter(Mandatory = $true)]
        [string]$PackageVersion,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $workflow = Get-RequiredProperty `
        $Qualification `
        'workflow' `
        $Owner
    $provider = [string](Get-RequiredProperty `
        $workflow `
        'provider' `
        "$Owner workflow")
    $repository = [string](Get-RequiredProperty `
        $workflow `
        'repository' `
        "$Owner workflow")
    $workflowName = [string](Get-RequiredProperty `
        $workflow `
        'name' `
        "$Owner workflow")
    $workflowPath = [string](Get-RequiredProperty `
        $workflow `
        'path' `
        "$Owner workflow")
    $workflowEvent = [string](Get-RequiredProperty `
        $workflow `
        'event' `
        "$Owner workflow")
    $runId = [long](Get-RequiredProperty `
        $workflow `
        'runId' `
        "$Owner workflow")
    $runAttempt = [int](Get-RequiredProperty `
        $workflow `
        'runAttempt' `
        "$Owner workflow")
    $evidenceUrl = [string](Get-RequiredProperty `
        $workflow `
        'evidenceUrl' `
        "$Owner workflow")
    if ($provider -cne 'github-actions' -or
        $repository -cne 'alexyorke/SharpProof' -or
        $workflowName -cne 'Cross-platform package consumers' -or
        $workflowPath -cne
            '.github/workflows/package-consumers.yml' -or
        $workflowEvent -cne 'push' -or
        $runId -le 0 -or
        $runAttempt -le 0) {
        throw (
            "$Owner workflow must identify the canonical SharpProof " +
            'release workflow and a positive run attempt.')
    }
    $authenticatedRun = Get-AuthenticatedGitHubWorkflowRun `
        -Repository $repository `
        -WorkflowName $workflowName `
        -WorkflowPath $workflowPath `
        -Event $workflowEvent `
        -RunId $runId `
        -RunAttempt $runAttempt `
        -SourceCommit $ProductCommit `
        -EvidenceUrl $evidenceUrl `
        -Owner "$Owner workflow"

    $qualificationArchiveSha256 = [string](Get-RequiredProperty `
        $workflow `
        'qualificationArtifactSha256' `
        "$Owner workflow")
    $qualificationRecordSha256 = [string](Get-RequiredProperty `
        $workflow `
        'qualificationRecordSha256' `
        "$Owner workflow")
    $qualificationArtifactName = (
        "release-qualification-$ProductCommit-$runAttempt")
    $qualificationRecord = Get-SharpProofGitHubArtifactRecord `
        -Repository $repository `
        -RunId $runId `
        -RunAttempt $runAttempt `
        -SourceCommit $ProductCommit `
        -ArtifactName $qualificationArtifactName `
        -ArchiveSha256 $qualificationArchiveSha256 `
        -RecordPath 'qualification.json' `
        -RecordSha256 $qualificationRecordSha256 `
        -AttemptStartedAt $authenticatedRun.startedAt `
        -AttemptCompletedAt $authenticatedRun.updatedAt `
        -VerifyQualificationReceipts `
        -Owner "$Owner qualification"
    Assert-QualificationArtifactRecord `
        -Record $qualificationRecord `
        -Repository $repository `
        -RunId $runId `
        -RunAttempt $runAttempt `
        -ReleaseTag $ReleaseTag `
        -ProductCommit $ProductCommit `
        -Owner $Owner

    $package = Get-RequiredProperty `
        $Qualification `
        'package' `
        $Owner
    $releaseManifestSha256 = [string](Get-RequiredProperty `
        $package `
        'releaseManifestSha256' `
        "$Owner package")
    $packageArchiveSha256 = [string](Get-RequiredProperty `
        $workflow `
        'packageArtifactSha256' `
        "$Owner workflow")
    $packageArtifactName = (
        "nuget-packages-$ProductCommit-$runAttempt")
    $releaseManifest = Get-SharpProofGitHubArtifactRecord `
        -Repository $repository `
        -RunId $runId `
        -RunAttempt $runAttempt `
        -SourceCommit $ProductCommit `
        -ArtifactName $packageArtifactName `
        -ArchiveSha256 $packageArchiveSha256 `
        -RecordPath 'SharpProof.release.json' `
        -RecordSha256 $releaseManifestSha256 `
        -AttemptStartedAt $authenticatedRun.startedAt `
        -AttemptCompletedAt $authenticatedRun.updatedAt `
        -VerifyReleaseManifestArtifacts `
        -Owner "$Owner packages"
    if ([int](Get-RequiredProperty `
            $releaseManifest `
            'schemaVersion' `
            "$Owner release manifest") -ne 2 -or
        [string](Get-RequiredProperty `
            $releaseManifest `
            'packageVersion' `
            "$Owner release manifest") -cne $PackageVersion -or
        [string](Get-RequiredProperty `
            $releaseManifest `
            'hashAlgorithm' `
            "$Owner release manifest") -cne 'SHA256') {
        throw (
            "$Owner release manifest must use schema 2, SHA256, and the " +
            'exact package version.')
    }
    $manifestRepository = Get-RequiredProperty `
        $releaseManifest `
        'repository' `
        "$Owner release manifest"
    if ([string](Get-RequiredProperty `
            $manifestRepository `
            'type' `
            "$Owner release manifest repository") -cne 'git' -or
        [string](Get-RequiredProperty `
            $manifestRepository `
            'url' `
            "$Owner release manifest repository") -cne
                'https://github.com/alexyorke/SharpProof' -or
        [string](Get-RequiredProperty `
            $manifestRepository `
            'commit' `
            "$Owner release manifest repository") -cne $ProductCommit) {
        throw (
            "$Owner release manifest does not identify the exact " +
            'SharpProof product commit.')
    }

    $declaredArtifacts = @(
        Get-RequiredProperty `
            $package `
            'artifacts' `
            "$Owner package"
    )
    if ($declaredArtifacts.Count -ne $expectedPackages.Count) {
        throw "$Owner package must identify the exact three-package graph."
    }
    $manifestArtifacts = @(
        Get-RequiredProperty `
            $releaseManifest `
            'artifacts' `
            "$Owner release manifest"
    )
    $manifestPackages = @(
        $manifestArtifacts |
            Where-Object {
                [string](Get-RequiredProperty `
                    $_ `
                    'kind' `
                    "$Owner release artifact") -ceq 'package'
            }
    )
    $manifestSymbols = @(
        $manifestArtifacts |
            Where-Object {
                [string](Get-RequiredProperty `
                    $_ `
                    'kind' `
                    "$Owner release artifact") -ceq 'symbols'
            }
    )
    $manifestSboms = @(
        $manifestArtifacts |
            Where-Object {
                [string](Get-RequiredProperty `
                    $_ `
                    'kind' `
                    "$Owner release artifact") -ceq 'sbom'
            }
    )
    $manifestSymbolIds = @(
        $manifestSymbols |
            ForEach-Object {
                [string](Get-RequiredProperty `
                    $_ `
                    'packageId' `
                    "$Owner release symbol package")
            } |
            Sort-Object
    )
    if ($manifestArtifacts.Count -ne 7 -or
        $manifestPackages.Count -ne $expectedPackages.Count -or
        $manifestSymbols.Count -ne $expectedPackages.Count -or
        $manifestSboms.Count -ne 1 -or
        ($manifestSymbolIds -join '|') -cne
            (($expectedPackages | Sort-Object) -join '|')) {
        throw (
            "$Owner release manifest must contain exactly three main " +
            'packages, three matching symbol packages, and one SBOM.')
    }
    $bindings = [Collections.Generic.List[string]]::new()
    foreach ($index in 0..($expectedPackages.Count - 1)) {
        $declared = $declaredArtifacts[$index]
        $packageId = [string](Get-RequiredProperty `
            $declared `
            'id' `
            "$Owner package artifact")
        $packageSha256 = [string](Get-RequiredProperty `
            $declared `
            'sha256' `
            "$Owner package '$packageId'")
        if ($packageId -cne $expectedPackages[$index]) {
            throw (
                "$Owner package IDs must preserve dependency order.")
        }
        Assert-Sha256 `
            -Value $packageSha256 `
            -Owner "$Owner package '$packageId'"
        $matchingManifestPackages = @(
            $manifestPackages |
                Where-Object {
                    [string](Get-RequiredProperty `
                        $_ `
                        'packageId' `
                        "$Owner release package") -ceq $packageId
                }
        )
        if ($matchingManifestPackages.Count -ne 1 -or
            [string](Get-RequiredProperty `
                $matchingManifestPackages[0] `
                'sha256' `
                "$Owner release package '$packageId'") -cne
                    $packageSha256) {
            throw (
                "$Owner package '$packageId' hash does not match the " +
                'authenticated release manifest.')
        }
        $bindings.Add("$packageId=$packageSha256")
    }

    return [pscustomobject][ordered]@{
        createdAt = $authenticatedRun.createdAt
        updatedAt = $authenticatedRun.updatedAt
        packageFingerprint = (
            "$PackageVersion|$releaseManifestSha256|" +
            ($bindings -join '|'))
    }
}

function Assert-PilotArtifactRecord {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Record,

        [Parameter(Mandatory = $true)]
        [string]$PilotId,

        [Parameter(Mandatory = $true)]
        [int]$SelectedClaims,

        [Parameter(Mandatory = $true)]
        [object]$Package,

        [Parameter(Mandatory = $true)]
        [object]$Runtime,

        [Parameter(Mandatory = $true)]
        [object]$Tool,

        [Parameter(Mandatory = $true)]
        [object]$Policy,

        [Parameter(Mandatory = $true)]
        [object]$Cycle,

        [Parameter(Mandatory = $true)]
        [object]$Workflow,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $expectedCycle = [pscustomobject][ordered]@{
        weekEnding = Get-RequiredProperty `
            $Cycle `
            'weekEnding' `
            $Owner
        outcomes = Get-RequiredProperty `
            $Cycle `
            'outcomes' `
            $Owner
        evidenceUse = Get-RequiredProperty `
            $Cycle `
            'evidenceUse' `
            $Owner
        result = Get-RequiredProperty `
            $Cycle `
            'result' `
            $Owner
    }
    $expectedWorkflow = [pscustomobject][ordered]@{
        provider = Get-RequiredProperty `
            $Workflow `
            'provider' `
            "$Owner workflow"
        repository = Get-RequiredProperty `
            $Workflow `
            'repository' `
            "$Owner workflow"
        name = Get-RequiredProperty `
            $Workflow `
            'name' `
            "$Owner workflow"
        path = Get-RequiredProperty `
            $Workflow `
            'path' `
            "$Owner workflow"
        event = Get-RequiredProperty `
            $Workflow `
            'event' `
            "$Owner workflow"
        runId = Get-RequiredProperty `
            $Workflow `
            'runId' `
            "$Owner workflow"
        runAttempt = Get-RequiredProperty `
            $Workflow `
            'runAttempt' `
            "$Owner workflow"
        sourceCommit = Get-RequiredProperty `
            $Workflow `
            'sourceCommit' `
            "$Owner workflow"
        evidenceUrl = Get-RequiredProperty `
            $Workflow `
            'evidenceUrl' `
            "$Owner workflow"
    }
    $expected = [pscustomobject][ordered]@{
        schemaVersion = 1
        pilotId = $PilotId
        selectedClaims = $SelectedClaims
        package = $Package
        runtime = $Runtime
        tool = $Tool
        policy = $Policy
        cycle = $expectedCycle
        workflow = $expectedWorkflow
    }
    $actualJson = $Record |
        ConvertTo-Json -Depth 100 -Compress
    $expectedJson = $expected |
        ConvertTo-Json -Depth 100 -Compress
    if ($actualJson -cne $expectedJson) {
        throw (
            "$Owner authenticated pilot artifact does not exactly match " +
            'the declared package, tool, policy, result, outcomes, and run.')
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $output = @(& git -C $Repository @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw (
            "$Operation failed with exit code $LASTEXITCODE. " +
            ($output -join "`n"))
    }
    return $output
}

function Invoke-GitToFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('-C')
    $startInfo.ArgumentList.Add($Repository)
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw "$Operation did not start Git."
    }
    try {
        $errorTask = $process.StandardError.ReadToEndAsync()
        $stream = [IO.File]::Create($OutputPath)
        try {
            $copyTask = $process.StandardOutput.BaseStream.CopyToAsync($stream)
            $process.WaitForExit()
            $null = $copyTask.GetAwaiter().GetResult()
        }
        finally {
            $stream.Dispose()
        }
        $errorText = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw (
                "$Operation failed with exit code $($process.ExitCode). " +
                $errorText)
        }
    }
    finally {
        $process.Dispose()
    }
}

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to fetch immutable human release evidence.'
}
if ($EvidenceRepository.StartsWith(
        '-',
        [StringComparison]::Ordinal)) {
    throw 'EvidenceRepository cannot begin with a command-line option prefix.'
}

$temporaryParent = Join-Path `
    ([IO.Path]::GetTempPath()) `
    'SharpProof.HumanReleaseEvidence'
[IO.Directory]::CreateDirectory($temporaryParent) |
    Out-Null
$temporaryRepository = Join-Path `
    $temporaryParent `
    ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryRepository) |
    Out-Null

$evidenceCommit = $null
$evidenceTagObject = $null
$evidenceDocumentSha256 = $null
$evidence = $null
$resolvedQualifiedRcCommit = $null
$qualifiedRcCommitTime = [DateTimeOffset]::MinValue
$qualifiedRcTagTime = [DateTimeOffset]::MinValue
$evidenceCommitTime = [DateTimeOffset]::MinValue
$computedQualifiedRcDigests = $null
$computedStableDigests = $null
try {
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @('init', '--bare', '--quiet') `
        -Operation 'Evidence repository initialization'
    $localRef = 'refs/tags/sharpproof-human-release-evidence'
    $localBranch = 'refs/heads/sharpproof-human-release-evidence'
    $localProductBranch = 'refs/heads/sharpproof-product-master'
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @(
            'fetch',
            '--quiet',
            '--no-tags',
            $EvidenceRepository,
            "+${EvidenceRef}:${localRef}",
            "+${evidenceBranchRef}:${localBranch}",
            "+${productBranchRef}:${localProductBranch}") `
        -Operation (
            "Fetching immutable evidence ref '$EvidenceRef' and evidence " +
            "branch '$evidenceBranchRef'")
    $objectType = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @('cat-file', '-t', $localRef) `
            -Operation 'Reading evidence tag type' |
            Select-Object -First 1).Trim()
    if ($objectType -ne 'tag') {
        throw (
            "Evidence ref '$EvidenceRef' must be an annotated tag, not " +
            "'$objectType'.")
    }
    $tagName = $EvidenceRef.Substring('refs/tags/'.Length)
    $tagContents = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @('cat-file', 'tag', $localRef) `
        -Operation 'Reading evidence tag object'
    if (-not ($tagContents -contains "tag $tagName")) {
        throw "Evidence tag object does not identify '$tagName'."
    }
    $evidenceTagObject = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @('rev-parse', $localRef) `
            -Operation 'Resolving evidence tag object' |
            Select-Object -First 1).Trim()
    $evidenceCommit = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @('rev-parse', "${localRef}^{commit}") `
            -Operation 'Resolving evidence commit' |
            Select-Object -First 1).Trim()
    Assert-Commit `
        -Value $evidenceTagObject `
        -Owner 'Evidence tag object'
    Assert-Commit `
        -Value $evidenceCommit `
        -Owner 'Evidence commit'
    if ($evidenceCommit -eq $ExpectedProductCommit) {
        throw 'Human evidence must be external to the product commit.'
    }
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @(
            'merge-base',
            '--is-ancestor',
            $evidenceCommit,
            $localBranch) `
        -Operation (
            "Confirming evidence tag membership in '$evidenceBranchRef'")
    $evidenceFile = Join-Path $temporaryRepository 'human-release-evidence.json'
    Invoke-GitToFile `
        -Repository $temporaryRepository `
        -Arguments @('show', "${evidenceCommit}:${evidencePath}") `
        -OutputPath $evidenceFile `
        -Operation "Reading '$evidencePath' from evidence commit"
    $evidenceDocumentSha256 = (Get-FileHash `
        -LiteralPath $evidenceFile `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $evidenceJson = [IO.File]::ReadAllText(
        $evidenceFile,
        [Text.UTF8Encoding]::new(
            $false,
            $true))
    $evidence = $evidenceJson | ConvertFrom-Json

    $preQualification = Get-RequiredProperty `
        $evidence `
        'qualification' `
        'Human release evidence'
    $preQualifiedRc = Get-RequiredProperty `
        $preQualification `
        'qualifiedRc' `
        'Release qualification'
    $preQualifiedRcTag = [string](Get-RequiredProperty `
        $preQualifiedRc `
        'releaseTag' `
        'Qualified RC')
    if ($preQualifiedRcTag -notmatch '^v1\.0\.0-rc\.[0-9]+$') {
        throw 'Qualified RC evidence must identify a v1.0.0-rc.N tag.'
    }
    $localQualifiedRcRef = 'refs/tags/sharpproof-qualified-rc'
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @(
            'fetch',
            '--quiet',
            '--no-tags',
            $EvidenceRepository,
            "+refs/tags/${preQualifiedRcTag}:${localQualifiedRcRef}") `
        -Operation "Fetching qualified RC tag '$preQualifiedRcTag'"
    $qualifiedRcObjectType = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @('cat-file', '-t', $localQualifiedRcRef) `
            -Operation 'Reading qualified RC tag type' |
            Select-Object -First 1).Trim()
    if ($qualifiedRcObjectType -ne 'tag') {
        throw (
            "Qualified RC tag '$preQualifiedRcTag' must be an annotated " +
            "tag, not '$qualifiedRcObjectType'.")
    }
    $qualifiedRcTagTimestamp = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @(
                'for-each-ref',
                '--format=%(taggerdate:iso-strict)',
                $localQualifiedRcRef) `
            -Operation 'Reading qualified RC tag time' |
            Select-Object -First 1).Trim()
    if (-not [DateTimeOffset]::TryParse(
            $qualifiedRcTagTimestamp,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$qualifiedRcTagTime)) {
        throw 'Qualified RC annotated-tag time is invalid.'
    }
    $resolvedQualifiedRcCommit = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @(
                'rev-parse',
                "${localQualifiedRcRef}^{commit}") `
            -Operation 'Resolving qualified RC commit' |
            Select-Object -First 1).Trim()
    Assert-Commit `
        -Value $resolvedQualifiedRcCommit `
        -Owner 'Resolved qualified RC commit'
    $qualifiedRcCommitTimestamp = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @(
                'show',
                '-s',
                '--format=%cI',
                $resolvedQualifiedRcCommit) `
            -Operation 'Reading qualified RC commit time' |
            Select-Object -First 1).Trim()
    if (-not [DateTimeOffset]::TryParse(
            $qualifiedRcCommitTimestamp,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$qualifiedRcCommitTime)) {
        throw 'Qualified RC commit time is invalid.'
    }
    $resolvedProductCommit = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @(
                'rev-parse',
                "${ExpectedProductCommit}^{commit}") `
            -Operation 'Resolving stable product commit' |
            Select-Object -First 1).Trim()
    if ($resolvedProductCommit -ne $ExpectedProductCommit) {
        throw 'The stable product commit does not resolve exactly.'
    }
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @(
            'merge-base',
            '--is-ancestor',
            $ExpectedProductCommit,
            $localProductBranch) `
        -Operation (
            "Confirming stable product membership in '$productBranchRef'")
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @(
            'merge-base',
            '--is-ancestor',
            $resolvedQualifiedRcCommit,
            $ExpectedProductCommit) `
        -Operation (
            'Confirming qualified RC ancestry of the stable product commit')
    $evidenceCommitTimestamp = (
        Invoke-Git `
            -Repository $temporaryRepository `
            -Arguments @(
                'show',
                '-s',
                '--format=%cI',
                $evidenceCommit) `
            -Operation 'Reading evidence commit time' |
            Select-Object -First 1).Trim()
    if (-not [DateTimeOffset]::TryParse(
            $evidenceCommitTimestamp,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$evidenceCommitTime)) {
        throw 'Evidence commit time is invalid.'
    }
    $digestScript = Join-Path `
        $repositoryRoot `
        'scripts/Get-SharpProofReleaseDigests.ps1'
    $computedQualifiedRcDigests = (
        & $digestScript `
            -RepositoryPath $temporaryRepository `
            -Commit $resolvedQualifiedRcCommit) |
        ConvertFrom-Json
    $computedStableDigests = (
        & $digestScript `
            -RepositoryPath $temporaryRepository `
            -Commit $ExpectedProductCommit) |
        ConvertFrom-Json
    if ([int]$computedQualifiedRcDigests.schemaVersion -ne 1 -or
        [string]$computedQualifiedRcDigests.commit -ne
            $resolvedQualifiedRcCommit -or
        [int]$computedStableDigests.schemaVersion -ne 1 -or
        [string]$computedStableDigests.commit -ne $ExpectedProductCommit) {
        throw 'Canonical release digest output is malformed.'
    }
}
finally {
    $resolvedParent = [IO.Path]::GetFullPath($temporaryParent)
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRepository)
    $relative = [IO.Path]::GetRelativePath(
        $resolvedParent,
        $resolvedTemporary)
    if ([IO.Path]::IsPathRooted($relative) -or
        $relative -eq '.' -or
        $relative -eq '..' -or
        $relative.StartsWith(
            '..' + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal)) {
        throw 'Refusing to remove an unexpected evidence-fetch directory.'
    }
    if ([IO.Directory]::Exists($resolvedTemporary)) {
        foreach ($file in [IO.Directory]::EnumerateFiles(
                $resolvedTemporary,
                '*',
                [IO.SearchOption]::AllDirectories)) {
            [IO.File]::SetAttributes(
                $file,
                [IO.FileAttributes]::Normal)
        }
        [IO.Directory]::Delete($resolvedTemporary, $true)
    }
}

if ([int](Get-RequiredProperty `
        $evidence `
        'schemaVersion' `
        'Human release evidence') -ne 4 -or
    [string](Get-RequiredProperty `
        $evidence `
        'releaseTag' `
        'Human release evidence') -ne 'v1.0.0' -or
    [string](Get-RequiredProperty `
        $evidence `
        'productCommit' `
        'Human release evidence') -ne $ExpectedProductCommit -or
    [string](Get-RequiredProperty `
        $evidence `
        'evidenceRef' `
        'Human release evidence') -ne $EvidenceRef) {
    throw (
        'Human release evidence must use schema 4 and identify the exact ' +
        'v1.0.0 product commit and immutable evidence ref.')
}

$releaseQualification = Get-RequiredProperty `
    $evidence `
    'qualification' `
    'Human release evidence'
$qualifiedRc = Get-RequiredProperty `
    $releaseQualification `
    'qualifiedRc' `
    'Release qualification'
$stableCandidate = Get-RequiredProperty `
    $releaseQualification `
    'stableCandidate' `
    'Release qualification'
$qualifiedRcTag = [string](Get-RequiredProperty `
    $qualifiedRc `
    'releaseTag' `
    'Qualified RC')
$qualifiedRcCommit = [string](Get-RequiredProperty `
    $qualifiedRc `
    'productCommit' `
    'Qualified RC')
$qualifiedRcPackageVersion = [string](Get-RequiredProperty `
    $qualifiedRc `
    'packageVersion' `
    'Qualified RC')
Assert-Commit `
    -Value $qualifiedRcCommit `
    -Owner 'Qualified RC product commit'
Assert-SemanticVersion `
    -Value $qualifiedRcPackageVersion `
    -Owner 'Qualified RC package version'
if ($qualifiedRcCommit -ne $resolvedQualifiedRcCommit -or
    $qualifiedRcCommit -eq $ExpectedProductCommit -or
    $qualifiedRcPackageVersion -notmatch '^1\.0\.0-rc\.[0-9]+$' -or
    $qualifiedRcTag -ne "v$qualifiedRcPackageVersion") {
    throw (
        'Qualified RC identity must name a distinct 1.0.0-rc.N product ' +
        'commit, package version, and matching release tag.')
}
$qualifiedRcProductionDigest = [string](Get-RequiredProperty `
    $qualifiedRc `
    'productionDigestSha256' `
    'Qualified RC')
$qualifiedRcTcbDigest = [string](Get-RequiredProperty `
    $qualifiedRc `
    'trustedComputingBaseDigestSha256' `
    'Qualified RC')
Assert-Sha256 `
    -Value $qualifiedRcProductionDigest `
    -Owner 'Qualified RC production digest'
Assert-Sha256 `
    -Value $qualifiedRcTcbDigest `
    -Owner 'Qualified RC trusted-computing-base digest'
$qualifiedAtValue = Get-RequiredProperty `
    $qualifiedRc `
    'qualifiedAtUtc' `
    'Qualified RC'
$qualifiedAt = ConvertTo-UtcTimestamp -Value $qualifiedAtValue
if ($null -eq $qualifiedAt) {
    throw (
        'Qualified RC qualifiedAtUtc must be an exact UTC timestamp; got ' +
        "'$qualifiedAtValue'.")
}
$earliestQualifiedAt = if (
    $qualifiedRcTagTime -gt $qualifiedRcCommitTime) {
    $qualifiedRcTagTime
}
else {
    $qualifiedRcCommitTime
}
if ($qualifiedAt -lt $earliestQualifiedAt -or
    $qualifiedAt -gt $evidenceCommitTime) {
    throw (
        'Qualified RC qualifiedAtUtc cannot predate the immutable RC commit ' +
        'and annotated tag or postdate the evidence commit.')
}
$authenticatedQualifiedRc =
    Get-AuthenticatedQualificationEvidence `
        -Qualification $qualifiedRc `
        -ReleaseTag $qualifiedRcTag `
        -ProductCommit $qualifiedRcCommit `
        -PackageVersion $qualifiedRcPackageVersion `
        -Owner 'Qualified RC'
if ($qualifiedAt -ne $authenticatedQualifiedRc.updatedAt) {
    throw (
        'Qualified RC qualifiedAtUtc must equal the authenticated ' +
        'successful qualification workflow completion time.')
}

$stableCandidateCommit = [string](Get-RequiredProperty `
    $stableCandidate `
    'productCommit' `
    'Stable candidate')
$stableCandidatePackageVersion = [string](Get-RequiredProperty `
    $stableCandidate `
    'packageVersion' `
    'Stable candidate')
if ($stableCandidateCommit -ne $ExpectedProductCommit -or
    $stableCandidatePackageVersion -ne '1.0.0') {
    throw (
        'Stable-candidate identity must name the exact final product commit ' +
        'and 1.0.0 package version.')
}
$stableProductionDigest = [string](Get-RequiredProperty `
    $stableCandidate `
    'productionDigestSha256' `
    'Stable candidate')
$stableTcbDigest = [string](Get-RequiredProperty `
    $stableCandidate `
    'trustedComputingBaseDigestSha256' `
    'Stable candidate')
Assert-Sha256 `
    -Value $stableProductionDigest `
    -Owner 'Stable-candidate production digest'
Assert-Sha256 `
    -Value $stableTcbDigest `
    -Owner 'Stable-candidate trusted-computing-base digest'
$computedRcProductionDigest = [string](
    $computedQualifiedRcDigests.productionDigestSha256)
$computedRcTcbDigest = [string](
    $computedQualifiedRcDigests.trustedComputingBaseDigestSha256)
$computedStableProductionDigest = [string](
    $computedStableDigests.productionDigestSha256)
$computedStableTcbDigest = [string](
    $computedStableDigests.trustedComputingBaseDigestSha256)
if ($qualifiedRcProductionDigest -ne $computedRcProductionDigest -or
    $qualifiedRcTcbDigest -ne $computedRcTcbDigest -or
    $stableProductionDigest -ne $computedStableProductionDigest -or
    $stableTcbDigest -ne $computedStableTcbDigest -or
    $stableProductionDigest -ne $qualifiedRcProductionDigest -or
    $stableTcbDigest -ne $qualifiedRcTcbDigest) {
    throw (
        'Evidence digests must equal the independently computed RC and ' +
        'stable production/trusted-computing-base digests, and both ' +
        'candidates must match.')
}
$approvedDifferences = @(
    Get-RequiredProperty `
        $stableCandidate `
        'approvedMetadataDifferences' `
        'Stable candidate'
)
$allowedDifferences = @('version', 'changelog', 'release-metadata')
$differenceSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
if ($approvedDifferences.Count -eq 0) {
    throw (
        'Stable-candidate evidence must identify its approved metadata-only ' +
        'differences from the qualified RC.')
}
foreach ($difference in $approvedDifferences) {
    $differenceName = [string]$difference
    if ($differenceName -notin $allowedDifferences -or
        -not $differenceSet.Add($differenceName)) {
        throw (
            'Stable-candidate approved metadata differences must be unique ' +
            'and limited to version, changelog, and release-metadata.')
    }
}
if (-not $differenceSet.Contains('version')) {
    throw (
        'Stable-candidate evidence must record the RC-to-stable version ' +
        'metadata change.')
}

$pilots = @(
    Get-RequiredProperty $evidence 'pilots' 'Human release evidence'
)
if ($pilots.Count -lt 2) {
    throw 'Final 1.0 requires evidence from at least two pilot libraries.'
}
$totalClaims = 0
$pilotIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$workflowRuns = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$workflowEvidenceUrls = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$pilotRepositories = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$expectedPackageFingerprint =
    [string]$authenticatedQualifiedRc.packageFingerprint
$expectedToolFingerprint = $null
foreach ($pilot in $pilots) {
    $pilotId = [string](Get-RequiredProperty $pilot 'id' 'Pilot')
    if ([string]::IsNullOrWhiteSpace($pilotId) -or
        -not $pilotIds.Add($pilotId)) {
        throw 'Pilot IDs must be nonblank and unique.'
    }
    $selectedClaims = [int](Get-RequiredProperty `
        $pilot `
        'selectedClaims' `
        "Pilot '$pilotId'")
    if ($selectedClaims -le 0) {
        throw "Pilot '$pilotId' must report selected claims."
    }
    $totalClaims += $selectedClaims

    $pilotOwner = "Pilot '$pilotId'"
    $package = Get-RequiredProperty $pilot 'package' $pilotOwner
    $packageVersion = [string](Get-RequiredProperty `
        $package `
        'version' `
        "$pilotOwner package")
    Assert-SemanticVersion `
        -Value $packageVersion `
        -Owner "$pilotOwner package version"
    if ($packageVersion -ne $qualifiedRcPackageVersion) {
        throw (
            "$pilotOwner must use the exact qualified RC package version " +
            "'$qualifiedRcPackageVersion'.")
    }
    $releaseManifestSha256 = [string](Get-RequiredProperty `
        $package `
        'releaseManifestSha256' `
        "$pilotOwner package")
    Assert-Sha256 `
        -Value $releaseManifestSha256 `
        -Owner "$pilotOwner release manifest"
    $packageArtifacts = @(
        Get-RequiredProperty `
            $package `
            'artifacts' `
            "$pilotOwner package"
    )
    if ($packageArtifacts.Count -ne $expectedPackages.Count) {
        throw (
            "$pilotOwner package evidence must identify the exact " +
            'three-package graph.')
    }
    $actualPackageIds = [Collections.Generic.List[string]]::new()
    $packageArtifactBindings = [Collections.Generic.List[string]]::new()
    foreach ($artifact in $packageArtifacts) {
        $packageId = [string](Get-RequiredProperty `
            $artifact `
            'id' `
            "$pilotOwner package artifact")
        $actualPackageIds.Add($packageId)
        $packageSha256 = [string](Get-RequiredProperty `
            $artifact `
            'sha256' `
            "$pilotOwner package '$packageId'")
        Assert-Sha256 `
            -Value $packageSha256 `
            -Owner "$pilotOwner package '$packageId'"
        $packageArtifactBindings.Add("$packageId=$packageSha256")
    }
    if (($actualPackageIds -join '|') -ne
        ($expectedPackages -join '|')) {
        throw (
            "$pilotOwner package evidence must preserve the exact " +
            'dependency-ordered package IDs.')
    }
    $packageFingerprint = (
        "$packageVersion|$releaseManifestSha256|" +
        ($packageArtifactBindings -join '|'))
    if ($null -eq $expectedPackageFingerprint) {
        $expectedPackageFingerprint = $packageFingerprint
    }
    elseif ($packageFingerprint -ne $expectedPackageFingerprint) {
        throw (
            'Every pilot must use the same qualified RC package bytes and ' +
            'release manifest.')
    }

    $runtime = Get-RequiredProperty $pilot 'runtime' $pilotOwner
    if ([string](Get-RequiredProperty `
            $runtime `
            'operatingSystem' `
            "$pilotOwner runtime") -ne 'windows' -or
        [string](Get-RequiredProperty `
            $runtime `
            'architecture' `
            "$pilotOwner runtime") -ne 'x64') {
        throw "$pilotOwner must run the verifier on Windows x64."
    }
    $dotnetSdkVersion = [string](Get-RequiredProperty `
        $runtime `
        'dotnetSdkVersion' `
        "$pilotOwner runtime")
    $dotnetRuntimeVersion = [string](Get-RequiredProperty `
        $runtime `
        'dotnetRuntimeVersion' `
        "$pilotOwner runtime")
    $roslynVersion = [string](Get-RequiredProperty `
        $runtime `
        'roslynVersion' `
        "$pilotOwner runtime")
    if ($dotnetSdkVersion -notmatch '^9\.0\.[3-9][0-9]{2}$' -or
        $dotnetRuntimeVersion -notmatch '^9\.0\.[0-9]+$' -or
        $roslynVersion -notmatch '^4\.14\.[0-9]+$') {
        throw (
            "$pilotOwner runtime must use SDK 9.0.300 or later in the 9.0 " +
            'line, a .NET 9 runtime, and Roslyn 4.14.')
    }

    $tool = Get-RequiredProperty $pilot 'tool' $pilotOwner
    if ([string](Get-RequiredProperty `
            $tool `
            'productCommit' `
            "$pilotOwner tool") -ne $qualifiedRcCommit -or
        [string](Get-RequiredProperty `
            $tool `
            'workerVersion' `
            "$pilotOwner tool") -ne $packageVersion -or
        [string](Get-RequiredProperty `
            $tool `
            'protocolVersion' `
            "$pilotOwner tool") -ne
                [string]$acceptance.worker.protocolVersion -or
        [int](Get-RequiredProperty `
            $tool `
            'manifestSchemaVersion' `
            "$pilotOwner tool") -ne
                [int]$acceptance.worker.manifestSchemaVersion -or
        [int](Get-RequiredProperty `
            $tool `
            'compilerArtifactSchemaVersion' `
            "$pilotOwner tool") -ne
                [int]$acceptance.worker.compilerArtifactSchemaVersion) {
        throw (
            "$pilotOwner tool identity must match the exact qualified RC " +
            'commit, package version, and current worker protocol schemas.')
    }
    $workerAssemblySha256 = [string](Get-RequiredProperty `
        $tool `
        'workerAssemblySha256' `
        "$pilotOwner tool")
    $runtimeClosureSha256 = [string](Get-RequiredProperty `
        $tool `
        'runtimeClosureSha256' `
        "$pilotOwner tool")
    $specificationCatalogSha256 = [string](Get-RequiredProperty `
        $tool `
        'specificationCatalogSha256' `
        "$pilotOwner tool")
    Assert-Sha256 `
        -Value $workerAssemblySha256 `
        -Owner "$pilotOwner worker assembly"
    Assert-Sha256 `
        -Value $runtimeClosureSha256 `
        -Owner "$pilotOwner runtime closure"
    Assert-Sha256 `
        -Value $specificationCatalogSha256 `
        -Owner "$pilotOwner specification catalog"
    $toolFingerprint = (
        "$qualifiedRcCommit|$packageVersion|$workerAssemblySha256|" +
        "$runtimeClosureSha256|$specificationCatalogSha256")
    if ($null -eq $expectedToolFingerprint) {
        $expectedToolFingerprint = $toolFingerprint
    }
    elseif ($toolFingerprint -ne $expectedToolFingerprint) {
        throw (
            'Every pilot must use the same worker assembly, runtime closure, ' +
            'and specification catalog.')
    }

    $policy = Get-RequiredProperty $pilot 'policy' $pilotOwner
    if ([string](Get-RequiredProperty `
            $policy `
            'profile' `
            "$pilotOwner policy") -ne 'strict' -or
        [string](Get-RequiredProperty `
            $policy `
            'features' `
            "$pilotOwner policy") -ne 'all' -or
        [string](Get-RequiredProperty `
            $policy `
            'verifyPolicy' `
            "$pilotOwner policy") -ne 'require-proven' -or
        [string](Get-RequiredProperty `
            $policy `
            'assumptionPolicy' `
            "$pilotOwner policy") -ne 'error') {
        throw (
            "$pilotOwner must use strict/all/require-proven/error policy.")
    }

    $cycles = @(
        Get-RequiredProperty $pilot 'weeklyCycles' "Pilot '$pilotId'"
    )
    if ($cycles.Count -lt 4) {
        throw "Pilot '$pilotId' must have at least four weekly cycles."
    }
    $previousDate = $null
    $pilotRepository = $null
    $pilotSourceCommit = $null
    $pilotInputSha256 = $null
    $pilotClaimManifestSha256 = $null
    foreach ($cycle in $cycles) {
        $weekEnding = [string](Get-RequiredProperty `
            $cycle `
            'weekEnding' `
            "Pilot '$pilotId' cycle")
        $date = [DateTime]::MinValue
        if (-not [DateTime]::TryParseExact(
                $weekEnding,
                'yyyy-MM-dd',
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal,
                [ref]$date)) {
            throw "Pilot '$pilotId' contains an invalid weekEnding date."
        }
        if ($null -ne $previousDate -and
            ($date - $previousDate).TotalDays -ne 7) {
            throw "Pilot '$pilotId' weekly cycles must be consecutive."
        }
        if ($date.Date -lt $qualifiedAt.UtcDateTime.Date -or
            $date.Date -gt $evidenceCommitTime.UtcDateTime.Date) {
            throw (
                "Pilot '$pilotId' weekly cycles must occur after RC " +
                'qualification and no later than the evidence commit.')
        }
        $previousDate = $date
        $owner = "Pilot '$pilotId' cycle $weekEnding"

        $outcomes = Get-RequiredProperty $cycle 'outcomes' $owner
        $outcomeSelected = [int](Get-RequiredProperty `
            $outcomes `
            'selectedClaims' `
            "$owner outcomes")
        $outcomeProven = [int](Get-RequiredProperty `
            $outcomes `
            'proven' `
            "$owner outcomes")
        $outcomeRefuted = [int](Get-RequiredProperty `
            $outcomes `
            'refuted' `
            "$owner outcomes")
        $outcomeUnknown = [int](Get-RequiredProperty `
            $outcomes `
            'unknown' `
            "$owner outcomes")
        $outcomeAssumptions = [int](Get-RequiredProperty `
            $outcomes `
            'assumptions' `
            "$owner outcomes")
        $outcomeTrustedEvidence = [int](Get-RequiredProperty `
            $outcomes `
            'trustedEvidence' `
            "$owner outcomes")
        $outcomeInfrastructureFailures = [int](Get-RequiredProperty `
            $outcomes `
            'infrastructureFailures' `
            "$owner outcomes")
        if ($outcomeSelected -ne $selectedClaims -or
            $outcomeProven -ne $selectedClaims -or
            $outcomeRefuted -ne 0 -or
            $outcomeUnknown -ne 0 -or
            $outcomeAssumptions -ne 0 -or
            $outcomeTrustedEvidence -ne 0 -or
            $outcomeInfrastructureFailures -ne 0) {
            throw (
                "$owner must prove every selected claim with no refutation, " +
                'Unknown, assumption, trusted evidence, or infrastructure ' +
                'failure.')
        }

        $reasonCounts = @(
            Get-RequiredProperty `
                $outcomes `
                'reasonCounts' `
                "$owner outcomes"
        )
        if ($reasonCounts.Count -eq 0) {
            throw "$owner must record typed outcome reason counts."
        }
        $reasonNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $reasonTotal = 0
        foreach ($reasonCount in $reasonCounts) {
            $reason = [string](Get-RequiredProperty `
                $reasonCount `
                'reason' `
                "$owner outcome reason")
            $count = [int](Get-RequiredProperty `
                $reasonCount `
                'count' `
                "$owner outcome reason '$reason'")
            if ([string]::IsNullOrWhiteSpace($reason) -or
                $reason -match '[\r\n]' -or
                -not $reasonNames.Add($reason) -or
                $count -lt 0) {
                throw (
                    "$owner outcome reasons must be unique, nonblank, and " +
                    'have nonnegative counts.')
            }
            $reasonTotal += $count
        }
        if ($reasonTotal -ne $selectedClaims) {
            throw (
                "$owner outcome reason counts must account for every " +
                'selected claim exactly once.')
        }
        if ($reasonCounts.Count -ne 1 -or
            [string](Get-RequiredProperty `
                $reasonCounts[0] `
                'reason' `
                "$owner outcome reason") -ne 'None' -or
            [int](Get-RequiredProperty `
                $reasonCounts[0] `
                'count' `
                "$owner outcome reason 'None'") -ne $selectedClaims) {
            throw (
                "$owner must record the protocol reason 'None' for every " +
                'Proven claim.')
        }

        $evidenceUse = Get-RequiredProperty `
            $cycle `
            'evidenceUse' `
            $owner
        $assumptionRecords = @(
            Get-RequiredProperty `
                $evidenceUse `
                'assumptions' `
                "$owner evidence use"
        )
        $trustedEvidenceRecords = @(
            Get-RequiredProperty `
                $evidenceUse `
                'trustedEvidence' `
                "$owner evidence use"
        )
        if ($assumptionRecords.Count -ne $outcomeAssumptions -or
            $trustedEvidenceRecords.Count -ne $outcomeTrustedEvidence) {
            throw (
                "$owner assumption and trusted-evidence counts must match " +
                'their explicit evidence-use records.')
        }
        if ($assumptionRecords.Count -ne 0 -or
            $trustedEvidenceRecords.Count -ne 0) {
            throw (
                "$owner strict qualification cannot use assumptions or " +
                'trusted evidence.')
        }

        $result = Get-RequiredProperty $cycle 'result' $owner
        if ([string](Get-RequiredProperty `
                $result `
                'format' `
                "$owner result") -ne 'SharpProof.WorkerVerifyResponse' -or
            [string](Get-RequiredProperty `
                $result `
                'runStatus' `
                "$owner result") -ne 'Complete') {
            throw "$owner result must be a complete worker response."
        }
        Assert-Sha256 `
            -Value ([string](Get-RequiredProperty `
                $result `
                'sha256' `
                "$owner result")) `
            -Owner "$owner result"
        Assert-Sha256 `
            -Value ([string](Get-RequiredProperty `
                $result `
                'requestHash' `
                "$owner result")) `
            -Owner "$owner request"
        $inputSha256 = [string](Get-RequiredProperty `
            $result `
            'inputSha256' `
            "$owner result")
        $claimManifestSha256 = [string](Get-RequiredProperty `
            $result `
            'claimManifestSha256' `
            "$owner result")
        Assert-Sha256 `
            -Value $inputSha256 `
            -Owner "$owner compiler input"
        Assert-Sha256 `
            -Value $claimManifestSha256 `
            -Owner "$owner claim manifest"
        if ($null -eq $pilotInputSha256) {
            $pilotInputSha256 = $inputSha256
            $pilotClaimManifestSha256 = $claimManifestSha256
        }
        elseif ($inputSha256 -ne $pilotInputSha256 -or
            $claimManifestSha256 -ne $pilotClaimManifestSha256) {
            throw (
                "$owner changed the compiler input or selected claim " +
                'manifest; the four-week qualification cycle must restart.')
        }

        $workflow = Get-RequiredProperty $cycle 'workflow' $owner
        foreach ($property in @('provider', 'repository', 'name')) {
            Assert-Nonblank `
                -Value ([string](Get-RequiredProperty `
                    $workflow `
                    $property `
                    "$owner workflow")) `
                -Owner "$owner workflow $property"
        }
        if ([int64](Get-RequiredProperty `
                $workflow `
                'runId' `
                "$owner workflow") -le 0 -or
            [int](Get-RequiredProperty `
                $workflow `
                'runAttempt' `
                "$owner workflow") -le 0) {
            throw "$owner workflow run identity must be positive."
        }
        Assert-Commit `
            -Value ([string](Get-RequiredProperty `
                $workflow `
                'sourceCommit' `
                "$owner workflow")) `
            -Owner "$owner workflow source commit"
        $workflowProvider = [string](Get-RequiredProperty `
            $workflow `
            'provider' `
            "$owner workflow")
        $workflowRepository = [string](Get-RequiredProperty `
            $workflow `
            'repository' `
            "$owner workflow")
        $workflowName = [string](Get-RequiredProperty `
            $workflow `
            'name' `
            "$owner workflow")
        $workflowPath = [string](Get-RequiredProperty `
            $workflow `
            'path' `
            "$owner workflow")
        $workflowEvent = [string](Get-RequiredProperty `
            $workflow `
            'event' `
            "$owner workflow")
        $workflowRunId = [int64](Get-RequiredProperty `
            $workflow `
            'runId' `
            "$owner workflow")
        $workflowRunAttempt = [int](Get-RequiredProperty `
            $workflow `
            'runAttempt' `
            "$owner workflow")
        $workflowEvidenceUrl = [string](Get-RequiredProperty `
            $workflow `
            'evidenceUrl' `
            "$owner workflow")
        $sourceCommit = [string](Get-RequiredProperty `
            $workflow `
            'sourceCommit' `
            "$owner workflow")
        if ($null -eq $pilotRepository) {
            $pilotRepository = $workflowRepository
            $pilotSourceCommit = $sourceCommit
            if (-not $pilotRepositories.Add($pilotRepository)) {
                throw (
                    'Each pilot ID must identify a distinct pilot ' +
                    'repository.')
            }
        }
        elseif ($workflowRepository -ne $pilotRepository -or
            $sourceCommit -ne $pilotSourceCommit) {
            throw (
                "$owner changed the pilot repository or source commit; the " +
                'four-week qualification cycle must restart.')
        }
        Assert-EvidenceUrl `
            -Value $workflowEvidenceUrl `
            -Owner "$owner workflow"
        if ($workflowProvider -cne 'github-actions') {
            throw "$owner workflow provider must be github-actions."
        }
        if ($workflowPath -cne
                '.github/workflows/sharpproof-strict-weekly.yml' -or
            $workflowEvent -cne 'workflow_dispatch') {
            throw (
                "$owner workflow must use the frozen SharpProof strict " +
                'weekly workflow_dispatch path.')
        }
        $authenticatedRun = Get-AuthenticatedGitHubWorkflowRun `
            -Repository $workflowRepository `
            -WorkflowName $workflowName `
            -WorkflowPath $workflowPath `
            -Event $workflowEvent `
            -RunId $workflowRunId `
            -RunAttempt $workflowRunAttempt `
            -SourceCommit $sourceCommit `
            -EvidenceUrl $workflowEvidenceUrl `
            -Owner "$owner workflow"
        $authenticatedWeekEnding = (
            $authenticatedRun.updatedAt.UtcDateTime.ToString(
                'yyyy-MM-dd',
                [Globalization.CultureInfo]::InvariantCulture))
        if ($authenticatedWeekEnding -cne $weekEnding -or
            $authenticatedRun.startedAt -lt $qualifiedAt -or
            $authenticatedRun.updatedAt -gt $evidenceCommitTime) {
            throw (
                "$owner workflow timestamps must authenticate the declared " +
                'week-ending date, follow RC qualification, and not postdate ' +
                "the evidence commit (started " +
                "$($authenticatedRun.startedAt.ToString('o')), updated " +
                "$($authenticatedRun.updatedAt.ToString('o'))).")
        }
        $artifactSha256 = [string](Get-RequiredProperty `
            $workflow `
            'artifactSha256' `
            "$owner workflow")
        $recordSha256 = [string](Get-RequiredProperty `
            $workflow `
            'recordSha256' `
            "$owner workflow")
        $artifactName = (
            "sharpproof-pilot-evidence-$sourceCommit-$workflowRunId-" +
            "$workflowRunAttempt")
        $pilotRecord = Get-SharpProofGitHubArtifactRecord `
            -Repository $workflowRepository `
            -RunId $workflowRunId `
            -RunAttempt $workflowRunAttempt `
            -SourceCommit $sourceCommit `
            -ArtifactName $artifactName `
            -ArchiveSha256 $artifactSha256 `
            -RecordPath 'sharpproof-pilot-evidence.json' `
            -RecordSha256 $recordSha256 `
            -MaximumArchiveBytes 32MB `
            -RequireSingleRecord `
            -AttemptStartedAt $authenticatedRun.startedAt `
            -AttemptCompletedAt $authenticatedRun.updatedAt `
            -Owner "$owner workflow"
        Assert-PilotArtifactRecord `
            -Record $pilotRecord `
            -PilotId $pilotId `
            -SelectedClaims $selectedClaims `
            -Package $package `
            -Runtime $runtime `
            -Tool $tool `
            -Policy $policy `
            -Cycle $cycle `
            -Workflow $workflow `
            -Owner $owner
        $workflowKey = (
            "$workflowProvider|$workflowRepository|$workflowName|" +
            "$workflowRunId|$workflowRunAttempt")
        if (-not $workflowRuns.Add($workflowKey) -or
            -not $workflowEvidenceUrls.Add($workflowEvidenceUrl)) {
            throw (
                "$owner must identify a unique immutable workflow run and " +
                'evidence URL.')
        }
    }
    if (($previousDate - $qualifiedAt.UtcDateTime.Date).TotalDays -lt 21) {
        throw (
            "Pilot '$pilotId' does not span four qualified weekly cycles " +
            'after RC qualification.')
    }
}
if ($totalClaims -lt 100) {
    throw (
        "Pilot evidence contains only $totalClaims selected claims; " +
        '100 are required.')
}

$openDefects = Get-RequiredProperty `
    $evidence `
    'openDefects' `
    'Human release evidence'
if ([int](Get-RequiredProperty $openDefects 'p0' 'Open defects') -ne 0 -or
    [int](Get-RequiredProperty $openDefects 'p1' 'Open defects') -ne 0) {
    throw 'Final 1.0 cannot have an open P0 or P1 defect.'
}
Assert-EvidenceUrl `
    -Value ([string](Get-RequiredProperty `
        $openDefects `
        'evidenceUrl' `
        'Open defects')) `
    -Owner 'Open-defect evidence'

$reviews = @(
    Get-RequiredProperty `
        $evidence `
        'soundnessReviews' `
        'Human release evidence'
)
if ($reviews.Count -lt 2) {
    throw 'Final 1.0 requires at least two independent soundness reviews.'
}
$reviewers = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($review in $reviews) {
    $reviewer = [string](Get-RequiredProperty `
        $review `
        'reviewer' `
        'Soundness review')
    if ([string]::IsNullOrWhiteSpace($reviewer) -or
        -not $reviewers.Add($reviewer) -or
        (Get-RequiredProperty `
            $review `
            'independent' `
            "Soundness review by '$reviewer'") -isnot [bool] -or
        -not [bool](Get-RequiredProperty `
            $review `
            'independent' `
            "Soundness review by '$reviewer'") -or
        [string](Get-RequiredProperty `
            $review `
            'productCommit' `
            "Soundness review by '$reviewer'") -ne
                $ExpectedProductCommit -or
        [string](Get-RequiredProperty `
            $review `
            'disposition' `
            "Soundness review by '$reviewer'") -ne 'approved') {
        throw (
            'Every soundness review must have a distinct reviewer, be ' +
            'independent, and approve the exact product commit.')
    }
    Assert-EvidenceUrl `
        -Value ([string](Get-RequiredProperty `
            $review `
            'evidenceUrl' `
            "Soundness review by '$reviewer'")) `
        -Owner "Soundness review by '$reviewer'"
}

$governance = Get-RequiredProperty `
    $evidence `
    'governance' `
    'Human release evidence'
foreach ($property in @(
        'protectedDefaultBranch',
        'protectedReleaseTags',
        'protectedPublishingEnvironments',
        'requiredChecks',
        'independentReviewRequired')) {
    $value = Get-RequiredProperty `
        $governance `
        $property `
        'Governance evidence'
    if ($value -isnot [bool] -or -not [bool]$value) {
        throw "Governance evidence '$property' must be true."
    }
}
Assert-EvidenceUrl `
    -Value ([string](Get-RequiredProperty `
        $governance `
        'evidenceUrl' `
        'Governance evidence')) `
    -Owner 'Governance evidence'

$validation = [pscustomobject][ordered]@{
    schemaVersion = 3
    status = 'passed'
    releaseTag = 'v1.0.0'
    productCommit = $ExpectedProductCommit
    evidenceRef = $EvidenceRef
    evidenceTagObject = $evidenceTagObject
    evidenceCommit = $evidenceCommit
    evidenceDocumentSha256 = $evidenceDocumentSha256
    qualifiedRc = [pscustomobject][ordered]@{
        releaseTag = $qualifiedRcTag
        productCommit = $qualifiedRcCommit
        packageVersion = $qualifiedRcPackageVersion
        productionDigestSha256 = $qualifiedRcProductionDigest
        trustedComputingBaseDigestSha256 = $qualifiedRcTcbDigest
        qualifiedAtUtc = $qualifiedAt.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            [Globalization.CultureInfo]::InvariantCulture)
        qualificationArtifactSha256 = [string](
            Get-RequiredProperty `
                (Get-RequiredProperty `
                    $qualifiedRc `
                    'workflow' `
                    'Qualified RC') `
                'qualificationArtifactSha256' `
                'Qualified RC workflow')
        qualificationRecordSha256 = [string](
            Get-RequiredProperty `
                (Get-RequiredProperty `
                    $qualifiedRc `
                    'workflow' `
                    'Qualified RC') `
                'qualificationRecordSha256' `
                'Qualified RC workflow')
        packageArtifactSha256 = [string](
            Get-RequiredProperty `
                (Get-RequiredProperty `
                    $qualifiedRc `
                    'workflow' `
                    'Qualified RC') `
                'packageArtifactSha256' `
                'Qualified RC workflow')
        releaseManifestSha256 = [string](
            Get-RequiredProperty `
                (Get-RequiredProperty `
                    $qualifiedRc `
                    'package' `
                    'Qualified RC') `
                'releaseManifestSha256' `
                'Qualified RC package')
    }
    stableCandidate = [pscustomobject][ordered]@{
        productCommit = $ExpectedProductCommit
        packageVersion = $stableCandidatePackageVersion
        productionDigestSha256 = $stableProductionDigest
        trustedComputingBaseDigestSha256 = $stableTcbDigest
    }
    pilots = $pilots.Count
    selectedClaims = $totalClaims
    soundnessReviews = $reviews.Count
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
        [IO.Path]::GetFullPath($OutputPath)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
    }
    $outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutput)
    if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
        throw "OutputPath has no parent directory: '$OutputPath'."
    }
    [IO.Directory]::CreateDirectory($outputDirectory) |
        Out-Null
    $json = ($validation | ConvertTo-Json) -replace "`r`n", "`n"
    [IO.File]::WriteAllText(
        $resolvedOutput,
        $json + "`n",
        [Text.UTF8Encoding]::new($false))
}

Write-Host (
    "Validated externally tagged final 1.0 human gates for " +
    "$($pilots.Count) pilots, $totalClaims selected claims, and " +
    "$($reviews.Count) independent reviews at evidence commit " +
    "$evidenceCommit.")
