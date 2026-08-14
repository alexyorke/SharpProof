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
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseChecksums.ps1')

function Get-SpdxPackageId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter()]
        [string]$Version
    )

    $suffix = if ([string]::IsNullOrWhiteSpace($Version)) {
        $Name
    }
    else {
        "$Name-$Version"
    }
    return 'SPDXRef-Package-' + (
        $suffix -replace '[^A-Za-z0-9.-]', '-')
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseVersion = Get-SharpProofReleaseVersion `
    -RepositoryRoot $repositoryRoot
$resolvedSource = (Resolve-Path `
    -LiteralPath $PackageSource `
    -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedSource -PathType Container)) {
    throw "PackageSource is not a directory: $resolvedSource"
}
if ($ExpectedTag -notmatch '^v(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)$') {
    throw "Release tag must be v<SemVer>: $ExpectedTag"
}
$expectedVersion = $Matches['version']
Test-SharpProofReleaseVersion `
    -ExpectedVersion $releaseVersion `
    -ActualVersion $expectedVersion `
    -Owner 'Release tag'
$manifestPath = Join-Path $resolvedSource 'SharpProof.release.json'
$sumsPath = Join-Path $resolvedSource 'SHA256SUMS'
$manifest = Get-Content -LiteralPath $manifestPath -Raw |
    ConvertFrom-Json
if ($manifest.schemaVersion -ne 2 -or
    [string]$manifest.hashAlgorithm -ne 'SHA256') {
    throw 'Unsupported release evidence schema or hash algorithm.'
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
if ($artifacts.Count -ne 7 -or
    @($artifacts | Where-Object { $_.kind -eq 'package' }).Count -ne 3 -or
    @($artifacts | Where-Object { $_.kind -eq 'symbols' }).Count -ne 3 -or
    @($artifacts | Where-Object { $_.kind -eq 'sbom' }).Count -ne 1) {
    throw 'Release evidence must contain three packages, three symbol packages, and one SBOM.'
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
            $_.Extension -in @('.nupkg', '.snupkg') -or
            $_.Name -eq 'SharpProof.spdx.json'
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
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).
        Hash.ToLowerInvariant()
    if ($hash -ne [string]$artifact.sha256 -or
        [int64]$file.Length -ne [int64]$artifact.bytes) {
        throw "Release artifact hash or size mismatch: $($artifact.fileName)"
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
$dependencyGraph = @(Get-SharpProofPackageDependencyGraph `
    -PackagePaths @(
        $artifacts |
            Where-Object { [string]$_.kind -in @('package', 'symbols') } |
            ForEach-Object {
                Join-Path $resolvedSource ([string]$_.fileName)
            }
    ))
$licenseGraph = @(Get-SharpProofPackageLicenseGraph `
    -PackagePaths @(
        $artifacts |
            Where-Object { [string]$_.kind -in @('package', 'symbols') } |
            ForEach-Object {
                Join-Path $resolvedSource ([string]$_.fileName)
            }
    ))
$sbomLicenseGraph = @(Get-SharpProofSbomLicenseGraph `
    -PackageLicenseGraph $licenseGraph `
    -PackageVersion $expectedVersion `
    -ThirdPartyComponents $catalogComponents)
$sbomArtifact = @(
    $artifacts |
        Where-Object { [string]$_.kind -eq 'sbom' }
)
$sbomPath = Join-Path $resolvedSource ([string]$sbomArtifact[0].fileName)
$sbom = Get-Content -LiteralPath $sbomPath -Raw |
    ConvertFrom-Json
if ($null -eq $sbom.PSObject.Properties['spdxVersion'] -or
    [string]$sbom.spdxVersion -ne 'SPDX-2.3' -or
    $null -eq $sbom.PSObject.Properties['dataLicense'] -or
    [string]$sbom.dataLicense -ne 'CC0-1.0' -or
    $null -eq $sbom.PSObject.Properties['packages'] -or
    $null -eq $sbom.PSObject.Properties['documentDescribes'] -or
    $null -eq $sbom.PSObject.Properties['relationships']) {
    throw 'Release SBOM is not a complete supported SPDX 2.3 document.'
}
$sbomPackages = @($sbom.packages)
$documentDescribes = @($sbom.documentDescribes)
$relationships = @($sbom.relationships)
Test-SharpProofSbomTopology `
    -SbomPackages $sbomPackages `
    -DocumentDescribes $documentDescribes `
    -Relationships $relationships `
    -FirstPartyPackageIds $expectedPackageIds `
    -PackageVersion $expectedVersion `
    -Components $catalogComponents `
    -DependencyGraph $dependencyGraph
Test-SharpProofSbomArtifactScope `
    -Artifacts $artifacts `
    -SbomPackages $sbomPackages `
    -DocumentDescribes $documentDescribes `
    -FirstPartyPackageIds $expectedPackageIds `
    -PackageVersion $expectedVersion
Test-SharpProofSbomAttestationWorkflow -Workflow (
    Get-Content -LiteralPath (Join-Path `
        $repositoryRoot '.github/workflows/package-consumers.yml') -Raw)
$componentKeys = @(
    $catalogComponents |
        ForEach-Object {
            [string]$_.id + "`0" + [string]$_.version
        } |
        Sort-Object -Unique
)
if ($sbomPackages.Count -ne
    ($expectedPackageIds.Count + $componentKeys.Count) -or
    $documentDescribes.Count -ne $expectedPackageIds.Count) {
    throw 'Release SBOM does not contain the exact package/component graph.'
}
foreach ($packageId in $expectedPackageIds) {
    $matches = @(
        $sbomPackages |
            Where-Object {
                [string]$_.name -eq $packageId -and
                [string]$_.versionInfo -eq $expectedVersion
            }
    )
    if ($matches.Count -ne 1) {
        throw "Release SBOM package identity is invalid: $packageId"
    }
    $spdxId = [string]$matches[0].SPDXID
    if (@($documentDescribes |
            Where-Object { [string]$_ -eq $spdxId }).Count -ne 1) {
        throw "Release SBOM does not describe '$packageId'."
    }
    $expectedHash = [string]@(
        $artifacts |
            Where-Object {
                [string]$_.kind -eq 'package' -and
                [string]$_.packageId -eq $packageId
            }
    )[0].sha256
    Test-SharpProofSpdxPackageChecksum `
        -Package $matches[0] `
        -ExpectedSha256 $expectedHash `
        -Identity $packageId
}
foreach ($key in $componentKeys) {
    $parts = $key.Split("`0")
    if (@($sbomPackages |
            Where-Object {
                [string]$_.name -eq $parts[0] -and
                [string]$_.versionInfo -eq $parts[1]
            }).Count -ne 1) {
        throw "Release SBOM component identity is invalid: $($parts[0])"
    }
}
foreach ($component in $catalogComponents) {
    $containerId = Get-SpdxPackageId -Name ([string]$component.packageId)
    $componentId = Get-SpdxPackageId `
        -Name ([string]$component.id) `
        -Version ([string]$component.version)
    if (@($relationships |
            Where-Object {
                [string]$_.spdxElementId -eq $containerId -and
                [string]$_.relationshipType -eq 'CONTAINS' -and
                [string]$_.relatedSpdxElement -eq $componentId
            }).Count -ne 1) {
        throw (
            "Release SBOM containment is invalid: " +
            "$($component.packageId)/$($component.id)")
    }
}
Test-SharpProofSbomDependencyGraph `
    -Relationships $relationships `
    -DependencyGraph $dependencyGraph
Test-SharpProofSbomComponentGraph `
    -SbomPackages $sbomPackages `
    -Relationships $relationships `
    -Components $catalogComponents
Test-SharpProofSbomLicenseGraph `
    -SbomPackages $sbomPackages `
    -LicenseGraph $sbomLicenseGraph
Test-SharpProofReleaseChecksumFile `
    -Path $sumsPath `
    -Artifacts $artifacts `
    -Owner 'SHA256SUMS'
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
