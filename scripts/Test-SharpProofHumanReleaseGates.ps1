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
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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

function Assert-EvidenceUrl {
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
        $uri.Host.EndsWith(
            '.invalid',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            "$Owner must contain an absolute HTTPS evidence URL and cannot " +
            'use a reserved placeholder domain.')
    }
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

    if ($Value -notmatch '^[0-9a-f]{64}$') {
        throw "$Owner must be a lowercase SHA-256 digest."
    }
}

function Assert-Commit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($Value -notmatch '^[0-9a-f]{40}$') {
        throw "$Owner must be a lowercase Git commit SHA."
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
            $copyTask.GetAwaiter().GetResult()
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
try {
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @('init', '--bare', '--quiet') `
        -Operation 'Evidence repository initialization'
    $localRef = 'refs/tags/sharpproof-human-release-evidence'
    $localBranch = 'refs/heads/sharpproof-human-release-evidence'
    $null = Invoke-Git `
        -Repository $temporaryRepository `
        -Arguments @(
            'fetch',
            '--quiet',
            '--no-tags',
            $EvidenceRepository,
            "+${EvidenceRef}:${localRef}",
            "+${evidenceBranchRef}:${localBranch}") `
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
        'Human release evidence') -ne 2 -or
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
        'Human release evidence must use schema 2 and identify the exact ' +
        'v1.0.0 product commit and immutable evidence ref.')
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
    if ($packageVersion -ne '1.0.0') {
        throw "$pilotOwner must use the exact final 1.0.0 package version."
    }
    Assert-Sha256 `
        -Value ([string](Get-RequiredProperty `
            $package `
            'releaseManifestSha256' `
            "$pilotOwner package")) `
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
    foreach ($artifact in $packageArtifacts) {
        $packageId = [string](Get-RequiredProperty `
            $artifact `
            'id' `
            "$pilotOwner package artifact")
        $actualPackageIds.Add($packageId)
        Assert-Sha256 `
            -Value ([string](Get-RequiredProperty `
                $artifact `
                'sha256' `
                "$pilotOwner package '$packageId'")) `
            -Owner "$pilotOwner package '$packageId'"
    }
    if (($actualPackageIds -join '|') -ne
        ($expectedPackages -join '|')) {
        throw (
            "$pilotOwner package evidence must preserve the exact " +
            'dependency-ordered package IDs.')
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
            "$pilotOwner tool") -ne $ExpectedProductCommit -or
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
            "$pilotOwner tool identity must match the exact product commit, " +
            'package version, and current worker protocol schemas.')
    }
    Assert-Sha256 `
        -Value ([string](Get-RequiredProperty `
            $tool `
            'workerBinarySha256' `
            "$pilotOwner tool")) `
        -Owner "$pilotOwner worker binary"

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
        $previousDate = $date
        $owner = "Pilot '$pilotId' cycle $weekEnding"

        $outcomes = Get-RequiredProperty $cycle 'outcomes' $owner
        if ([int](Get-RequiredProperty `
                $outcomes `
                'selectedClaims' `
                "$owner outcomes") -ne $selectedClaims -or
            [int](Get-RequiredProperty `
                $outcomes `
                'proven' `
                "$owner outcomes") -ne $selectedClaims -or
            [int](Get-RequiredProperty `
                $outcomes `
                'refuted' `
                "$owner outcomes") -ne 0 -or
            [int](Get-RequiredProperty `
                $outcomes `
                'unknown' `
                "$owner outcomes") -ne 0 -or
            [int](Get-RequiredProperty `
                $outcomes `
                'assumptions' `
                "$owner outcomes") -ne 0 -or
            [int](Get-RequiredProperty `
                $outcomes `
                'infrastructureFailures' `
                "$owner outcomes") -ne 0) {
            throw (
                "$owner must prove every selected claim with no refutation, " +
                'Unknown, assumption, or infrastructure failure.')
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
        Assert-EvidenceUrl `
            -Value $workflowEvidenceUrl `
            -Owner "$owner workflow"
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
    schemaVersion = 1
    status = 'passed'
    releaseTag = 'v1.0.0'
    productCommit = $ExpectedProductCommit
    evidenceRef = $EvidenceRef
    evidenceTagObject = $evidenceTagObject
    evidenceCommit = $evidenceCommit
    evidenceDocumentSha256 = $evidenceDocumentSha256
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
