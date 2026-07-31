[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('running', 'failed', 'passed')]
    [string]$Status,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ReleaseCommit,

    [Parameter()]
    [string]$CoverageBaselineCommit,

    [Parameter()]
    [string]$HumanEvidencePath,

    [Parameter()]
    [ValidatePattern('^[^\r\n]+$')]
    [string]$HumanEvidenceRepository,

    [Parameter()]
    [ValidateSet(
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
        'humanEvidence')]
    [string]$Gate,

    [Parameter()]
    [ValidateSet('running', 'passed')]
    [string]$GateStatus,

    [Parameter()]
    [string]$GateEvidencePath,

    [Parameter()]
    [string]$ImmutableReceiptDirectory,

    [Parameter()]
    [string]$FailureKind,

    [Parameter()]
    [string]$OutputPath =
        'artifacts/release-qualification/qualification.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$isFinal = $Tag -eq 'v1.0.0'
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
$receiptDirectory = Join-Path `
    $outputDirectory `
    ([IO.Path]::GetFileNameWithoutExtension($resolvedOutput) + '-receipts')

function Test-Commit {
    param([string]$Value)

    return $Value -match '^[0-9a-f]{40}$' -and
        $Value -ne ('0' * 40)
}

function Test-Sha256 {
    param([string]$Value)

    return $Value -match '^[0-9a-f]{64}$' -and
        $Value -ne ('0' * 64)
}

function ConvertTo-UtcTimestamp {
    param([object]$Value)

    $parsed = [DateTimeOffset]::MinValue
    if ($Value -is [DateTimeOffset]) {
        $parsed = ([DateTimeOffset]$Value).ToUniversalTime()
    }
    elseif ($Value -is [DateTime]) {
        $parsed = [DateTimeOffset]::new(
            ([DateTime]$Value).ToUniversalTime())
    }
    else {
        $text = [string]$Value
        if ($text -notmatch
                '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' -or
            -not [DateTimeOffset]::TryParse(
                $text,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal -bor
                    [Globalization.DateTimeStyles]::AdjustToUniversal,
                [ref]$parsed)) {
            return $null
        }
    }
    return $parsed.ToString(
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        [Globalization.CultureInfo]::InvariantCulture)
}

function New-EmptyHumanEvidence {
    param([string]$EvidenceStatus)

    return [pscustomobject][ordered]@{
        status = $EvidenceStatus
        ref = $null
        tagObject = $null
        commit = $null
        documentSha256 = $null
        qualifiedRc = $null
        stableCandidate = $null
    }
}

function Read-HumanEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolvedHumanEvidence = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
    }
    if (-not (Test-Path `
            -LiteralPath $resolvedHumanEvidence `
            -PathType Leaf)) {
        throw "Human evidence validation is missing: $Path"
    }
    $validation = Get-Content `
        -LiteralPath $resolvedHumanEvidence `
        -Raw |
        ConvertFrom-Json
    $qualifiedRc = $validation.PSObject.Properties['qualifiedRc']
    $stableCandidate = $validation.PSObject.Properties['stableCandidate']
    if ([int]$validation.schemaVersion -ne 3 -or
        [string]$validation.status -ne 'passed' -or
        [string]$validation.releaseTag -ne $Tag -or
        [string]$validation.productCommit -ne $ReleaseCommit -or
        [string]$validation.evidenceRef -ne
            'refs/tags/evidence/v1.0.0' -or
        -not (Test-Commit ([string]$validation.evidenceTagObject)) -or
        -not (Test-Commit ([string]$validation.evidenceCommit)) -or
        -not (Test-Sha256 `
            ([string]$validation.evidenceDocumentSha256)) -or
        $null -eq $qualifiedRc -or
        $null -eq $stableCandidate) {
        throw (
            'Human evidence validation does not bind the exact final ' +
            'product commit and immutable evidence tag.')
    }
    $qualifiedRcValue = $qualifiedRc.Value
    $stableCandidateValue = $stableCandidate.Value
    $qualifiedAtUtc = ConvertTo-UtcTimestamp `
        $qualifiedRcValue.qualifiedAtUtc
    if ([string]$qualifiedRcValue.releaseTag -notmatch
            '^v1\.0\.0-rc\.[0-9]+$' -or
        -not (Test-Commit ([string]$qualifiedRcValue.productCommit)) -or
        [string]$qualifiedRcValue.productCommit -eq $ReleaseCommit -or
        [string]$qualifiedRcValue.packageVersion -ne
            ([string]$qualifiedRcValue.releaseTag).Substring(1) -or
        -not (Test-Sha256 `
            ([string]$qualifiedRcValue.productionDigestSha256)) -or
        -not (Test-Sha256 `
            ([string]$qualifiedRcValue.trustedComputingBaseDigestSha256)) -or
        -not (Test-Sha256 `
            ([string]$qualifiedRcValue.qualificationArtifactSha256)) -or
        -not (Test-Sha256 `
            ([string]$qualifiedRcValue.qualificationRecordSha256)) -or
        -not (Test-Sha256 `
            ([string]$qualifiedRcValue.packageArtifactSha256)) -or
        -not (Test-Sha256 `
            ([string]$qualifiedRcValue.releaseManifestSha256)) -or
        $null -eq $qualifiedAtUtc -or
        [string]$stableCandidateValue.productCommit -ne $ReleaseCommit -or
        [string]$stableCandidateValue.packageVersion -ne '1.0.0' -or
        [string]$stableCandidateValue.productionDigestSha256 -ne
            [string]$qualifiedRcValue.productionDigestSha256 -or
        [string]$stableCandidateValue.trustedComputingBaseDigestSha256 -ne
            [string]$qualifiedRcValue.trustedComputingBaseDigestSha256) {
        throw (
            'Human evidence validation does not preserve the qualified RC ' +
            'production and trusted-computing-base identity.')
    }
    return [pscustomobject][ordered]@{
        status = 'passed'
        ref = [string]$validation.evidenceRef
        tagObject = [string]$validation.evidenceTagObject
        commit = [string]$validation.evidenceCommit
        documentSha256 = [string]$validation.evidenceDocumentSha256
        qualifiedRc = [pscustomobject][ordered]@{
            releaseTag = [string]$qualifiedRcValue.releaseTag
            productCommit = [string]$qualifiedRcValue.productCommit
            packageVersion = [string]$qualifiedRcValue.packageVersion
            productionDigestSha256 =
                [string]$qualifiedRcValue.productionDigestSha256
            trustedComputingBaseDigestSha256 =
                [string]$qualifiedRcValue.trustedComputingBaseDigestSha256
            qualifiedAtUtc = $qualifiedAtUtc
            qualificationArtifactSha256 =
                [string]$qualifiedRcValue.qualificationArtifactSha256
            qualificationRecordSha256 =
                [string]$qualifiedRcValue.qualificationRecordSha256
            packageArtifactSha256 =
                [string]$qualifiedRcValue.packageArtifactSha256
            releaseManifestSha256 =
                [string]$qualifiedRcValue.releaseManifestSha256
        }
        stableCandidate = [pscustomobject][ordered]@{
            productCommit = [string]$stableCandidateValue.productCommit
            packageVersion = [string]$stableCandidateValue.packageVersion
            productionDigestSha256 =
                [string]$stableCandidateValue.productionDigestSha256
            trustedComputingBaseDigestSha256 =
                [string]$stableCandidateValue.trustedComputingBaseDigestSha256
        }
    }
}

function ConvertTo-HumanEvidenceIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Evidence
    )

    return $Evidence | ConvertTo-Json -Depth 8 -Compress
}

function Invoke-HumanEvidenceRevalidation {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EvidenceRepository
    )

    if ($EvidenceRepository.StartsWith(
            '-',
            [StringComparison]::Ordinal)) {
        throw (
            'HumanEvidenceRepository cannot begin with a command-line ' +
            'option prefix.')
    }
    [IO.Directory]::CreateDirectory($outputDirectory) |
        Out-Null
    $temporaryValidation = Join-Path `
        $outputDirectory `
        ('.human-evidence.' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        & (Join-Path `
                $PSScriptRoot `
                'Test-SharpProofHumanReleaseGates.ps1') `
            -ExpectedProductCommit $ReleaseCommit `
            -EvidenceRepository $EvidenceRepository `
            -EvidenceRef 'refs/tags/evidence/v1.0.0' `
            -OutputPath $temporaryValidation
        return Read-HumanEvidence -Path $temporaryValidation
    }
    finally {
        if ([IO.File]::Exists($temporaryValidation)) {
            [IO.File]::Delete($temporaryValidation)
        }
    }
}

function Get-FileSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [IO.File]::OpenRead($Path)
    try {
        return [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                $stream)).ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
    }
}

function Get-GitHubRunIdentity {
    $required = [ordered]@{
        repository = $env:GITHUB_REPOSITORY
        runId = $env:GITHUB_RUN_ID
        runAttempt = $env:GITHUB_RUN_ATTEMPT
        workflowRef = $env:GITHUB_WORKFLOW_REF
        job = $env:GITHUB_JOB
        ref = $env:GITHUB_REF
        refName = $env:GITHUB_REF_NAME
        sha = $env:GITHUB_SHA
    }
    if ($env:GITHUB_ACTIONS -ne 'true') {
        throw (
            'Release qualification gate evidence must be recorded by ' +
            'GitHub Actions.')
    }
    foreach ($entry in $required.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.Value) -or
            [string]$entry.Value -match '[\r\n]') {
            throw (
                "GitHub Actions run identity '$($entry.Key)' is missing " +
                'or malformed.')
        }
    }
    if ([string]$required.runId -notmatch '^[1-9][0-9]*$' -or
        [string]$required.runAttempt -notmatch '^[1-9][0-9]*$' -or
        [string]$required.repository -notmatch
            '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
        -not ([string]$required.workflowRef).StartsWith(
            ([string]$required.repository + '/.github/workflows/'),
            [StringComparison]::Ordinal) -or
        -not ([string]$required.workflowRef).EndsWith(
            ("@refs/tags/$Tag"),
            [StringComparison]::Ordinal) -or
        [string]$required.job -ne 'release-qualification' -or
        [string]$required.ref -ne "refs/tags/$Tag" -or
        [string]$required.refName -ne $Tag -or
        [string]$required.sha -ne $ReleaseCommit) {
        throw (
            'GitHub Actions run identity does not match the release ' +
            'qualification candidate.')
    }
    return [pscustomobject][ordered]@{
        provider = 'github-actions'
        repository = [string]$required.repository
        runId = [string]$required.runId
        runAttempt = [string]$required.runAttempt
        workflowRef = [string]$required.workflowRef
        job = [string]$required.job
        ref = [string]$required.ref
        sha = [string]$required.sha
    }
}

function Resolve-GateEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolved = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
    }
    $relativeToOutput = [IO.Path]::GetRelativePath(
        $outputDirectory,
        $resolved)
    if ($relativeToOutput -eq '..' -or
        $relativeToOutput.StartsWith(
            '..' + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relativeToOutput)) {
        throw (
            'GateEvidencePath must identify a file under the release ' +
            'qualification output directory.')
    }
    if (-not [IO.File]::Exists($resolved)) {
        throw "Gate evidence file is missing: '$Path'."
    }
    $relativeToRepository = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $resolved)
    return [pscustomobject][ordered]@{
        path = $relativeToRepository.Replace('\', '/')
        sha256 = Get-FileSha256 -Path $resolved
    }
}

function Get-ImmutableReceiptPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GateName
    )

    if ([string]::IsNullOrWhiteSpace($ImmutableReceiptDirectory)) {
        throw (
            'Passed qualification requires an immutable receipt directory ' +
            'downloaded from GitHub Actions artifacts.')
    }
    $resolvedDirectory = if (
        [IO.Path]::IsPathRooted($ImmutableReceiptDirectory)) {
        [IO.Path]::GetFullPath($ImmutableReceiptDirectory)
    }
    else {
        [IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot $ImmutableReceiptDirectory))
    }
    $relativeToOutput = [IO.Path]::GetRelativePath(
        $outputDirectory,
        $resolvedDirectory)
    if ($relativeToOutput -eq '..' -or
        $relativeToOutput.StartsWith(
            '..' + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relativeToOutput) -or
        $relativeToOutput -ne 'immutable-receipts') {
        throw (
            'ImmutableReceiptDirectory must be the immutable-receipts ' +
            'directory under the release qualification output directory.')
    }
    if (-not [IO.Directory]::Exists($resolvedDirectory)) {
        throw (
            'Immutable release qualification receipt directory is ' +
            "missing: '$ImmutableReceiptDirectory'.")
    }
    return Join-Path $resolvedDirectory "$GateName.json"
}

function Get-GateReceiptPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GateName
    )

    return Join-Path $receiptDirectory "$GateName.json"
}

function Write-GateReceipt {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GateName,

        [Parameter(Mandatory = $true)]
        [object]$Evidence,

        [Parameter(Mandatory = $true)]
        [string]$EvidencePath
    )

    [IO.Directory]::CreateDirectory($receiptDirectory) |
        Out-Null
    $runIdentity = Get-GitHubRunIdentity
    $gateEvidence = Resolve-GateEvidence -Path $EvidencePath
    $receipt = [pscustomobject][ordered]@{
        schemaVersion = 2
        tag = $Tag
        releaseCommit = $ReleaseCommit
        gate = $GateName
        status = 'passed'
        run = $runIdentity
        evidence = $gateEvidence
        humanEvidenceDocumentSha256 = if (
            $GateName -eq 'humanEvidence') {
            [string]$Evidence.documentSha256
        }
        else {
            $null
        }
    }
    $receiptPath = Get-GateReceiptPath -GateName $GateName
    $temporaryPath = Join-Path `
        $receiptDirectory `
        (".$GateName." + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $json = ($receipt | ConvertTo-Json) -replace "`r`n", "`n"
        [IO.File]::WriteAllText(
            $temporaryPath,
            $json + "`n",
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $receiptPath, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
    return Get-FileSha256 -Path $receiptPath
}

function Assert-GateReceipt {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GateName,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha256
    )

    if (-not (Test-Sha256 $ExpectedSha256)) {
        throw "Gate '$GateName' has no valid transition receipt digest."
    }
    $receiptPath = Get-GateReceiptPath -GateName $GateName
    if (-not [IO.File]::Exists($receiptPath) -or
        (Get-FileSha256 -Path $receiptPath) -ne $ExpectedSha256) {
        throw "Gate '$GateName' transition receipt is missing or changed."
    }
    $immutableReceiptPath = Get-ImmutableReceiptPath -GateName $GateName
    if (-not [IO.File]::Exists($immutableReceiptPath) -or
        (Get-FileSha256 -Path $immutableReceiptPath) -ne $ExpectedSha256) {
        throw (
            "Gate '$GateName' receipt does not match its immutable GitHub " +
            'Actions artifact.')
    }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw |
        ConvertFrom-Json
    $currentRun = Get-GitHubRunIdentity
    $receiptRun = $receipt.PSObject.Properties['run']
    $receiptEvidence = $receipt.PSObject.Properties['evidence']
    if ([int]$receipt.schemaVersion -ne 2 -or
        [string]$receipt.tag -ne $Tag -or
        [string]$receipt.releaseCommit -ne $ReleaseCommit -or
        [string]$receipt.gate -ne $GateName -or
        [string]$receipt.status -ne 'passed' -or
        $null -eq $receiptRun -or
        $null -eq $receiptEvidence -or
        [string]$receiptRun.Value.provider -ne
            [string]$currentRun.provider -or
        [string]$receiptRun.Value.repository -ne
            [string]$currentRun.repository -or
        [string]$receiptRun.Value.runId -ne [string]$currentRun.runId -or
        [string]$receiptRun.Value.runAttempt -ne
            [string]$currentRun.runAttempt -or
        [string]$receiptRun.Value.workflowRef -ne
            [string]$currentRun.workflowRef -or
        [string]$receiptRun.Value.job -ne [string]$currentRun.job -or
        [string]$receiptRun.Value.ref -ne [string]$currentRun.ref -or
        [string]$receiptRun.Value.sha -ne [string]$currentRun.sha -or
        -not (Test-Sha256 ([string]$receiptEvidence.Value.sha256))) {
        throw "Gate '$GateName' transition receipt is malformed."
    }
    $currentEvidence = Resolve-GateEvidence `
        -Path ([string]$receiptEvidence.Value.path)
    if ([string]$currentEvidence.path -ne
            [string]$receiptEvidence.Value.path -or
        [string]$currentEvidence.sha256 -ne
            [string]$receiptEvidence.Value.sha256) {
        throw "Gate '$GateName' result evidence is missing or changed."
    }
    if ($GateName -eq 'humanEvidence' -and
        [string]$receipt.humanEvidenceDocumentSha256 -ne
            [string]$humanEvidence.documentSha256) {
        throw "Gate '$GateName' receipt does not bind its evidence."
    }
}

function Read-ExistingQualification {
    if (-not (Test-Path -LiteralPath $resolvedOutput -PathType Leaf)) {
        throw (
            'Release qualification progress is missing. Initialize it with ' +
            '-Status running before recording gates or a terminal status.')
    }
    $existing = Get-Content -LiteralPath $resolvedOutput -Raw |
        ConvertFrom-Json
    $existingRun = $existing.PSObject.Properties['run']
    if ([int]$existing.schemaVersion -ne 5 -or
        [string]$existing.status -ne 'running' -or
        [string]$existing.tag -ne $Tag -or
        [string]$existing.releaseCommit -ne $ReleaseCommit -or
        $null -eq $existingRun -or
        (ConvertTo-Json $existingRun.Value -Compress) -ne
            (ConvertTo-Json $runIdentity -Compress)) {
        throw (
            'Existing release qualification progress does not identify the ' +
            'same GitHub Actions run and release candidate.')
    }
    return $existing
}

if (($Gate -and -not $GateStatus) -or
    ($GateStatus -and -not $Gate)) {
    throw 'Gate and GateStatus must be supplied together.'
}
if ($Status -ne 'running' -and ($Gate -or $GateStatus)) {
    throw 'Individual gate updates require Status running.'
}
if ($Status -eq 'failed') {
    if ([string]::IsNullOrWhiteSpace($FailureKind) -or
        $FailureKind -match '[\r\n]') {
        throw 'Failed qualification requires a single-line FailureKind.'
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($FailureKind)) {
    throw 'FailureKind is valid only for failed qualification.'
}
if (-not [string]::IsNullOrWhiteSpace($GateEvidencePath) -and
    -not ($Status -eq 'running' -and $GateStatus -eq 'passed')) {
    throw (
        'GateEvidencePath is valid only when recording a passed gate.')
}
if ($Status -eq 'running' -and
    $GateStatus -eq 'passed' -and
    [string]::IsNullOrWhiteSpace($GateEvidencePath)) {
    throw 'A passed gate requires GateEvidencePath.'
}
if (-not [string]::IsNullOrWhiteSpace($ImmutableReceiptDirectory) -and
    $Status -ne 'passed') {
    throw (
        'ImmutableReceiptDirectory is valid only for terminal passed ' +
        'qualification.')
}
if ($Status -eq 'passed' -and
    [string]::IsNullOrWhiteSpace($ImmutableReceiptDirectory)) {
    throw (
        'Passed qualification requires ImmutableReceiptDirectory.')
}
if (-not [string]::IsNullOrWhiteSpace($CoverageBaselineCommit) -and
    -not (Test-Commit $CoverageBaselineCommit)) {
    throw 'CoverageBaselineCommit must be a lowercase Git commit SHA.'
}
$runIdentity = Get-GitHubRunIdentity

$isInitialization = $Status -eq 'running' -and
    [string]::IsNullOrWhiteSpace($Gate)
$isHumanEvidenceGatePass = $Status -eq 'running' -and
    $Gate -eq 'humanEvidence' -and
    $GateStatus -eq 'passed'
$isFinalPass = $Status -eq 'passed' -and $isFinal
if (-not [string]::IsNullOrWhiteSpace($HumanEvidencePath) -and
    -not ($isHumanEvidenceGatePass -or $isFinalPass)) {
    throw (
        'HumanEvidencePath is valid only when passing the final human-' +
        'evidence gate or completing final 1.0 qualification.')
}
if (-not [string]::IsNullOrWhiteSpace($HumanEvidenceRepository) -and
    -not $isFinalPass) {
    throw (
        'HumanEvidenceRepository is valid only when completing final 1.0 ' +
        'qualification.')
}
if ($isFinalPass -and
    ([string]::IsNullOrWhiteSpace($HumanEvidencePath) -or
        [string]::IsNullOrWhiteSpace($HumanEvidenceRepository))) {
    throw (
        'Final 1.0 qualification requires the human-evidence validation ' +
        'path and evidence repository for terminal revalidation.')
}
$qualificationExists = Test-Path `
    -LiteralPath $resolvedOutput `
    -PathType Leaf
if ($isInitialization -and $qualificationExists) {
    throw (
        'Release qualification progress already exists; initialization ' +
        'cannot overwrite it.')
}
if ($isInitialization -and
    [IO.Directory]::Exists($receiptDirectory)) {
    throw (
        'Release qualification transition receipts already exist; ' +
        'initialization cannot reuse them.')
}
if ($Status -eq 'failed' -and
    $qualificationExists) {
    $terminal = Get-Content -LiteralPath $resolvedOutput -Raw |
        ConvertFrom-Json
    if ([int]$terminal.schemaVersion -eq 5 -and
        [string]$terminal.status -eq 'failed' -and
        [string]$terminal.tag -eq $Tag -and
        [string]$terminal.releaseCommit -eq $ReleaseCommit -and
        (ConvertTo-Json $terminal.run -Compress) -eq
            (ConvertTo-Json $runIdentity -Compress)) {
        Write-Host (
            "Release qualification for $Tag at $ReleaseCommit is already " +
            'recorded as failed.')
        return
    }
}
$gateStates = [ordered]@{}
$gateReceipts = [ordered]@{}
$humanEvidence = $null
$storedCoverageBaseline = $null
$isFailureInitialization = $Status -eq 'failed' -and
    -not $qualificationExists
if ($isInitialization -or $isFailureInitialization) {
    foreach ($gateName in $gateNames) {
        $gateStates[$gateName] = if ($isFailureInitialization) {
            if ($gateName -eq 'humanEvidence' -and -not $isFinal) {
                'not-required'
            }
            else {
                'not-run'
            }
        }
        elseif ($gateName -eq 'humanEvidence' -and -not $isFinal) {
            'not-required'
        }
        else {
            'pending'
        }
        $gateReceipts[$gateName] = $null
    }
    $humanEvidence = New-EmptyHumanEvidence `
        -EvidenceStatus $gateStates['humanEvidence']
}
else {
    $existing = Read-ExistingQualification
    foreach ($gateName in $gateNames) {
        $property = $existing.gates.PSObject.Properties[$gateName]
        if ($null -eq $property -or
            [string]$property.Value -notin @(
                'pending',
                'running',
                'passed',
                'failed',
                'not-run',
                'not-required')) {
            throw "Existing qualification gate '$gateName' is invalid."
        }
        $gateStates[$gateName] = [string]$property.Value
        $receiptProperty =
            $existing.gateReceipts.PSObject.Properties[$gateName]
        if ($null -eq $receiptProperty) {
            throw (
                "Existing qualification gate '$gateName' has no receipt " +
                'entry.')
        }
        $receiptDigest = [string]$receiptProperty.Value
        if ($gateStates[$gateName] -eq 'passed') {
            if (-not (Test-Sha256 $receiptDigest)) {
                throw (
                    "Existing qualification gate '$gateName' has no valid " +
                    'receipt digest.')
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($receiptDigest)) {
            throw (
                "Existing qualification gate '$gateName' has a receipt " +
                "before it passed.")
        }
        $gateReceipts[$gateName] = if (
            [string]::IsNullOrWhiteSpace($receiptDigest)) {
            $null
        }
        else {
            $receiptDigest
        }
    }
    $humanEvidence = $existing.humanEvidence
    $storedCoverageBaseline = [string]$existing.coverageBaselineCommit
}

if (-not [string]::IsNullOrWhiteSpace($CoverageBaselineCommit)) {
    if (-not [string]::IsNullOrWhiteSpace($storedCoverageBaseline) -and
        $storedCoverageBaseline -ne $CoverageBaselineCommit) {
        throw 'CoverageBaselineCommit cannot change during qualification.'
    }
    $storedCoverageBaseline = $CoverageBaselineCommit
}

if ($Status -eq 'running' -and -not $isInitialization) {
    $currentGateStatus = [string]$gateStates[$Gate]
    if ($GateStatus -eq 'running') {
        if ($currentGateStatus -ne 'pending') {
            throw (
                "Gate '$Gate' cannot start from status " +
                "'$currentGateStatus'.")
        }
        if (@($gateStates.Values | Where-Object { $_ -eq 'running' }).Count -ne
            0) {
            throw 'Only one release qualification gate may run at a time.'
        }
        $gateStates[$Gate] = 'running'
    }
    else {
        if ($currentGateStatus -ne 'running') {
            throw (
                "Gate '$Gate' cannot pass from status " +
                "'$currentGateStatus'.")
        }
        if ($Gate -eq 'humanEvidence') {
            if (-not $isFinal) {
                throw 'Human evidence is not required for preview or RC tags.'
            }
            if ([string]::IsNullOrWhiteSpace($HumanEvidencePath)) {
                throw (
                    'Passing the final human-evidence gate requires its ' +
                    'validated evidence path.')
            }
            $humanEvidence = Read-HumanEvidence -Path $HumanEvidencePath
        }
        $gateStates[$Gate] = 'passed'
        $gateReceipts[$Gate] = Write-GateReceipt `
            -GateName $Gate `
            -Evidence $humanEvidence `
            -EvidencePath $GateEvidencePath
    }
}
elseif ($Status -eq 'failed') {
    foreach ($gateName in $gateNames) {
        if ($gateStates[$gateName] -eq 'running') {
            $gateStates[$gateName] = 'failed'
            if ($gateName -eq 'humanEvidence') {
                $humanEvidence = New-EmptyHumanEvidence `
                    -EvidenceStatus 'failed'
            }
        }
        elseif ($gateStates[$gateName] -eq 'pending') {
            $gateStates[$gateName] = 'not-run'
            if ($gateName -eq 'humanEvidence') {
                $humanEvidence = New-EmptyHumanEvidence `
                    -EvidenceStatus 'not-run'
            }
        }
    }
}
elseif ($Status -eq 'passed') {
    if ($storedCoverageBaseline -notmatch '^[0-9a-f]{40}$' -or
        $storedCoverageBaseline -eq $ReleaseCommit) {
        throw (
            'Passed qualification requires a distinct lowercase coverage ' +
            'baseline commit SHA.')
    }
    foreach ($gateName in $gateNames) {
        $expected = if (
            $gateName -eq 'humanEvidence' -and -not $isFinal) {
            'not-required'
        }
        else {
            'passed'
        }
        if ($gateStates[$gateName] -ne $expected) {
            throw (
                "Passed qualification requires gate '$gateName' to be " +
                "'$expected'; actual status is '$($gateStates[$gateName])'.")
        }
        if ($expected -eq 'passed') {
            Assert-GateReceipt `
                -GateName $gateName `
                -ExpectedSha256 ([string]$gateReceipts[$gateName])
        }
    }
    if ($isFinal -and
        [string]$humanEvidence.status -ne 'passed') {
        throw 'Final 1.0 passed qualification requires human evidence.'
    }
    if ($isFinal) {
        $providedHumanEvidence = Read-HumanEvidence `
            -Path $HumanEvidencePath
        $revalidatedHumanEvidence = Invoke-HumanEvidenceRevalidation `
            -EvidenceRepository $HumanEvidenceRepository
        $expectedIdentity = ConvertTo-HumanEvidenceIdentity `
            -Evidence $revalidatedHumanEvidence
        if ((ConvertTo-HumanEvidenceIdentity `
                -Evidence $providedHumanEvidence) -ne
            $expectedIdentity -or
            (ConvertTo-HumanEvidenceIdentity `
                -Evidence $humanEvidence) -ne
            $expectedIdentity) {
            throw (
                'Final 1.0 human evidence changed after its gate passed or ' +
                'does not match fresh immutable-tag validation.')
        }
        $humanEvidence = $revalidatedHumanEvidence
    }
}

$qualification = [pscustomobject][ordered]@{
    schemaVersion = 5
    status = $Status
    failureKind = if ($Status -eq 'failed') {
        $FailureKind
    }
    else {
        $null
    }
    tag = $Tag
    releaseCommit = $ReleaseCommit
    run = $runIdentity
    coverageBaselineCommit = if (
        [string]::IsNullOrWhiteSpace($storedCoverageBaseline)) {
        $null
    }
    else {
        $storedCoverageBaseline
    }
    humanEvidence = $humanEvidence
    gates = [pscustomobject]$gateStates
    gateReceipts = [pscustomobject]$gateReceipts
}

[IO.Directory]::CreateDirectory($outputDirectory) |
    Out-Null
$json = ($qualification | ConvertTo-Json -Depth 8) -replace "`r`n", "`n"
$temporaryPath = Join-Path `
    $outputDirectory `
    ('.' + [IO.Path]::GetFileName($resolvedOutput) + '.' +
        [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::WriteAllText(
        $temporaryPath,
        $json + "`n",
        [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryPath, $resolvedOutput, $true)
}
finally {
    if ([IO.File]::Exists($temporaryPath)) {
        [IO.File]::Delete($temporaryPath)
    }
}

Write-Host (
    "Recorded release qualification status '$Status' for $Tag at " +
    "$ReleaseCommit.")
