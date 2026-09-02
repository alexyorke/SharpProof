[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedTag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-SharpProofSymbolPackages.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackagePayloads.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackageDependencies.ps1')
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseBundle.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseVersion = Get-SharpProofReleaseVersion `
    -RepositoryRoot $repositoryRoot
$resolvedSource = (Resolve-Path `
    -LiteralPath $PackageSource `
    -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedSource -PathType Container)) {
    throw "PackageSource is not a directory: $resolvedSource"
}
if (-not $ExpectedTag.StartsWith('v', [StringComparison]::Ordinal) -or
    -not (Test-SharpProofReleaseVersionSyntax `
        -Version $ExpectedTag.Substring(1))) {
    throw "Release tag must be v<SemVer>: $ExpectedTag"
}
$expectedVersion = $ExpectedTag.Substring(1)
Test-SharpProofReleaseVersion `
    -ExpectedVersion $releaseVersion `
    -ActualVersion $expectedVersion `
    -Owner 'Release tag'
$manifestPath = Join-Path $resolvedSource 'SharpProof.release.json'
$manifest = Read-SharpProofCanonicalReleaseJson `
    -Path $manifestPath `
    -DocumentType ReleaseManifest
if ($manifest.schemaVersion -ne 2) {
    throw 'Unsupported release evidence schema.'
}
if ([string]$manifest.packageVersion -ne $expectedVersion) {
    throw "Release tag '$ExpectedTag' does not match package version " +
        "'$($manifest.packageVersion)'."
}
Test-SharpProofReleaseVersionAuthority `
    -RepositoryRoot $repositoryRoot `
    -Authority $manifest.versionAuthority
$head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not resolve the release checkout commit.'
}
if ([string]$manifest.repository.type -ne 'git' -or
    [string]$manifest.repository.url -ne
        'https://github.com/alexyorke/SharpProof' -or
    [string]$manifest.repository.commit -ne $head) {
    throw 'Release evidence repository identity does not match the checkout.'
}

$artifacts = @($manifest.artifacts)
if ($artifacts.Count -ne 6 -or
    @($artifacts | Where-Object { $_.kind -eq 'package' }).Count -ne 3 -or
    @($artifacts | Where-Object { $_.kind -eq 'symbols' }).Count -ne 3) {
    throw 'Release evidence must contain three packages and three symbol packages.'
}
Test-SharpProofReleaseBundleTopology `
    -Directory $resolvedSource `
    -Artifacts $artifacts `
    -Owner 'Release artifact bundle'
$expectedPackageIds = @(
    'SharpProof',
    'SharpProof.Attributes',
    'SharpProof.Verifier'
) | Sort-Object
foreach ($kind in @('package', 'symbols')) {
    $actualPackageIds = @(
        $artifacts |
            Where-Object { [string]$_.kind -eq $kind } |
            ForEach-Object { [string]$_.packageId } |
            Sort-Object
    )
    if (($actualPackageIds -join '|') -ne
        ($expectedPackageIds -join '|')) {
        throw "Release evidence does not contain the exact $kind package graph."
    }
}
$expectedNames = @($artifacts |
    ForEach-Object { [string]$_.fileName } |
    Sort-Object)
$actualNames = @(
    Get-ChildItem -LiteralPath $resolvedSource -File |
        Where-Object {
            $_.Extension -in @('.nupkg', '.snupkg')
        } |
        ForEach-Object { $_.Name } |
        Sort-Object
)
if (($actualNames -join '|') -ne ($expectedNames -join '|')) {
    throw 'Release directory artifacts do not exactly match the evidence manifest.'
}
foreach ($artifact in $artifacts) {
    $path = Join-Path $resolvedSource ([string]$artifact.fileName)
    $file = Get-Item -LiteralPath $path -ErrorAction Stop
    if ([int64]$file.Length -ne [int64]$artifact.bytes) {
        throw "Release artifact size mismatch: $($artifact.fileName)"
    }
}
$payloadSets = @($manifest.packagePayloads)
if ($payloadSets.Count -ne $expectedPackageIds.Count -or
    ((@($payloadSets.packageId | Sort-Object) -join '|') -ne
        ($expectedPackageIds -join '|'))) {
    throw 'Release evidence does not contain the exact package payload graph.'
}
$catalogComponents = @(Get-SharpProofThirdPartyComponentGraph)
Test-SharpProofThirdPartyComponentProjection `
    -ActualComponents @($manifest.thirdPartyComponents) `
    -ExpectedComponents $catalogComponents
foreach ($packageId in $expectedPackageIds) {
    $mainArtifact = @(
        $artifacts |
            Where-Object {
                [string]$_.kind -eq 'package' -and
                [string]$_.packageId -eq $packageId
            }
    )[0]
    $symbolArtifact = @(
        $artifacts |
            Where-Object {
                [string]$_.kind -eq 'symbols' -and
                [string]$_.packageId -eq $packageId
            }
    )[0]
    $components = @(
        $catalogComponents |
            Where-Object { [string]$_.packageId -eq $packageId }
    )
    $null = Test-SharpProofPackagePayload `
        -PackagePath (Join-Path `
            $resolvedSource `
            ([string]$mainArtifact.fileName)) `
        -PackageId $packageId `
        -RepositoryRoot $repositoryRoot `
        -Components $components `
        -ExpectedPayloads @(
            $payloadSets |
                Where-Object { [string]$_.packageId -eq $packageId } |
                ForEach-Object { @($_.entries) }
        )
    $null = Test-SharpProofSymbolPackagePair `
        -PackagePath (Join-Path `
            $resolvedSource `
            ([string]$mainArtifact.fileName)) `
        -SymbolPackagePath (Join-Path `
            $resolvedSource `
            ([string]$symbolArtifact.fileName)) `
        -PackageId $packageId `
        -PackageVersion $expectedVersion `
        -RepositoryCommit $head
}
if (@($manifest.thirdPartyComponents).Count -eq 0 -or
    @($manifest.thirdPartyComponents |
        Where-Object { [string]$_.license -ne 'MIT' }).Count -ne 0) {
    throw 'Release evidence has incomplete third-party licensing metadata.'
}
$thirdPartyPackageIds = @(
    $manifest.thirdPartyComponents |
        ForEach-Object { [string]$_.packageId } |
        Sort-Object -Unique
)
$expectedThirdPartyPackageIds = @(
    'SharpProof',
    'SharpProof.Verifier'
) | Sort-Object
if (($thirdPartyPackageIds -join '|') -ne
    ($expectedThirdPartyPackageIds -join '|') -or
    @($manifest.thirdPartyComponents |
        Where-Object {
            [string]::IsNullOrWhiteSpace([string]$_.id) -or
            [string]::IsNullOrWhiteSpace([string]$_.version) -or
            @($_.entries).Count -eq 0
        }).Count -ne 0) {
    throw 'Release evidence has an invalid third-party component inventory.'
}
Write-Host (
    "Validated immutable SharpProof $expectedVersion artifacts for " +
    "commit $head.")
