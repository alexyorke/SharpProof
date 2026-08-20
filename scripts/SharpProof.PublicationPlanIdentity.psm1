Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-SharpProofPublicationPlanIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Plan
    )

    if ([int]$Plan.schemaVersion -ne 2) {
        throw 'Publication plan schema version is unsupported.'
    }
    $version = [string]$Plan.packageVersion
    $commit = [string]$Plan.repositoryCommit
    if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$' -or
        $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Publication plan version or commit identity is invalid.'
    }
    $artifacts = @($Plan.artifacts)
    $expectedRoles = @(
        'main','symbols','main','symbols','main','symbols',
        'release-manifest','sbom','checksums')
    if ($artifacts.Count -ne $expectedRoles.Count) {
        throw 'Publication plan must bind exactly nine release files.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    for ($index = 0; $index -lt $artifacts.Count; $index++) {
        $artifact = $artifacts[$index]
        $properties = @($artifact.PSObject.Properties.Name)
        if (($properties -join '|') -cne
                'path|fileName|bytes|sha256|role|version|repositoryCommit') {
            throw 'Publication plan artifact schema is invalid.'
        }
        $path = [string]$artifact.path
        if (-not [IO.Path]::IsPathFullyQualified($path) -or
            [IO.Path]::GetFullPath($path) -cne $path -or
            [IO.Path]::GetFileName($path) -cne [string]$artifact.fileName -or
            [string]$artifact.role -cne $expectedRoles[$index] -or
            [string]$artifact.version -cne $version -or
            [string]$artifact.repositoryCommit -cne $commit -or
            -not $seen.Add($path) -or
            -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw 'Publication plan artifact identity is invalid.'
        }
        $file = Get-Item -LiteralPath $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ([int64]$artifact.bytes -ne [int64]$file.Length -or
            [string]$artifact.sha256 -cne $hash) {
            throw "Publication plan artifact bytes changed: '$path'."
        }
    }

    $manifest = Get-Content -LiteralPath $artifacts[6].path -Raw | ConvertFrom-Json
    if ([string]$manifest.packageVersion -cne $version -or
        [string]$manifest.repository.commit -cne $commit) {
        throw 'Publication plan release manifest identity is stale.'
    }
    $manifestArtifacts = @($manifest.artifacts)
    foreach ($artifact in @($artifacts[0..5]) + @($artifacts[7])) {
        $row = @($manifestArtifacts | Where-Object {
            [string]$_.fileName -ceq [string]$artifact.fileName })
        if ($row.Count -ne 1 -or
            [int64]$row[0].bytes -ne [int64]$artifact.bytes -or
            [string]$row[0].sha256 -cne [string]$artifact.sha256) {
            throw 'Publication plan does not agree with the release manifest.'
        }
    }
    $expectedChecksumBytes = [Text.StringBuilder]::new()
    foreach ($artifact in $manifestArtifacts) {
        [void]$expectedChecksumBytes.Append(
            ([string]$artifact.sha256) + '  ' +
            ([string]$artifact.fileName) + "`n")
    }
    if ([IO.File]::ReadAllText([string]$artifacts[8].path) -cne
        $expectedChecksumBytes.ToString()) {
        throw 'Publication plan does not agree with SHA256SUMS.'
    }
}

function New-SharpProofPublicationPlanIdentities {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Packages,
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$RepositoryCommit
    )
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($package in $Packages) {
        foreach ($pair in @(
                [pscustomobject]@{ Path = [string]$package.mainPath; Role = 'main' },
                [pscustomobject]@{ Path = [string]$package.symbolsPath; Role = 'symbols' })) {
            $rows.Add((New-SharpProofPublicationPlanFileIdentity `
                -Path $pair.Path -Role $pair.Role -Version $Version `
                -RepositoryCommit $RepositoryCommit))
        }
    }
    foreach ($pair in @(
            [pscustomobject]@{ Path = Join-Path $Directory 'SharpProof.release.json'; Role = 'release-manifest' },
            [pscustomobject]@{ Path = Join-Path $Directory 'SharpProof.spdx.json'; Role = 'sbom' },
            [pscustomobject]@{ Path = Join-Path $Directory 'SHA256SUMS'; Role = 'checksums' })) {
        $rows.Add((New-SharpProofPublicationPlanFileIdentity `
            -Path $pair.Path -Role $pair.Role -Version $Version `
            -RepositoryCommit $RepositoryCommit))
    }
    return @($rows)
}

function New-SharpProofPublicationPlanFileIdentity {
    param([string]$Path,[string]$Role,[string]$Version,[string]$RepositoryCommit)
    $canonical = [IO.Path]::GetFullPath($Path)
    $file = Get-Item -LiteralPath $canonical
    return [pscustomobject][ordered]@{
        path = $canonical
        fileName = $file.Name
        bytes = [int64]$file.Length
        sha256 = (Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash.ToLowerInvariant()
        role = $Role
        version = $Version
        repositoryCommit = $RepositoryCommit
    }
}

Export-ModuleMember -Function New-SharpProofPublicationPlanIdentities,Test-SharpProofPublicationPlanIdentity
