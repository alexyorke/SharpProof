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
Test-SharpProofReleaseBundleTopology `
    -Directory $resolvedSource `
    -Artifacts $artifacts `
    -Owner 'Release artifact bundle'
$expectedPackageIds = @($SharpProofPackageIds)
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
foreach ($artifact in $artifacts) {
    $fileName = [string]$artifact.fileName
    $kind = [string]$artifact.kind
    $expectedExtension = if ($kind -ceq 'package') { '.nupkg' } else { '.snupkg' }
    if (-not $fileName.EndsWith(
            $expectedExtension,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release artifact extension is invalid: $fileName"
    }
    $path = Join-Path $resolvedSource $fileName
    $file = Get-Item -LiteralPath $path -ErrorAction Stop
    if ([int64]$file.Length -ne [int64]$artifact.bytes) {
        throw "Release artifact size mismatch: $fileName"
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
$artifactsByKindAndPackage =
    [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($artifact in $artifacts) {
    $key = '{0}|{1}' -f [string]$artifact.kind, [string]$artifact.packageId
    $artifactsByKindAndPackage.Add($key, $artifact)
}
$payloadsByPackage = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
foreach ($payloadSet in $payloadSets) {
    $payloadsByPackage.Add([string]$payloadSet.packageId, $payloadSet)
}
$componentsByPackage =
    [Collections.Generic.Dictionary[string, Collections.Generic.List[object]]]::new(
        [StringComparer]::Ordinal)
foreach ($component in $catalogComponents) {
    $packageId = [string]$component.packageId
    if (-not $componentsByPackage.ContainsKey($packageId)) {
        $componentsByPackage.Add($packageId, [Collections.Generic.List[object]]::new())
    }
    [void]$componentsByPackage[$packageId].Add($component)
}
$payloadValidationCache = @{}
foreach ($packageId in $expectedPackageIds) {
    $mainArtifact = $artifactsByKindAndPackage["package|$packageId"]
    $symbolArtifact = $artifactsByKindAndPackage["symbols|$packageId"]
    $components = @(
        if ($componentsByPackage.ContainsKey($packageId)) {
            $componentsByPackage[$packageId].ToArray()
        }
    )
    $payloadEntries = @($payloadsByPackage[$packageId].entries)
    $null = Test-SharpProofPackagePayload `
        -PackagePath (Join-Path `
            $resolvedSource `
            ([string]$mainArtifact.fileName)) `
        -PackageId $packageId `
        -RepositoryRoot $repositoryRoot `
        -Components $components `
        -ExpectedPayloads $payloadEntries `
        -ValidationCache $payloadValidationCache
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
