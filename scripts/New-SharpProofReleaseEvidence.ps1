[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [Parameter()]
    [string]$SbomPath,

    [Parameter()]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

$resolvedSource = (Resolve-Path `
    -LiteralPath $PackageSource `
    -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath $resolvedSource -PathType Container)) {
    throw "PackageSource is not a directory: $resolvedSource"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $resolvedOutput = $resolvedSource
}
else {
    $outputPath = [IO.Path]::GetFullPath($OutputDirectory)
    if (-not (Test-Path -LiteralPath $outputPath)) {
        New-Item -ItemType Directory -Path $outputPath |
            Out-Null
    }
    $resolvedOutput = (Resolve-Path `
        -LiteralPath $outputPath `
        -ErrorAction Stop).Path
    if (-not (Test-Path `
            -LiteralPath $resolvedOutput `
            -PathType Container)) {
        throw "OutputDirectory is not a directory: $resolvedOutput"
    }
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

$expectedIds = @(
    'SharpProof',
    'SharpProof.Attributes',
    'SharpProof.Verifier.Win-x64'
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
$commits = @(
    $identities |
        ForEach-Object { $_.Identity.RepositoryCommit } |
        Sort-Object -Unique
)
if ($commits.Count -ne 1) {
    throw "NuGet artifact repository commits must match; found '$($commits -join ', ')'."
}

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

if (-not [string]::IsNullOrWhiteSpace($SbomPath)) {
    $resolvedSbom = (Resolve-Path `
        -LiteralPath $SbomPath `
        -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolvedSbom -PathType Leaf)) {
        throw "SbomPath is not a file: $resolvedSbom"
    }
    $sbom = Get-Content -LiteralPath $resolvedSbom -Raw |
        ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$sbom.spdxVersion) -or
        -not ([string]$sbom.spdxVersion).StartsWith(
            'SPDX-',
            [StringComparison]::Ordinal)) {
        throw "SbomPath is not an SPDX JSON document: $resolvedSbom"
    }
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
}

$orderedArtifacts = @(
    $artifacts |
        Sort-Object fileName
)
$manifest = [pscustomobject][ordered]@{
    schemaVersion = 1
    packageVersion = $versions[0]
    repository = [pscustomobject][ordered]@{
        type = 'git'
        url = 'https://github.com/alexyorke/SharpProof'
        commit = $commits[0]
    }
    hashAlgorithm = 'SHA256'
    artifacts = $orderedArtifacts
}
$json = ($manifest | ConvertTo-Json -Depth 8) -replace "`r`n", "`n"
$json += "`n"
$sums = (
    $orderedArtifacts |
        ForEach-Object {
            if ($_.fileName -match '[\r\n]') {
                throw 'Artifact file names must not contain newlines.'
            }
            $_.sha256 + '  ' + $_.fileName
        }
) -join "`n"
$sums += "`n"

$manifestPath = Join-Path $resolvedOutput 'SharpProof.release.json'
$sumsPath = Join-Path $resolvedOutput 'SHA256SUMS'
Write-AtomicText -Path $manifestPath -Value $json
Write-AtomicText -Path $sumsPath -Value $sums

Write-Host "Wrote deterministic SharpProof release evidence for version $($versions[0])."
[pscustomobject][ordered]@{
    ManifestPath = $manifestPath
    Sha256SumsPath = $sumsPath
    PackageVersion = $versions[0]
    RepositoryCommit = $commits[0]
    ArtifactCount = $orderedArtifacts.Count
}
