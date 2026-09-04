[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [Parameter()]
    [string]$OutputDirectory,

    [Parameter()]
    [string]$ThirdPartyManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedOutput = $null
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseBundle.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofSymbolPackages.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackagePayloads.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackageDependencies.ps1')
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function Get-ArchiveText {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Package archive entry is missing: $EntryName"
    }
    $reader = [IO.StreamReader]::new(
        $entry.Open(),
        [Text.UTF8Encoding]::new($false, $true))
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Test-ThirdPartyComponentVersions {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,

        [Parameter(Mandatory = $true)]
        [object]$Manifest
    )

    $assetRoutes = @{
        'SharpProof' = @(
            @{
                EntryPrefix = 'tools/collector/'
                AssetsPath =
                    'SharpProof.CompilerCollector\obj\project.assets.json'
            },
            @{
                EntryPrefix = 'tools/shared/netstandard2.0/'
                AssetsPath =
                    'SharpProof.CompilerCollector\obj\project.assets.json'
            }
        )
        'SharpProof.Verifier' = @(
            @{
                EntryPrefix = 'tools/net9/'
                AssetsPath = 'SharpProof.Worker\obj\project.assets.json'
            },
            @{
                EntryPrefix = 'tools/native/linux-x64/'
                AssetsPath = 'SharpProof.Worker\obj\project.assets.json'
            }
        )
    }
    $assetLibraries = @{}
    foreach ($packageId in $assetRoutes.Keys) {
        foreach ($component in @(
                $Manifest.packages.PSObject.Properties[$packageId].Value)) {
            $id = [string]$component.id
            $version = [string]$component.version
            $componentAssetPaths = @(
                @(
                    foreach ($entry in @($component.entries)) {
                        $entryText = [string]$entry
                        $matchingRoutes = @(
                            $assetRoutes[$packageId] |
                                Where-Object {
                                    $entryText.StartsWith(
                                        [string]$_.EntryPrefix,
                                        [StringComparison]::Ordinal)
                                }
                        )
                        if ($matchingRoutes.Count -ne 1) {
                            throw (
                                "Third-party component entry '$entryText' " +
                                "for '$packageId' does not map to exactly " +
                                'one restored-assets owner.')
                        }
                        [string]$matchingRoutes[0].AssetsPath
                    }
                ) | Sort-Object -Unique
            )
            if ($componentAssetPaths.Count -ne 1) {
                throw (
                    "Third-party component '$id $version' for '$packageId' " +
                    'spans multiple restored-assets owners: ' +
                    ($componentAssetPaths -join ', '))
            }
            $relativeAssetsPath = $componentAssetPaths[0]
            $assetsPath = Join-Path $RepositoryRoot $relativeAssetsPath
            if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
                throw (
                    "Restored assets are missing for '$packageId' entry " +
                    "owner '$relativeAssetsPath': $assetsPath")
            }
            if (-not $assetLibraries.ContainsKey($assetsPath)) {
                $assets = Get-Content -LiteralPath $assetsPath -Raw |
                    ConvertFrom-Json -AsHashtable
                $assetLibraries[$assetsPath] = @($assets.libraries.Keys)
            }
            $libraryKeys = @($assetLibraries[$assetsPath])
            $matchingVersions = @(
                $libraryKeys |
                    Where-Object {
                        $_.StartsWith(
                            "$id/",
                            [StringComparison]::OrdinalIgnoreCase)
                    }
            )
            if ($matchingVersions.Count -ne 1 -or
                -not [string]::Equals(
                    $matchingVersions[0],
                    "$id/$version",
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw (
                    "Third-party component '$id $version' for '$packageId' " +
                    "does not match restored assets: " +
                    ($matchingVersions -join ', '))
            }
        }
    }
}

function Test-PackageThirdPartyInventory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Components,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$NoticeComponents,

        [Parameter()]
        [AllowNull()]
        [IO.Compression.ZipArchive]$Archive
    )

    $ownsArchive = $null -eq $Archive
    if ($ownsArchive) {
        $Archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    }
    try {
        $actualThirdPartyEntries = @(
            $Archive.Entries |
                Where-Object {
                    ($_.FullName.EndsWith(
                            '.dll',
                            [StringComparison]::OrdinalIgnoreCase) -and
                        -not [IO.Path]::GetFileName($_.FullName).StartsWith(
                            'SharpProof.',
                            [StringComparison]::Ordinal)) -or
                    $_.FullName.EndsWith(
                        '.so',
                        [StringComparison]::OrdinalIgnoreCase)
                } |
                ForEach-Object { $_.FullName } |
                Sort-Object -Unique
        )
        $declaredEntries = @(
            $Components |
                ForEach-Object { @($_.entries) } |
                ForEach-Object { [string]$_ } |
                Sort-Object -Unique
        )
        if (($actualThirdPartyEntries -join '|') -ne
            ($declaredEntries -join '|')) {
            throw "Third-party inventory for '$PackageId' does not match " +
                "the package payload. Actual: " +
                ($actualThirdPartyEntries -join ', ') +
                ". Declared: " + ($declaredEntries -join ', ') + "."
        }
        if ($Components.Count -eq 0) {
            return
        }
        $notice = Get-ArchiveText `
            -Archive $Archive `
            -EntryName 'THIRD-PARTY-NOTICES.txt'
        $actualNoticePackages = @(
            [regex]::Matches(
                $notice,
                '(?m)^Package:\s*(?<id>\S+)\s+(?<version>\S+)\s*$') |
                ForEach-Object {
                    $_.Groups['id'].Value + ' ' +
                        $_.Groups['version'].Value
                } |
                Sort-Object -Unique
        )
        $declaredNoticePackages = @(
            $NoticeComponents |
                ForEach-Object {
                    [string]$_.id + ' ' + [string]$_.version
                } |
                Sort-Object -Unique
        )
        if (($actualNoticePackages -join '|') -ne
            ($declaredNoticePackages -join '|')) {
            throw "Third-party notice for '$PackageId' does not match " +
                'the declared component set. Actual: ' +
                ($actualNoticePackages -join ', ') +
                '. Declared: ' +
                ($declaredNoticePackages -join ', ') + '.'
        }
        foreach ($component in $Components) {
            $id = [string]$component.id
            $version = [string]$component.version
            $license = [string]$component.license
            if ([string]::IsNullOrWhiteSpace($id) -or
                [string]::IsNullOrWhiteSpace($version) -or
                $license -ne 'MIT' -or
                @($component.entries).Count -eq 0) {
                throw "Third-party component metadata for '$PackageId' is invalid."
            }
            $needle = "Package: $id $version"
            if (-not $notice.Contains(
                    $needle,
                    [StringComparison]::Ordinal)) {
                throw "Third-party notice for '$PackageId' is missing '$needle'."
            }
        }
    }
    finally {
        if ($ownsArchive) {
            $Archive.Dispose()
        }
    }
}

$resolvedSource = (Resolve-Path `
    -LiteralPath $PackageSource `
    -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedSource -PathType Container)) {
    throw "PackageSource is not a directory: $resolvedSource"
}
$repositoryRoot = (Resolve-Path `
    -LiteralPath (Join-Path $PSScriptRoot '..') `
    -ErrorAction Stop).Path
$releaseVersion = Get-SharpProofReleaseVersion `
    -RepositoryRoot $repositoryRoot
if ([string]::IsNullOrWhiteSpace($ThirdPartyManifestPath)) {
    $resolvedThirdPartyManifest = Join-Path `
        $repositoryRoot `
        'eng\release\third-party-components.json'
}
else {
    $resolvedThirdPartyManifest = (Resolve-Path `
        -LiteralPath $ThirdPartyManifestPath `
        -ErrorAction Stop).Path
}
$thirdPartyManifest = Get-Content `
    -LiteralPath $resolvedThirdPartyManifest `
    -Raw |
    ConvertFrom-Json
if ($thirdPartyManifest.schemaVersion -ne 1) {
    throw 'Unsupported third-party component manifest schema.'
}
Test-ThirdPartyComponentVersions `
    -RepositoryRoot $repositoryRoot `
    -Manifest $thirdPartyManifest
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $finalOutput = $resolvedSource
}
else {
    $outputPath = [IO.Path]::GetFullPath($OutputDirectory)
    if (-not (Test-Path -LiteralPath $outputPath)) {
        New-Item -ItemType Directory -Path $outputPath |
            Out-Null
    }
    $finalOutput = (Resolve-Path `
        -LiteralPath $outputPath `
        -ErrorAction Stop).Path
    if (-not (Test-Path `
            -LiteralPath $finalOutput `
            -PathType Container)) {
        throw "OutputDirectory is not a directory: $finalOutput"
    }
}
$resolvedOutput = Join-Path ([IO.Path]::GetDirectoryName($finalOutput)) (
    '.' + [IO.Path]::GetFileName($finalOutput) + '.' +
    [Guid]::NewGuid().ToString('N') + '.staging')
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
trap {
    if (-not [string]::IsNullOrWhiteSpace($resolvedOutput) -and
        [IO.Directory]::Exists($resolvedOutput)) {
        [IO.Directory]::Delete($resolvedOutput, $true)
    }
    throw $PSItem
}

$packageFiles = @(
    Get-ChildItem -LiteralPath $resolvedSource -File |
        Where-Object {
            $_.Extension -in @('.nupkg', '.snupkg')
        } |
        Sort-Object Name
)
if ($packageFiles.Count -ne 6) {
    throw "Release evidence requires exactly six NuGet artifacts; found $($packageFiles.Count)."
}
Test-SharpProofExactRegularFileSet `
    -Directory $resolvedSource `
    -ExpectedFileNames @($packageFiles.Name) `
    -Owner 'Release package input'

$expectedIds = $SharpProofPackageIds
$identities = @(
    $packageFiles |
        ForEach-Object {
            [pscustomobject][ordered]@{
                File = $_
                Identity = Get-SharpProofPackageIdentity `
                    -Path $_.FullName -RequireRepository
            }
        }
)
foreach ($extension in @('.nupkg', '.snupkg')) {
    $actualIds = @(
        $identities |
            Where-Object { $_.File.Extension -eq $extension } |
            ForEach-Object { $_.Identity.Id } |
            Sort-Object
    )
    if (($actualIds -join '|') -ne ($expectedIds -join '|')) {
        throw "Release evidence requires one $extension for each package ID; found '$($actualIds -join ', ')'."
    }
}

$versions = @($identities | ForEach-Object { $_.Identity.Version })
Test-SharpProofReleaseVersionSet `
    -ExpectedVersion $releaseVersion `
    -Versions $versions `
    -Owner 'NuGet artifacts'
$commits = @(
    $identities |
        ForEach-Object { $_.Identity.RepositoryCommit } |
        Sort-Object -Unique
)
if ($commits.Count -ne 1) {
    throw "NuGet artifact repository commits must match; found '$($commits -join ', ')'."
}
$checkoutCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $checkoutCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not resolve the release checkout commit.'
}
if ($commits[0] -ne $checkoutCommit) {
    throw (
        "NuGet artifact repository commit '$($commits[0])' does not match " +
        "checkout '$checkoutCommit'.")
}
foreach ($packageId in $expectedIds) {
    $main = @(
        $identities |
            Where-Object {
                $_.File.Extension -eq '.nupkg' -and
                $_.Identity.Id -eq $packageId
            }
    )[0]
    $symbols = @(
        $identities |
            Where-Object {
                $_.File.Extension -eq '.snupkg' -and
                $_.Identity.Id -eq $packageId
            }
    )[0]
    try {
        Test-SharpProofSymbolPackagePair `
            -PackagePath $main.File.FullName `
            -SymbolPackagePath $symbols.File.FullName `
            -PackageId $packageId `
            -PackageVersion $versions[0] `
            -RepositoryCommit $checkoutCommit
    }
    catch {
        throw "Package payload validation failed: $($_.Exception.Message)"
    }
}

$thirdPartyPackages = @(
    $thirdPartyManifest.packages.PSObject.Properties |
        ForEach-Object { $_.Name } |
        Sort-Object
)
if (($thirdPartyPackages -join '|') -ne ($expectedIds -join '|')) {
    throw 'Third-party component manifest must cover the exact package graph.'
}
$packagePayloadEvidence = [Collections.Generic.List[object]]::new()
$thirdPartyComponents = [Collections.Generic.List[object]]::new()
$thirdPartyNoticeComponents = @(
    $thirdPartyManifest.packages.PSObject.Properties |
        ForEach-Object { @($_.Value) }
)
$payloadValidationCache = @{}
foreach ($item in $identities |
        Where-Object { $_.File.Extension -eq '.nupkg' }) {
    $packageId = $item.Identity.Id
    $components = @(
        $thirdPartyManifest.packages.PSObject.Properties[$packageId].Value
    )
    $archive = [IO.Compression.ZipFile]::OpenRead($item.File.FullName)
    try {
        $payloads = @(Test-SharpProofPackagePayload `
            -PackagePath $item.File.FullName `
            -PackageId $packageId `
            -RepositoryRoot $repositoryRoot `
            -Components $components `
            -ValidationCache $payloadValidationCache `
            -Archive $archive)
        $packagePayloadEvidence.Add([pscustomobject][ordered]@{
            packageId = $packageId
            entries = $payloads
        })
        Test-PackageThirdPartyInventory `
            -PackagePath $item.File.FullName `
            -PackageId $packageId `
            -Components $components `
            -NoticeComponents $thirdPartyNoticeComponents `
            -Archive $archive
        foreach ($component in $components) {
            $thirdPartyComponents.Add([pscustomobject][ordered]@{
                packageId = $packageId
                id = [string]$component.id
                version = [string]$component.version
                license = [string]$component.license
                entries = @(
                    @($component.entries) |
                        ForEach-Object { [string]$_ } |
                        Sort-Object
                )
            })
        }
    }
    finally {
        $archive.Dispose()
    }
}
$catalogComponents = @(Get-SharpProofThirdPartyComponentGraph `
    -ContractPath $resolvedThirdPartyManifest)
Test-SharpProofThirdPartyComponentProjection `
    -ActualComponents @($thirdPartyComponents) `
    -ExpectedComponents $catalogComponents
$artifacts = [Collections.Generic.List[object]]::new()
foreach ($item in $identities) {
    $artifacts.Add([pscustomobject][ordered]@{
        fileName = $item.File.Name
        kind = if ($item.File.Extension -eq '.snupkg') {
            'symbols'
        }
        else {
            'package'
        }
        packageId = $item.Identity.Id
        bytes = [int64]$item.File.Length
    })
}

$artifactsByName = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
foreach ($artifact in $artifacts) {
    if (-not $artifactsByName.TryAdd(
            [string]$artifact.fileName,
            $artifact)) {
        throw "Release artifacts contain duplicate file name '$($artifact.fileName)'."
    }
}
$artifactNames = [string[]]@($artifactsByName.Keys)
[Array]::Sort($artifactNames, [StringComparer]::Ordinal)
$orderedArtifacts = @(
    $artifactNames | ForEach-Object { $artifactsByName[$_] }
)
$manifest = [pscustomobject][ordered]@{
    schemaVersion = 2
    packageVersion = $releaseVersion
    versionAuthority = Get-SharpProofReleaseVersionAuthority `
        -RepositoryRoot $repositoryRoot
    repository = [pscustomobject][ordered]@{
        type = 'git'
        url = 'https://github.com/alexyorke/SharpProof'
        commit = $commits[0]
    }
    artifacts = $orderedArtifacts
    packagePayloads = @(
        $packagePayloadEvidence |
            Sort-Object packageId
    )
    thirdPartyComponents = @(
        $thirdPartyComponents |
            Sort-Object packageId, id, version
    )
}
$json = ($manifest | ConvertTo-Json -Depth 8) -replace "`r`n", "`n"
$json += "`n"
$manifestPath = Join-Path $resolvedOutput 'SharpProof.release.json'
Write-SharpProofAtomicText -Path $manifestPath -Value $json
$null = Read-SharpProofCanonicalReleaseJson `
    -Path $manifestPath `
    -DocumentType ReleaseManifest
foreach ($packageFile in $packageFiles) {
    [IO.File]::Copy(
        $packageFile.FullName,
        (Join-Path $resolvedOutput $packageFile.Name),
        $false)
}
Test-SharpProofReleaseBundleTopology `
    -Directory $resolvedOutput `
    -Artifacts $orderedArtifacts `
    -Owner 'Generated release bundle staging'
Publish-SharpProofReleaseBundleAtomically `
    -StagingDirectory $resolvedOutput `
    -DestinationDirectory $finalOutput `
    -Artifacts $orderedArtifacts `
    -Owner 'Generated release bundle'
$manifestPath = Join-Path $finalOutput 'SharpProof.release.json'

Write-Host "Wrote deterministic SharpProof release evidence for version $($versions[0])."
[pscustomobject][ordered]@{
    ManifestPath = $manifestPath
    PackageVersion = $versions[0]
    RepositoryCommit = $commits[0]
    ArtifactCount = $orderedArtifacts.Count
}
