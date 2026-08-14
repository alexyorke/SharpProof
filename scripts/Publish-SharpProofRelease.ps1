[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [Parameter()]
    [string]$Source,

    [Parameter()]
    [string]$ApiKey,

    [Parameter()]
    [string]$ReadApiKey,

    [Parameter()]
    [string]$SymbolSource,

    [Parameter()]
    [string]$SymbolApiKey,

    [Parameter()]
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 300,

    [Parameter()]
    [string]$DotNetPath = 'dotnet',

    [Parameter()]
    [switch]$PlanOnly,

    [Parameter()]
    [string]$RemotePackageDirectory,

    [Parameter()]
    [string]$PlanOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Test-SharpProofSymbolPackages.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackagePayloads.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPackageDependencies.ps1')
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanTopology.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PublicationDestination.ps1')

$packageOrder = @(
    'SharpProof.Attributes',
    'SharpProof',
    'SharpProof.Verifier'
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

function Get-RepositoryHead {
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $releaseVersion = Get-SharpProofReleaseVersion `
        -RepositoryRoot $repositoryRoot
    if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is required to verify the release checkout commit.'
    }
    $head = (& git -C $repositoryRoot rev-parse --verify HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve the release checkout commit.'
    }
    return $head
}

function Get-RepositorySdkVersion {
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $globalJsonPath = Join-Path $repositoryRoot 'global.json'
    if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
        throw "The repository SDK policy is missing: $globalJsonPath"
    }

    $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw |
        ConvertFrom-Json
    $sdk = $globalJson.PSObject.Properties['sdk']
    $version = if ($null -eq $sdk) {
        $null
    }
    else {
        [string]$sdk.Value.version
    }
    if ($version -notmatch '^9\.0\.[0-9]+$') {
        throw "The repository SDK policy is invalid: '$version'."
    }
    return $version
}

function Resolve-ReleaseDotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Candidate,

        [Parameter(Mandatory = $true)]
        [string]$SdkVersion
    )

    if ([string]::IsNullOrWhiteSpace($Candidate) -or
        (-not [IO.Path]::IsPathRooted($Candidate) -and
         $Candidate -ne 'dotnet')) {
        throw (
            "DotNetPath must be the default 'dotnet' command or an " +
            "absolute trusted host path: '$Candidate'.")
    }
    $command = Get-Command $Candidate -ErrorAction SilentlyContinue
    if ($null -eq $command -or $command.CommandType -ne 'Application') {
        throw "DotNetPath is not an executable application: '$Candidate'."
    }
    $path = [IO.Path]::GetFullPath([string]$command.Path)
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    if (-not [IO.Path]::IsPathRooted($path) -or
        [IO.Path]::GetFileNameWithoutExtension($path) -ne 'dotnet') {
        throw "DotNetPath did not resolve to a trusted absolute host: '$path'."
    }
    if ($path.StartsWith(
            $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::Ordinal)) {
        throw "DotNetPath cannot use a project-local host: '$path'."
    }
    $actualVersion = (& $path --version 2>&1).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualVersion -ne $SdkVersion) {
        throw (
            "DotNetPath resolved SDK '$actualVersion'; repository policy " +
            "requires '$SdkVersion'.")
    }
    return $path
}

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
            throw "Package '$Path' has incomplete identity metadata."
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
            id = $id.InnerText
            version = $version.InnerText
            repositoryCommit = $repositoryCommit.ToLowerInvariant()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ArtifactPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    if ([string]::IsNullOrWhiteSpace($FileName) -or
        $FileName -eq '.' -or
        $FileName -eq '..' -or
        $FileName -match '[\r\n]' -or
        $FileName.Contains('/', [StringComparison]::Ordinal) -or
        $FileName.Contains('\', [StringComparison]::Ordinal) -or
        [IO.Path]::GetFileName($FileName) -ne $FileName) {
        throw "Release artifact has an unsafe file name: '$FileName'."
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Directory $FileName))
    $parent = [IO.Path]::GetDirectoryName($path)
    if (-not [string]::Equals(
            $parent,
            $Directory,
            [StringComparison]::Ordinal)) {
        throw "Release artifact escapes PackageSource: '$FileName'."
    }
    return $path
}

function Get-ValidatedRelease {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Directory,

        [Parameter(Mandatory = $true)]
        [string]$RepositoryCommit
    )

    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

    $manifestPath = Join-Path $Directory 'SharpProof.release.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Release manifest is missing: $manifestPath"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw |
        ConvertFrom-Json
    if ((Get-RequiredProperty $manifest 'schemaVersion' 'Release manifest') -ne
            2 -or
        [string](Get-RequiredProperty `
            $manifest `
            'hashAlgorithm' `
            'Release manifest') -ne 'SHA256') {
        throw 'Release manifest must use schema 2 and SHA256.'
    }
    $version = [string](Get-RequiredProperty `
        $manifest `
        'packageVersion' `
        'Release manifest')
    if ($version -notmatch
        '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
        throw "Release manifest package version is invalid: '$version'."
    }
    Test-SharpProofReleaseVersion `
        -ExpectedVersion $releaseVersion `
        -ActualVersion $version `
        -Owner 'Release manifest'
    Test-SharpProofReleaseVersionAuthority `
        -RepositoryRoot $repositoryRoot `
        -Authority (Get-RequiredProperty `
            $manifest 'versionAuthority' 'Release manifest')

    $repository = Get-RequiredProperty `
        $manifest `
        'repository' `
        'Release manifest'
    $manifestCommit = [string](Get-RequiredProperty `
        $repository `
        'commit' `
        'Release repository')
    if ([string](Get-RequiredProperty `
            $repository `
            'type' `
            'Release repository') -ne 'git' -or
        [string](Get-RequiredProperty `
            $repository `
            'url' `
            'Release repository') -ne
                'https://github.com/alexyorke/SharpProof' -or
        $manifestCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Release manifest repository identity is invalid.'
    }
    if ($manifestCommit -ne $RepositoryCommit) {
        throw (
            "Release manifest repository commit '$manifestCommit' does not " +
            "match checkout '$RepositoryCommit'.")
    }

    $artifacts = @(
        Get-RequiredProperty $manifest 'artifacts' 'Release manifest'
    )
    if ($artifacts.Count -ne 7) {
        throw 'Release manifest must contain exactly seven artifacts.'
    }
    $seenFileNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($artifact in $artifacts) {
        $fileName = [string](Get-RequiredProperty `
            $artifact `
            'fileName' `
            'Release artifact')
        $kind = [string](Get-RequiredProperty `
            $artifact `
            'kind' `
            "Release artifact '$fileName'")
        $bytes = [int64](Get-RequiredProperty `
            $artifact `
            'bytes' `
            "Release artifact '$fileName'")
        $sha256 = [string](Get-RequiredProperty `
            $artifact `
            'sha256' `
            "Release artifact '$fileName'")
        if (-not $seenFileNames.Add($fileName) -or
            $kind -notin @('package', 'symbols', 'sbom') -or
            $bytes -lt 0 -or
            $sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Release artifact metadata is invalid: '$fileName'."
        }
        $path = Get-ArtifactPath `
            -Directory $Directory `
            -FileName $fileName
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Release artifact is missing: $path"
        }
        $file = Get-Item -LiteralPath $path
        $actualHash = (Get-FileHash `
            -LiteralPath $path `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ([int64]$file.Length -ne $bytes -or
            $actualHash -ne $sha256) {
            throw "Release artifact does not match its manifest: '$fileName'."
        }
    }

    $packageArtifacts = @(
        $artifacts |
            Where-Object {
                [string]$_.kind -in @('package', 'symbols')
            }
    )
    $sbomArtifacts = @(
        $artifacts |
            Where-Object { [string]$_.kind -eq 'sbom' }
    )
    if ($packageArtifacts.Count -ne 6 -or
        $sbomArtifacts.Count -ne 1 -or
        [string]$sbomArtifacts[0].fileName -ne 'SharpProof.spdx.json') {
        throw 'Release manifest has an invalid package, symbol, or SBOM graph.'
    }

    $packages = [Collections.Generic.List[object]]::new()
    $payloadSets = @(
        Get-RequiredProperty $manifest 'packagePayloads' 'Release manifest'
    )
    if ($payloadSets.Count -ne $packageOrder.Count -or
        ((@($payloadSets.packageId | Sort-Object) -join '|') -ne
            (@($packageOrder | Sort-Object) -join '|'))) {
        throw 'Release manifest has an invalid package payload graph.'
    }
    $catalogComponents = @(Get-SharpProofThirdPartyComponentGraph)
    Test-SharpProofThirdPartyComponentProjection `
        -ActualComponents @($manifest.thirdPartyComponents) `
        -ExpectedComponents $catalogComponents
    foreach ($packageId in $packageOrder) {
        $main = @(
            $packageArtifacts |
                Where-Object {
                    [string]$_.kind -eq 'package' -and
                    [string]$_.packageId -eq $packageId
                }
        )
        $symbols = @(
            $packageArtifacts |
                Where-Object {
                    [string]$_.kind -eq 'symbols' -and
                    [string]$_.packageId -eq $packageId
                }
        )
        if ($main.Count -ne 1 -or $symbols.Count -ne 1) {
            throw (
                "Release manifest must contain one package and symbol " +
                "artifact for '$packageId'.")
        }
        if (-not ([string]$main[0].fileName).EndsWith(
                '.nupkg',
                [StringComparison]::OrdinalIgnoreCase) -or
            ([string]$main[0].fileName).EndsWith(
                '.snupkg',
                [StringComparison]::OrdinalIgnoreCase) -or
            -not ([string]$symbols[0].fileName).EndsWith(
                '.snupkg',
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Release package extensions are invalid for '$packageId'."
        }
        $mainPath = Get-ArtifactPath `
            -Directory $Directory `
            -FileName ([string]$main[0].fileName)
        $symbolsPath = Get-ArtifactPath `
            -Directory $Directory `
            -FileName ([string]$symbols[0].fileName)
        $mainIdentity = Get-PackageIdentity -Path $mainPath
        $symbolsIdentity = Get-PackageIdentity -Path $symbolsPath
        if ($mainIdentity.id -ne $packageId -or
            $symbolsIdentity.id -ne $packageId -or
            $mainIdentity.version -ne $version -or
            $symbolsIdentity.version -ne $version) {
            throw "Release package identity is invalid for '$packageId'."
        }
        if ($mainIdentity.repositoryCommit -ne $RepositoryCommit -or
            $symbolsIdentity.repositoryCommit -ne $RepositoryCommit) {
            throw (
                "Release package repository commit does not match checkout " +
                "'$RepositoryCommit' for '$packageId'.")
        }
        $components = @(
            $catalogComponents |
                Where-Object { [string]$_.packageId -eq $packageId }
        )
        $null = Test-SharpProofPackagePayload `
            -PackagePath $mainPath `
            -PackageId $packageId `
            -RepositoryRoot $repositoryRoot `
            -Components $components `
            -ExpectedPayloads @(
                $payloadSets |
                    Where-Object { [string]$_.packageId -eq $packageId } |
                    ForEach-Object { @($_.entries) }
            )
        $null = Test-SharpProofSymbolPackagePair `
            -PackagePath $mainPath `
            -SymbolPackagePath $symbolsPath `
            -PackageId $packageId `
            -PackageVersion $version `
            -RepositoryCommit $RepositoryCommit
        $null = $packages.Add([pscustomobject][ordered]@{
            packageId = $packageId
            version = $version
            mainFileName = [string]$main[0].fileName
            mainPath = $mainPath
            symbolsFileName = [string]$symbols[0].fileName
            symbolsPath = $symbolsPath
        })
    }
    $actualPackageIds = @(
        $packageArtifacts |
            ForEach-Object { [string]$_.packageId } |
            Sort-Object -Unique
    )
    if (($actualPackageIds -join '|') -ne
        (@($packageOrder | Sort-Object) -join '|')) {
        throw 'Release manifest contains an unexpected package ID.'
    }

    $dependencyGraph = @(Get-SharpProofPackageDependencyGraph `
        -PackagePaths @($packages | ForEach-Object {
            $_.mainPath
            $_.symbolsPath
        }))
    $licenseGraph = @(Get-SharpProofPackageLicenseGraph `
        -PackagePaths @($packages | ForEach-Object {
            $_.mainPath
            $_.symbolsPath
        }))
    $sbomLicenseGraph = @(Get-SharpProofSbomLicenseGraph `
        -PackageLicenseGraph $licenseGraph `
        -PackageVersion $version `
        -ThirdPartyComponents $catalogComponents)
    $sbomPath = Get-ArtifactPath `
        -Directory $Directory `
        -FileName ([string]$sbomArtifacts[0].fileName)
    $sbom = Get-Content -LiteralPath $sbomPath -Raw |
        ConvertFrom-Json
    if ($null -eq $sbom.PSObject.Properties['relationships'] -or
        $null -eq $sbom.PSObject.Properties['documentDescribes'] -or
        $null -eq $sbom.PSObject.Properties['packages']) {
        throw 'Release SBOM has no complete package topology.'
    }
    foreach ($packageId in $packageOrder) {
        $sbomPackages = @($sbom.packages | Where-Object {
            [string]$_.name -ceq $packageId -and
            [string]$_.versionInfo -ceq $version
        })
        $manifestPackages = @($packageArtifacts | Where-Object {
            [string]$_.kind -ceq 'package' -and
            [string]$_.packageId -ceq $packageId
        })
        if ($sbomPackages.Count -ne 1 -or $manifestPackages.Count -ne 1) {
            throw "Release SBOM package checksum identity is invalid: $packageId"
        }
        Test-SharpProofSpdxPackageChecksum `
            -Package $sbomPackages[0] `
            -ExpectedSha256 ([string]$manifestPackages[0].sha256) `
            -Identity $packageId
    }
    Test-SharpProofSbomTopology `
        -SbomPackages @($sbom.packages) `
        -DocumentDescribes @($sbom.documentDescribes) `
        -Relationships @($sbom.relationships) `
        -FirstPartyPackageIds $packageOrder `
        -PackageVersion $version `
        -Components $catalogComponents `
        -DependencyGraph $dependencyGraph
    Test-SharpProofSbomArtifactScope `
        -Artifacts $packageArtifacts `
        -SbomPackages @($sbom.packages) `
        -DocumentDescribes @($sbom.documentDescribes) `
        -FirstPartyPackageIds $packageOrder `
        -PackageVersion $version
    Test-SharpProofSbomAttestationWorkflow -Workflow (
        Get-Content -LiteralPath (Join-Path `
            $repositoryRoot '.github/workflows/package-consumers.yml') -Raw)
    Test-SharpProofSbomDependencyGraph `
        -Relationships @($sbom.relationships) `
        -DependencyGraph $dependencyGraph
    Test-SharpProofSbomComponentGraph `
        -SbomPackages @($sbom.packages) `
        -Relationships @($sbom.relationships) `
        -Components $catalogComponents
    Test-SharpProofSbomLicenseGraph `
        -SbomPackages @($sbom.packages) `
        -LicenseGraph $sbomLicenseGraph

    return [pscustomobject][ordered]@{
        version = $version
        versionAuthority = $manifest.versionAuthority
        packages = @($packages)
    }
}

function Invoke-V3Get {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [Parameter()]
        [string]$OutputPath
    )

    $parameters = @{
        Uri = $Uri
        Method = 'Get'
        SkipHttpErrorCheck = $true
        TimeoutSec = $TimeoutSeconds
        ErrorAction = 'Stop'
    }
    if (-not [string]::IsNullOrWhiteSpace($ReadApiKey)) {
        $parameters.Headers = @{
            'X-NuGet-ApiKey' = $ReadApiKey
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $parameters.OutFile = $OutputPath
        $parameters.PassThru = $true
    }
    return Invoke-WebRequest @parameters
}

function Get-V3PackageBaseAddress {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceIndex
    )

    $serviceUri = $null
    if (-not [Uri]::TryCreate(
            $ServiceIndex,
            [UriKind]::Absolute,
            [ref]$serviceUri) -or
        $serviceUri.Scheme -ne 'https') {
        throw "NuGet source must be an HTTPS V3 service index: '$ServiceIndex'."
    }
    $response = Invoke-V3Get -Uri $serviceUri.AbsoluteUri
    if ([int]$response.StatusCode -ne 200) {
        throw (
            "NuGet V3 service index returned HTTP " +
            "$([int]$response.StatusCode): $ServiceIndex")
    }
    try {
        $index = $response.Content | ConvertFrom-Json
    }
    catch {
        throw "NuGet source is not a valid V3 service index: '$ServiceIndex'."
    }
    $resources = @(
        Get-RequiredProperty $index 'resources' 'NuGet V3 service index'
    )
    $baseAddresses = @(
        $resources |
            Where-Object {
                @($_.'@type') |
                    Where-Object {
                        [string]$_ -match '^PackageBaseAddress/'
                    }
            } |
            ForEach-Object { [string]$_.'@id' } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    if ($baseAddresses.Count -ne 1) {
        throw (
            "NuGet V3 service index must expose exactly one " +
            'PackageBaseAddress resource.')
    }
    $baseUri = $null
    if (-not [Uri]::TryCreate(
            $serviceUri,
            $baseAddresses[0],
            [ref]$baseUri) -or
        $baseUri.Scheme -ne 'https') {
        throw 'NuGet PackageBaseAddress must resolve to HTTPS.'
    }
    return $baseUri.AbsoluteUri.TrimEnd('/')
}

function Get-RemotePackageUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseAddress,

        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    $normalizedId = $PackageId.ToLowerInvariant()
    $normalizedVersion = $Version.ToLowerInvariant()
    return (
        $BaseAddress + '/' +
        [Uri]::EscapeDataString($normalizedId) + '/' +
        [Uri]::EscapeDataString($normalizedVersion) + '/' +
        [Uri]::EscapeDataString(
            "$normalizedId.$normalizedVersion.nupkg"))
}

function Get-RemotePackageState {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Package,

        [Parameter()]
        [string]$BaseAddress,

        [Parameter()]
        [string]$FixtureDirectory
    )

    if (-not [string]::IsNullOrWhiteSpace($FixtureDirectory)) {
        $remoteMainPath = Join-Path `
            $FixtureDirectory `
            $Package.mainFileName
        if (Test-Path -LiteralPath $remoteMainPath -PathType Leaf) {
            throw (
                "Remote main package already exists; publication is " +
                "non-overwriting: $($Package.packageId) " +
                "$($Package.version).")
        }
        $remoteSymbolsPath = Join-Path `
            $FixtureDirectory `
            $Package.symbolsFileName
        if (Test-Path -LiteralPath $remoteSymbolsPath -PathType Leaf) {
            throw (
                "Remote symbol package already exists; publication is " +
                "non-overwriting: $($Package.packageId) " +
                "$($Package.version).")
        }
        return [pscustomobject][ordered]@{
            state = 'Absent'
            remoteUrl = $null
        }
    }

    $remoteUrl = Get-RemotePackageUrl `
        -BaseAddress $BaseAddress `
        -PackageId $Package.packageId `
        -Version $Package.version
    $temporaryPath = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::Delete($temporaryPath)
        $response = Invoke-V3Get `
            -Uri $remoteUrl `
            -OutputPath $temporaryPath
        $status = [int]$response.StatusCode
        if ($status -eq 404) {
            return [pscustomobject][ordered]@{
                state = 'Absent'
                remoteUrl = $remoteUrl
            }
        }
        if ($status -ne 200) {
            throw (
                "NuGet PackageBaseAddress returned HTTP $status for " +
                "$($Package.packageId) $($Package.version).")
        }
        throw (
            "Remote main package already exists; publication is " +
            "non-overwriting: $($Package.packageId) $($Package.version).")
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

function Invoke-NuGetPush {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Destination,

        [Parameter(Mandatory = $true)]
        [string]$Key,

        [Parameter(Mandatory = $true)]
        [bool]$NoSymbols
    )

    $arguments = [Collections.Generic.List[string]]::new()
    foreach ($argument in @(
            'nuget',
            'push',
            $Path,
            '--api-key',
            $Key,
            '--source',
            $Destination,
            '--timeout',
            [string]$TimeoutSeconds)) {
        $arguments.Add($argument)
    }
    if ($NoSymbols) {
        $arguments.Add('--no-symbols')
    }
    & $DotNetPath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw (
            "NuGet push failed with exit code $LASTEXITCODE for " +
            "'$([IO.Path]::GetFileName($Path))'.")
    }
}

function Write-PublicationPlan {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Plan,

        [AllowNull()][string]$OutputPath,

        [AllowNull()][object]$InputSnapshot
    )

    $json = ($Plan | ConvertTo-Json -Depth 6) -replace "`r`n", "`n"
    $json += "`n"
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        Write-Output $json.TrimEnd()
        return
    }
    Write-SharpProofPublicationPlanAtomic `
        -OutputPath $OutputPath `
        -Json $json `
        -InputSnapshot $InputSnapshot
}

$resolvedPackageSource = (Resolve-Path `
    -LiteralPath $PackageSource `
    -ErrorAction Stop).Path
if (-not (Test-Path `
        -LiteralPath $resolvedPackageSource `
        -PathType Container)) {
    throw "PackageSource is not a directory: $resolvedPackageSource"
}
if (-not $PlanOnly -and
    -not [string]::IsNullOrWhiteSpace($RemotePackageDirectory)) {
    throw 'RemotePackageDirectory is available only with PlanOnly.'
}
$resolvedRemoteDirectory = $null
if (-not [string]::IsNullOrWhiteSpace($RemotePackageDirectory)) {
    $resolvedRemoteDirectory = (Resolve-Path `
        -LiteralPath $RemotePackageDirectory `
        -ErrorAction Stop).Path
    if (-not (Test-Path `
            -LiteralPath $resolvedRemoteDirectory `
            -PathType Container)) {
        throw (
            "RemotePackageDirectory is not a directory: " +
            $resolvedRemoteDirectory)
    }
}
$resolvedPlanOutputPath = $null
$publicationInputSnapshot = New-SharpProofPublicationInputSnapshot `
    -PackageSource $resolvedPackageSource `
    -FixtureDirectory $resolvedRemoteDirectory
if ($PlanOnly -and -not [string]::IsNullOrWhiteSpace($PlanOutputPath)) {
    $resolvedPlanOutputPath = Resolve-SharpProofPublicationPlanOutput `
        -Path $PlanOutputPath
    Assert-SharpProofPublicationPlanTopology `
        -OutputPath $resolvedPlanOutputPath `
        -InputSnapshot $publicationInputSnapshot
}
$publicationDestination = New-SharpProofPublicationDestinationAuthority `
    -Source $Source `
    -SymbolSource $SymbolSource `
    -FixtureDirectory $resolvedRemoteDirectory `
    -InputSnapshot $publicationInputSnapshot
if (-not $PlanOnly -and
    ([string]::IsNullOrWhiteSpace($Source) -or
     [string]::IsNullOrWhiteSpace($ApiKey))) {
    throw 'Source and ApiKey are required for publication.'
}
if (-not $PlanOnly) {
    $DotNetPath = Resolve-ReleaseDotNet `
        -Candidate $DotNetPath `
        -SdkVersion (Get-RepositorySdkVersion)
}

$repositoryHead = Get-RepositoryHead
$release = Get-ValidatedRelease `
    -Directory $resolvedPackageSource `
    -RepositoryCommit $repositoryHead
$baseAddress = $null
if (-not $PlanOnly) {
    $baseAddress = Get-V3PackageBaseAddress `
        -ServiceIndex $publicationDestination.mainDestination
}
$entries = [Collections.Generic.List[object]]::new()
foreach ($package in $release.packages) {
    $remote = if ($PlanOnly -and
        $publicationDestination.mode -cne 'fixture') {
        [pscustomobject][ordered]@{
            state = if ($publicationDestination.mode -ceq 'registry') {
                'Unchecked'
            }
            else { $null }
            remoteUrl = $null
        }
    }
    else {
        Get-RemotePackageState `
            -Package $package `
            -BaseAddress $baseAddress `
            -FixtureDirectory $resolvedRemoteDirectory
    }
    $entries.Add([pscustomobject][ordered]@{
        packageId = $package.packageId
        version = $package.version
        mainFileName = $package.mainFileName
        symbolsFileName = $package.symbolsFileName
        availabilityMode = $publicationDestination.mode
        remoteState = if ($publicationDestination.mode -ceq 'fixture') {
            $null
        }
        else { $remote.state }
        fixtureState = if ($publicationDestination.mode -ceq 'fixture') {
            $remote.state
        }
        else { $null }
        remoteUrl = $remote.remoteUrl
        mainAction = if ($publicationDestination.mode -ceq 'targetless') {
            $null
        }
        elseif ($remote.state -eq 'Absent') {
            'Push'
        }
        else {
            'PreflightThenPush'
        }
        symbolsAction = if ($publicationDestination.mode -ceq 'targetless') {
            $null
        }
        elseif ($remote.state -eq 'Absent') {
            'Push'
        }
        else {
            'PreflightThenPush'
        }
    })
}

$plan = [pscustomobject][ordered]@{
    schemaVersion = 1
    planOnly = [bool]$PlanOnly
    packageVersion = $release.version
    versionAuthority = $release.versionAuthority
    repositoryCommit = $repositoryHead
    publicationDestination = $publicationDestination
    packages = @($entries)
}
if ($PlanOnly) {
    Write-PublicationPlan `
        -Plan $plan `
        -OutputPath $resolvedPlanOutputPath `
        -InputSnapshot $publicationInputSnapshot
    return
}

$effectiveSymbolApiKey = if (
    [string]::IsNullOrWhiteSpace($SymbolApiKey)) {
    $ApiKey
}
else {
    $SymbolApiKey
}
for ($index = 0; $index -lt $release.packages.Count; $index++) {
    $package = $release.packages[$index]
    Write-Host (
        "Publishing $($package.packageId) $($package.version) " +
        "main package.")
    Invoke-NuGetPush `
        -Path $package.mainPath `
        -Destination $publicationDestination.mainDestination `
        -Key $ApiKey `
        -NoSymbols $true
    Write-Host (
        "Publishing $($package.packageId) $($package.version) " +
        "symbol package.")
    Invoke-NuGetPush `
        -Path $package.symbolsPath `
        -Destination $publicationDestination.symbolDestination `
        -Key $effectiveSymbolApiKey `
        -NoSymbols $false
}
Write-PublicationPlan -Plan $plan
