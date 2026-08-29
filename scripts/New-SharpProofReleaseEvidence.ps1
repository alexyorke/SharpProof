[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [Parameter()]
    [string]$SbomPath,

    [Parameter()]
    [string]$OutputDirectory,

    [Parameter()]
    [string]$ThirdPartyManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedOutput = $null
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseChecksums.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofSymbolPackages.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackagePayloads.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackageDependencies.ps1')
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $nuspecEntries = @(
            $archive.Entries |
                Where-Object {
                    $_.FullName.EndsWith(
                        '.nuspec',
                        [StringComparison]::OrdinalIgnoreCase)
                }
        )
        if ($nuspecEntries.Count -ne 1) {
            throw "Package '$Path' must contain exactly one nuspec."
        }
        $reader = [IO.StreamReader]::new($nuspecEntries[0].Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        $namespaces = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
        $namespaces.AddNamespace(
            'n',
            $nuspec.DocumentElement.NamespaceURI)
        $metadata = $nuspec.SelectSingleNode(
            '/n:package/n:metadata',
            $namespaces)
        if ($null -eq $metadata) {
            throw "Package '$Path' has no nuspec metadata."
        }
        $id = $metadata.SelectSingleNode('n:id', $namespaces)
        $version = $metadata.SelectSingleNode('n:version', $namespaces)
        $repository = $metadata.SelectSingleNode(
            'n:repository',
            $namespaces)
        if ($null -eq $id -or
            $null -eq $version -or
            $null -eq $repository) {
            throw "Package '$Path' has incomplete release metadata."
        }
        $repositoryType = $repository.GetAttribute('type')
        $repositoryUrl = $repository.GetAttribute('url')
        $repositoryCommit = $repository.GetAttribute('commit')
        if ($repositoryType -ne 'git' -or
            $repositoryUrl -ne
                'https://github.com/alexyorke/SharpProof' -or
            $repositoryCommit -notmatch '^[0-9a-fA-F]{40}$') {
            throw "Package '$Path' has invalid repository metadata."
        }
        return [pscustomobject][ordered]@{
            Id = $id.InnerText
            Version = $version.InnerText
            RepositoryUrl = $repositoryUrl
            RepositoryCommit = $repositoryCommit.ToLowerInvariant()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Write-AtomicText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $directory = [IO.Path]::GetDirectoryName($Path)
    $leaf = [IO.Path]::GetFileName($Path)
    $temporaryPath = Join-Path `
        $directory `
        ('.' + $leaf + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText(
            $temporaryPath,
            $Value,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

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

function New-DeterministicPackageSbom {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object[]]$PackageItems,

        [Parameter(Mandatory = $true)]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryCommit,

        [Parameter(Mandatory = $true)]
        [object[]]$ThirdPartyComponents,

        [Parameter(Mandatory = $true)]
        [object[]]$DependencyGraph,

        [Parameter(Mandatory = $true)]
        [object[]]$LicenseGraph,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $releaseIdentity = Get-SharpProofSbomReleaseIdentity `
        -RepositoryRoot $RepositoryRoot `
        -Version $Version `
        -RepositoryCommit $RepositoryCommit

    $packages = [Collections.Generic.List[object]]::new()
    $relationships = [Collections.Generic.List[object]]::new()
    $described = [Collections.Generic.List[string]]::new()
    foreach ($item in $PackageItems |
            Sort-Object { $_.Identity.Id }) {
        $id = [string]$item.Identity.Id
        $spdxId = Get-SpdxPackageId -Name $id
        $described.Add($spdxId)
        $hash = (Get-FileHash `
            -LiteralPath $item.File.FullName `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        $license = @($LicenseGraph | Where-Object {
            [string]$_.PackageId -eq $id
        })
        if ($license.Count -ne 1) {
            throw "Package license authority is missing '$id'."
        }
        $packages.Add([pscustomobject][ordered]@{
            name = $id
            SPDXID = $spdxId
            versionInfo = $Version
            downloadLocation = 'NOASSERTION'
            filesAnalyzed = $false
            checksums = @(
                [pscustomobject][ordered]@{
                    algorithm = 'SHA256'
                    checksumValue = $hash
                }
            )
            licenseConcluded = [string]$license[0].LicenseExpression
            licenseDeclared = [string]$license[0].LicenseExpression
            copyrightText = 'NOASSERTION'
            externalRefs = @(
                [pscustomobject][ordered]@{
                    referenceCategory = 'PACKAGE-MANAGER'
                    referenceType = 'purl'
                    referenceLocator = Get-SharpProofNuGetPurl `
                        -Name $id `
                        -Version $Version
                }
            )
        })
        $relationships.Add([pscustomobject][ordered]@{
            spdxElementId = 'SPDXRef-DOCUMENT'
            relationshipType = 'DESCRIBES'
            relatedSpdxElement = $spdxId
        })
    }

    $componentIds = @{}
    foreach ($component in $ThirdPartyComponents |
            Sort-Object id, version) {
        $componentName = [string]$component.id
        $componentVersion = [string]$component.version
        $key = "$componentName`0$componentVersion"
        if (-not $componentIds.ContainsKey($key)) {
            $componentSpdxId = Get-SpdxPackageId `
                -Name $componentName `
                -Version $componentVersion
            $componentIds[$key] = $componentSpdxId
            $packages.Add([pscustomobject][ordered]@{
                name = $componentName
                SPDXID = $componentSpdxId
                versionInfo = $componentVersion
                downloadLocation = 'NOASSERTION'
                filesAnalyzed = $false
                licenseConcluded = [string]$component.license
                licenseDeclared = [string]$component.license
                copyrightText = 'NOASSERTION'
                externalRefs = @(
                    [pscustomobject][ordered]@{
                        referenceCategory = 'PACKAGE-MANAGER'
                        referenceType = 'purl'
                        referenceLocator = Get-SharpProofNuGetPurl `
                            -Name $componentName `
                            -Version $componentVersion
                    }
                )
            })
        }
        $relationships.Add([pscustomobject][ordered]@{
            spdxElementId = Get-SpdxPackageId `
                -Name ([string]$component.packageId)
            relationshipType = 'CONTAINS'
            relatedSpdxElement = [string]$componentIds[$key]
        })
    }
    foreach ($dependency in $DependencyGraph) {
        $relationships.Add([pscustomobject][ordered]@{
            spdxElementId = Get-SpdxPackageId -Name $dependency.FromId
            relationshipType = 'DEPENDS_ON'
            relatedSpdxElement = Get-SpdxPackageId -Name $dependency.ToId
        })
    }

    $document = [pscustomobject][ordered]@{
        spdxVersion = 'SPDX-2.3'
        dataLicense = 'CC0-1.0'
        SPDXID = 'SPDXRef-DOCUMENT'
        name = [string]$releaseIdentity.Name
        documentNamespace = [string]$releaseIdentity.DocumentNamespace
        creationInfo = [pscustomobject][ordered]@{
            created = [string]$releaseIdentity.Created
            creators = @($releaseIdentity.Creators)
            comment = [string]$releaseIdentity.Comment
        }
        documentDescribes = @($described)
        packages = @($packages |
            Sort-Object name, versionInfo)
        relationships = @($relationships |
            Sort-Object spdxElementId, relationshipType, relatedSpdxElement)
    }
    Test-SharpProofSbomReleaseIdentity `
        -Sbom $document `
        -RepositoryRoot $RepositoryRoot `
        -Version $Version `
        -RepositoryCommit $RepositoryCommit
    foreach ($item in $PackageItems) {
        $id = [string]$item.Identity.Id
        $matches = @($document.packages | Where-Object {
            [string]$_.name -ceq $id -and
            [string]$_.versionInfo -ceq $Version
        })
        if ($matches.Count -ne 1) {
            throw "Generated SPDX package identity is invalid: $id"
        }
        $expectedHash = (Get-FileHash `
            -LiteralPath $item.File.FullName `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        Test-SharpProofSpdxPackageChecksum `
            -Package $matches[0] `
            -ExpectedSha256 $expectedHash `
            -Identity $id
    }
    $json = ($document | ConvertTo-Json -Depth 10) -replace "`r`n", "`n"
    Write-AtomicText -Path $Path -Value ($json + "`n")
    $null = Read-SharpProofCanonicalReleaseJson `
        -Path $Path `
        -DocumentType Spdx
}

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
        [object[]]$Components
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $actualThirdPartyEntries = @(
            $archive.Entries |
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
            -Archive $archive `
            -EntryName 'THIRD-PARTY-NOTICES.txt'
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
        $archive.Dispose()
    }
}

$null = Restore-SharpProofReleaseBundleBackup `
    -DestinationDirectory ([IO.Path]::GetFullPath($PackageSource)) `
    -Owner 'Release package source recovery'
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
$inPlace = [IO.Path]::GetFullPath($resolvedSource) -ceq
    [IO.Path]::GetFullPath($finalOutput)
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
$sourcePackageFiles = $packageFiles
$expectedPackageNames = @($packageFiles.Name)
if ($inPlace) {
    $ownedEvidenceNames = @(
        'SharpProof.release.json',
        'SharpProof.spdx.json',
        'SHA256SUMS')
    $sourceNames = @(Get-ChildItem -LiteralPath $resolvedSource -Force |
        ForEach-Object { $_.Name })
    $hasCompletePriorBundle =
        $sourceNames.Count -eq ($expectedPackageNames.Count + $ownedEvidenceNames.Count) -and
        @($ownedEvidenceNames | Where-Object { $sourceNames -notcontains $_ }).Count -eq 0
    Test-SharpProofReleasePackageInput `
        -Directory $resolvedSource `
        -PackageNames $expectedPackageNames `
        -AllowGeneratedEvidence:$hasCompletePriorBundle
}
else {
    Test-SharpProofReleasePackageInput `
        -Directory $resolvedSource `
        -PackageNames $expectedPackageNames
}

foreach ($sourcePackageFile in $packageFiles) {
    $stagedPackagePath = Join-Path $resolvedOutput $sourcePackageFile.Name
    [IO.File]::Copy(
        $sourcePackageFile.FullName,
        $stagedPackagePath,
        $false)
    Convert-SharpProofPackageArchive -Path $stagedPackagePath
}
$packageFiles = @($sourcePackageFiles | ForEach-Object {
    Get-Item -LiteralPath (Join-Path $resolvedOutput $_.Name)
})

$expectedIds = @(
    'SharpProof',
    'SharpProof.Attributes',
    'SharpProof.Verifier'
) | Sort-Object
$identities = @(
    $packageFiles |
        ForEach-Object {
            [pscustomobject][ordered]@{
                File = $_
                Identity = Get-PackageIdentity -Path $_.FullName
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

$versions = @(
    $identities |
        ForEach-Object { $_.Identity.Version } |
        Sort-Object -Unique
)
if ($versions.Count -ne 1) {
    throw "NuGet artifact versions must match; found '$($versions -join ', ')'."
}
Test-SharpProofReleaseVersionSet `
    -ExpectedVersion $releaseVersion `
    -Versions @($identities | ForEach-Object { $_.Identity.Version }) `
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
$dependencyGraph = @(Get-SharpProofPackageDependencyGraph `
    -PackagePaths @($identities.File.FullName))
$licenseGraph = @(Get-SharpProofPackageLicenseGraph `
    -PackagePaths @($identities.File.FullName))

$packagePayloadEvidence = [Collections.Generic.List[object]]::new()
foreach ($item in $identities |
        Where-Object { $_.File.Extension -eq '.nupkg' }) {
    $packageId = $item.Identity.Id
    $components = @(
        $thirdPartyManifest.packages.PSObject.Properties[$packageId].Value
    )
    $payloads = @(Test-SharpProofPackagePayload `
        -PackagePath $item.File.FullName `
        -PackageId $packageId `
        -RepositoryRoot $repositoryRoot `
        -Components $components)
    $packagePayloadEvidence.Add([pscustomobject][ordered]@{
        packageId = $packageId
        entries = $payloads
    })
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
    Test-SharpProofSymbolPackagePair `
        -PackagePath $main.File.FullName `
        -SymbolPackagePath $symbols.File.FullName `
        -PackageId $packageId `
        -PackageVersion $versions[0] `
        -RepositoryCommit $checkoutCommit
}

$thirdPartyPackages = @(
    $thirdPartyManifest.packages.PSObject.Properties |
        ForEach-Object { $_.Name } |
        Sort-Object
)
if (($thirdPartyPackages -join '|') -ne ($expectedIds -join '|')) {
    throw 'Third-party component manifest must cover the exact package graph.'
}
$thirdPartyComponents = [Collections.Generic.List[object]]::new()
foreach ($item in $identities |
        Where-Object { $_.File.Extension -eq '.nupkg' }) {
    $packageId = $item.Identity.Id
    $components = @(
        $thirdPartyManifest.packages.PSObject.Properties[$packageId].Value
    )
    Test-PackageThirdPartyInventory `
        -PackagePath $item.File.FullName `
        -PackageId $packageId `
        -Components $components
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
$catalogComponents = @(Get-SharpProofThirdPartyComponentGraph `
    -ContractPath $resolvedThirdPartyManifest)
Test-SharpProofThirdPartyComponentProjection `
    -ActualComponents @($thirdPartyComponents) `
    -ExpectedComponents $catalogComponents
$sbomLicenseGraph = @(Get-SharpProofSbomLicenseGraph `
    -PackageLicenseGraph $licenseGraph `
    -PackageVersion $versions[0] `
    -ThirdPartyComponents $catalogComponents)

$artifacts = [Collections.Generic.List[object]]::new()
foreach ($item in $identities) {
    $hash = Get-FileHash `
        -LiteralPath $item.File.FullName `
        -Algorithm SHA256
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
        sha256 = $hash.Hash.ToLowerInvariant()
    })
}

if ([string]::IsNullOrWhiteSpace($SbomPath)) {
    $resolvedSbom = Join-Path $resolvedOutput 'SharpProof.spdx.json'
    New-DeterministicPackageSbom `
        -Path $resolvedSbom `
        -PackageItems @($identities |
            Where-Object { $_.File.Extension -eq '.nupkg' }) `
        -Version $versions[0] `
        -RepositoryCommit $commits[0] `
        -ThirdPartyComponents @($thirdPartyComponents) `
        -DependencyGraph $dependencyGraph `
        -LicenseGraph $licenseGraph `
        -RepositoryRoot $repositoryRoot
}
else {
    $suppliedSbom = (Resolve-Path `
        -LiteralPath $SbomPath `
        -ErrorAction Stop).Path
    $resolvedSbom = Copy-SharpProofReleaseSbom `
        -SourcePath $suppliedSbom `
        -StagingDirectory $resolvedOutput
}
if (-not (Test-Path -LiteralPath $resolvedSbom -PathType Leaf)) {
    throw "SbomPath is not a file: $resolvedSbom"
}
$sbom = Read-SharpProofCanonicalReleaseJson `
    -Path $resolvedSbom `
    -DocumentType Spdx
if ($null -eq $sbom.PSObject.Properties['spdxVersion'] -or
    [string]$sbom.spdxVersion -ne 'SPDX-2.3' -or
    $null -eq $sbom.PSObject.Properties['dataLicense'] -or
    [string]$sbom.dataLicense -ne 'CC0-1.0') {
    throw "SbomPath is not a supported SPDX JSON document: $resolvedSbom"
}
Test-SharpProofSbomReleaseIdentity `
    -Sbom $sbom `
    -RepositoryRoot $repositoryRoot `
    -Version $versions[0] `
    -RepositoryCommit $commits[0]
if ($null -eq $sbom.PSObject.Properties['packages'] -or
    $null -eq $sbom.PSObject.Properties['documentDescribes'] -or
    $null -eq $sbom.PSObject.Properties['relationships']) {
    throw "SPDX SBOM is missing its package graph: $resolvedSbom"
}
$sbomPackages = @($sbom.packages)
$documentDescribes = @($sbom.documentDescribes)
$relationships = @($sbom.relationships)
Test-SharpProofSbomTopology `
    -SbomPackages $sbomPackages `
    -DocumentDescribes $documentDescribes `
    -Relationships $relationships `
    -FirstPartyPackageIds $expectedIds `
    -PackageVersion $versions[0] `
    -Components @($thirdPartyComponents) `
    -DependencyGraph $dependencyGraph
Test-SharpProofSbomArtifactScope `
    -Artifacts @($artifacts) `
    -SbomPackages $sbomPackages `
    -DocumentDescribes $documentDescribes `
    -FirstPartyPackageIds $expectedIds `
    -PackageVersion $versions[0]
Test-SharpProofSbomAttestationWorkflow -Workflow (
    Get-Content -LiteralPath (Join-Path `
        $repositoryRoot '.github/workflows/package-consumers.yml') -Raw)
$expectedComponentKeys = @(
    $thirdPartyComponents |
        ForEach-Object {
            [string]$_.id + "`0" + [string]$_.version
        } |
        Sort-Object -Unique
)
if ($sbomPackages.Count -ne
    ($expectedIds.Count + $expectedComponentKeys.Count) -or
    $documentDescribes.Count -ne $expectedIds.Count) {
    throw "SPDX SBOM does not contain the exact package graph: $resolvedSbom"
}
foreach ($expectedId in $expectedIds) {
    $matchingPackages = @(
        $sbomPackages |
            Where-Object {
                [string]$_.name -eq $expectedId -and
                [string]$_.versionInfo -eq $versions[0]
            }
    )
    if ($matchingPackages.Count -ne 1) {
        throw "SPDX SBOM must describe exactly one $expectedId " +
            "$($versions[0]) package."
    }
    $spdxId = [string]$matchingPackages[0].SPDXID
    if (@($documentDescribes |
            Where-Object { [string]$_ -eq $spdxId }).Count -ne 1) {
        throw "SPDX SBOM does not describe package '$expectedId'."
    }
    $packageItem = @(
        $identities |
            Where-Object {
                $_.File.Extension -eq '.nupkg' -and
                $_.Identity.Id -eq $expectedId
            }
    )
    $expectedHash = (Get-FileHash `
        -LiteralPath $packageItem[0].File.FullName `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    Test-SharpProofSpdxPackageChecksum `
        -Package $matchingPackages[0] `
        -ExpectedSha256 $expectedHash `
        -Identity $expectedId
}
foreach ($key in $expectedComponentKeys) {
    $parts = $key.Split("`0")
    $matchingComponents = @(
        $sbomPackages |
            Where-Object {
                [string]$_.name -eq $parts[0] -and
                [string]$_.versionInfo -eq $parts[1]
            }
    )
    if ($matchingComponents.Count -ne 1) {
        throw (
            "SPDX SBOM must contain exactly one component " +
            "'$($parts[0]) $($parts[1])'.")
    }
}
foreach ($component in $thirdPartyComponents) {
    $containerId = Get-SpdxPackageId -Name ([string]$component.packageId)
    $componentId = Get-SpdxPackageId `
        -Name ([string]$component.id) `
        -Version ([string]$component.version)
    $matchingRelationships = @(
        $relationships |
            Where-Object {
                [string]$_.spdxElementId -eq $containerId -and
                [string]$_.relationshipType -eq 'CONTAINS' -and
                [string]$_.relatedSpdxElement -eq $componentId
            }
    )
    if ($matchingRelationships.Count -ne 1) {
        throw (
            "SPDX SBOM containment is missing for " +
            "'$($component.packageId)' and '$($component.id)'.")
    }
}
Test-SharpProofSbomDependencyGraph `
    -Relationships $relationships `
    -DependencyGraph $dependencyGraph
Test-SharpProofSbomComponentGraph `
    -SbomPackages $sbomPackages `
    -Relationships $relationships `
    -Components @($thirdPartyComponents)
Test-SharpProofSbomLicenseGraph `
    -SbomPackages $sbomPackages `
    -LicenseGraph $sbomLicenseGraph
$sbomFile = Get-Item -LiteralPath $resolvedSbom
$sbomHash = Get-FileHash `
    -LiteralPath $resolvedSbom `
    -Algorithm SHA256
$artifacts.Add([pscustomobject][ordered]@{
    fileName = $sbomFile.Name
    kind = 'sbom'
    packageId = $null
    bytes = [int64]$sbomFile.Length
    sha256 = $sbomHash.Hash.ToLowerInvariant()
})

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
    hashAlgorithm = 'SHA256'
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
$sumsPath = Join-Path $resolvedOutput 'SHA256SUMS'
Write-AtomicText -Path $manifestPath -Value $json
$null = Read-SharpProofCanonicalReleaseJson `
    -Path $manifestPath `
    -DocumentType ReleaseManifest
Write-SharpProofReleaseChecksumFile `
    -Path $sumsPath `
    -Artifacts $orderedArtifacts
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
$sumsPath = Join-Path $finalOutput 'SHA256SUMS'

Write-Host "Wrote deterministic SharpProof release evidence for version $($versions[0])."
[pscustomobject][ordered]@{
    ManifestPath = $manifestPath
    Sha256SumsPath = $sumsPath
    PackageVersion = $versions[0]
    RepositoryCommit = $commits[0]
    ArtifactCount = $orderedArtifacts.Count
}
