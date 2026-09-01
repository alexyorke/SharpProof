Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanTopology.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PublicationDestination.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function Test-SharpProofPublicationVersionSyntax {
    param([Parameter(Mandatory = $true)][string]$Version)

    return $Version -cmatch (
        '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.' +
        '(0|[1-9][0-9]*)(?:-(?:(?:0|[1-9][0-9]*)|' +
        '(?:[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))' +
        '(?:\.(?:(?:0|[1-9][0-9]*)|' +
        '(?:[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)))*)?\z')
}

function Test-SharpProofPublicationCommitSyntax {
    param([Parameter(Mandatory = $true)][string]$Commit)

    return $Commit -cmatch '^[0-9a-f]{40}\z'
}

function Test-SharpProofExactProperties {
    param(
        [Parameter(Mandatory = $true)][object]$Value,
        [Parameter(Mandatory = $true)][string[]]$Expected
    )

    return (@($Value.PSObject.Properties.Name) -join '|') -ceq
        ($Expected -join '|')
}

function Test-SharpProofPublicationPlanIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Plan
    )

    if (($Plan.schemaVersion -isnot [int] -and
         $Plan.schemaVersion -isnot [int64]) -or
        [int64]$Plan.schemaVersion -ne 2) {
        throw 'Publication plan schema version is unsupported.'
    }
    if (-not (Test-SharpProofExactProperties -Value $Plan -Expected @(
                'schemaVersion','planOnly','packageVersion',
                'versionAuthority','repositoryCommit',
                'publicationDestination','packages','artifacts')) -or
        $Plan.planOnly -isnot [bool]) {
        throw 'Publication plan schema is invalid.'
    }
    if ($Plan.packageVersion -isnot [string] -or
        $Plan.repositoryCommit -isnot [string]) {
        throw 'Publication plan version or commit identity is invalid.'
    }
    $version = [string]$Plan.packageVersion
    $commit = [string]$Plan.repositoryCommit
    if (-not (Test-SharpProofPublicationVersionSyntax -Version $version) -or
        -not (Test-SharpProofPublicationCommitSyntax -Commit $commit)) {
        throw 'Publication plan version or commit identity is invalid.'
    }
    $versionAuthority = $Plan.versionAuthority
    if (-not (Test-SharpProofExactProperties `
            -Value $versionAuthority -Expected @(
                'schemaVersion','path','property','version','sha256')) -or
        ($versionAuthority.schemaVersion -isnot [int] -and
         $versionAuthority.schemaVersion -isnot [int64]) -or
        [int64]$versionAuthority.schemaVersion -ne 1 -or
        $versionAuthority.path -isnot [string] -or
        $versionAuthority.path -cne 'SharpProof.Release.props' -or
        $versionAuthority.property -isnot [string] -or
        $versionAuthority.property -cne 'SharpProofPackageVersion' -or
        $versionAuthority.version -isnot [string] -or
        $versionAuthority.version -cne $version -or
        $versionAuthority.sha256 -isnot [string] -or
        $versionAuthority.sha256 -cnotmatch '^[0-9a-f]{64}\z') {
        throw 'Publication plan version authority is invalid.'
    }
    $destination = $Plan.publicationDestination
    $fixtureArchives = @()
    if (-not (Test-SharpProofExactProperties -Value $destination -Expected @(
                'schemaVersion','mode','mainDestination',
                'symbolDestination','packageBaseAddress','fixture')) -or
        ($destination.schemaVersion -isnot [int] -and
         $destination.schemaVersion -isnot [int64]) -or
        [int64]$destination.schemaVersion -ne 1 -or
        $destination.mode -isnot [string] -or
        $destination.mode -cnotin @('targetless','fixture','registry')) {
        throw 'Publication destination schema is invalid.'
    }
    switch ([string]$destination.mode) {
        'targetless' {
            if (-not $Plan.planOnly -or
                $null -ne $destination.mainDestination -or
                $null -ne $destination.symbolDestination -or
                $null -ne $destination.packageBaseAddress -or
                $null -ne $destination.fixture) {
                throw 'Targetless publication destination is invalid.'
            }
        }
        'registry' {
            if ($destination.mainDestination -isnot [string] -or
                $destination.symbolDestination -isnot [string] -or
                $null -ne $destination.fixture) {
                throw 'Registry publication destination is invalid.'
            }
            foreach ($value in @(
                    $destination.mainDestination,
                    $destination.symbolDestination)) {
                $uri = $null
                if (-not [Uri]::TryCreate(
                        $value, [UriKind]::Absolute, [ref]$uri) -or
                    $uri.Scheme -cne 'https' -or
                    [string]::IsNullOrWhiteSpace($uri.Host) -or
                    -not [string]::IsNullOrEmpty($uri.UserInfo) -or
                    -not [string]::IsNullOrEmpty($uri.Query) -or
                    -not [string]::IsNullOrEmpty($uri.Fragment)) {
                    throw 'Registry publication destination is invalid.'
                }
            }
            if ($Plan.planOnly) {
                if ($null -ne $destination.packageBaseAddress) {
                    throw 'Registry publication destination is invalid.'
                }
            }
            elseif ($destination.packageBaseAddress -isnot [string]) {
                throw 'Registry publication destination is invalid.'
            }
            else {
                $baseUri = $null
                if (-not [Uri]::TryCreate(
                        $destination.packageBaseAddress,
                        [UriKind]::Absolute,
                        [ref]$baseUri) -or
                    $baseUri.Scheme -cne 'https' -or
                    [string]::IsNullOrWhiteSpace($baseUri.Host) -or
                    -not [string]::IsNullOrEmpty($baseUri.UserInfo) -or
                    -not [string]::IsNullOrEmpty($baseUri.Query) -or
                    -not [string]::IsNullOrEmpty($baseUri.Fragment) -or
                    $baseUri.AbsoluteUri.TrimEnd('/') -cne
                        $destination.packageBaseAddress) {
                    throw 'Registry publication destination is invalid.'
                }
            }
        }
        'fixture' {
            if (-not $Plan.planOnly -or
                $null -ne $destination.mainDestination -or
                $null -ne $destination.symbolDestination -or
                $null -ne $destination.packageBaseAddress -or
                $null -eq $destination.fixture) {
                throw 'Fixture publication destination is invalid.'
            }
            $fixture = $destination.fixture
            if (-not (Test-SharpProofExactProperties `
                    -Value $fixture -Expected @(
                        'path','fileIdentity','entryCount',
                        'entriesSha256','archives')) -or
                $fixture.path -isnot [string] -or
                -not [IO.Path]::IsPathFullyQualified($fixture.path) -or
                [IO.Path]::GetFullPath($fixture.path) -cne $fixture.path -or
                $fixture.fileIdentity -isnot [string] -or
                $fixture.fileIdentity -cnotmatch '^[0-9]+:[0-9]+\z' -or
                ($fixture.entryCount -isnot [int] -and
                 $fixture.entryCount -isnot [int64]) -or
                [int64]$fixture.entryCount -lt 0 -or
                $fixture.entriesSha256 -isnot [string] -or
                $fixture.entriesSha256 -cnotmatch '^[0-9a-f]{64}\z') {
                throw 'Fixture publication authority is invalid.'
            }
            $fixturePrefix = $fixture.path.TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar) +
                [IO.Path]::DirectorySeparatorChar
            $fixtureArchiveIdentities =
                [Collections.Generic.HashSet[string]]::new(
                    [StringComparer]::OrdinalIgnoreCase)
            $fixtureArchives = @($fixture.archives)
            foreach ($archive in $fixtureArchives) {
                if (-not (Test-SharpProofExactProperties `
                        -Value $archive -Expected @(
                            'path','packageId','version','role')) -or
                    $archive.path -isnot [string] -or
                    -not [IO.Path]::IsPathFullyQualified($archive.path) -or
                    [IO.Path]::GetFullPath($archive.path) -cne $archive.path -or
                    -not $archive.path.StartsWith(
                        $fixturePrefix, [StringComparison]::Ordinal) -or
                    $archive.packageId -isnot [string] -or
                    $archive.packageId -cnotmatch
                        '^[A-Za-z0-9][A-Za-z0-9._-]*\z' -or
                    $archive.version -isnot [string] -or
                    -not (Test-SharpProofPublicationVersionSyntax `
                        -Version $archive.version) -or
                    $archive.role -isnot [string] -or
                    $archive.role -cnotin @('main','symbols') -or
                    -not $fixtureArchiveIdentities.Add(
                        $archive.packageId + "`0" +
                        $archive.version + "`0" + $archive.role)) {
                    throw 'Fixture publication archive authority is invalid.'
                }
            }
        }
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
        foreach ($property in @(
                'path','fileName','sha256','role','version',
                'repositoryCommit')) {
            if ($artifact.$property -isnot [string]) {
                throw 'Publication plan artifact schema is invalid.'
            }
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
        if ($artifact.bytes -isnot [int64] -or
            $artifact.bytes -ne [int64]$file.Length -or
            [string]$artifact.sha256 -cne $hash) {
            throw "Publication plan artifact bytes changed: '$path'."
        }
    }
    if ($destination.mode -ceq 'fixture') {
        $packageSource = [IO.Path]::GetDirectoryName(
            [string]$artifacts[0].path)
        $currentSnapshot = New-SharpProofPublicationInputSnapshot `
            -PackageSource $packageSource `
            -FixtureDirectory ([string]$destination.fixture.path)
        $currentFixture = Get-SharpProofPublicationFixtureAuthority `
            -FixtureDirectory ([string]$destination.fixture.path) `
            -InputSnapshot $currentSnapshot
        $plannedFixtureJson = ConvertTo-Json `
            -InputObject $destination.fixture -Depth 6 -Compress
        $currentFixtureJson = ConvertTo-Json `
            -InputObject $currentFixture -Depth 6 -Compress
        if ($currentFixtureJson -cne $plannedFixtureJson) {
            throw 'Fixture publication authority changed after plan creation.'
        }
    }

    $packageIds = $SharpProofPackagePushOrder
    $packages = @($Plan.packages)
    if ($packages.Count -ne $packageIds.Count) {
        throw 'Publication plan package decisions are incomplete.'
    }
    for ($index = 0; $index -lt $packages.Count; $index++) {
        $package = $packages[$index]
        if (-not (Test-SharpProofExactProperties -Value $package -Expected @(
                    'packageId','version','mainFileName','symbolsFileName',
                    'availabilityMode','remoteState','fixtureState','remoteUrl',
                    'mainState','mainAction','symbolsState','symbolsAction'))) {
            throw 'Publication plan package decision schema is invalid.'
        }
        foreach ($property in @(
                'packageId','version','mainFileName','symbolsFileName',
                'availabilityMode','mainState','mainAction',
                'symbolsState','symbolsAction')) {
            if ($package.$property -isnot [string]) {
                throw 'Publication plan package decision schema is invalid.'
            }
        }
        if ($package.packageId -cne $packageIds[$index] -or
            $package.version -cne $version -or
            $package.mainFileName -cne $artifacts[$index * 2].fileName -or
            $package.symbolsFileName -cne
                $artifacts[$index * 2 + 1].fileName -or
            $package.availabilityMode -cne $destination.mode) {
            throw 'Publication plan package decision identity is invalid.'
        }
        switch ([string]$destination.mode) {
            'targetless' {
                if ($null -ne $package.remoteState -or
                    $null -ne $package.fixtureState -or
                    $null -ne $package.remoteUrl -or
                    $package.mainState -cne 'NotTargeted' -or
                    $package.mainAction -cne 'None' -or
                    $package.symbolsState -cne 'NotTargeted' -or
                    $package.symbolsAction -cne 'None') {
                    throw 'Targetless package decision is invalid.'
                }
            }
            'registry' {
                $expectedRemote = if ($Plan.planOnly) {
                    'Unchecked'
                } else { [string]$package.remoteState }
                $remoteUrlValid = $Plan.planOnly -and
                    $null -eq $package.remoteUrl
                if (-not $Plan.planOnly -and
                    $package.remoteUrl -is [string]) {
                    $normalizedId = $package.packageId.ToLowerInvariant()
                    $normalizedVersion = $package.version.ToLowerInvariant()
                    $expectedSuffix = '/' +
                        [Uri]::EscapeDataString($normalizedId) + '/' +
                        [Uri]::EscapeDataString($normalizedVersion) + '/' +
                        [Uri]::EscapeDataString(
                            "$normalizedId.$normalizedVersion.nupkg")
                    $remoteUrlValid = $package.remoteUrl -ceq
                        ($destination.packageBaseAddress + $expectedSuffix)
                }
                if ($package.remoteState -isnot [string] -or
                    $package.remoteState -cne $expectedRemote -or
                    $null -ne $package.fixtureState -or
                    -not $remoteUrlValid -or
                    $package.mainState -cne $expectedRemote -or
                    $package.mainAction -cne $(if ($Plan.planOnly) {
                        'PreflightThenPush'
                    } elseif ($expectedRemote -ceq 'Present') {
                        'Resume'
                    } else { 'Push' }) -or
                    $package.symbolsState -cne 'Unchecked' -or
                    $package.symbolsAction -cne 'CollisionOnPush') {
                    throw 'Registry package decision is invalid.'
                }
            }
            'fixture' {
                $matchingFixtureArchives = @($fixtureArchives | Where-Object {
                    [string]::Equals(
                        [string]$_.packageId,
                        [string]$package.packageId,
                        [StringComparison]::OrdinalIgnoreCase) -and
                    [string]::Equals(
                        [string]$_.version,
                        [string]$package.version,
                        [StringComparison]::OrdinalIgnoreCase)
                })
                $expectedMainState = if (@(
                        $matchingFixtureArchives | Where-Object {
                            $_.role -ceq 'main'
                        }).Count -eq 1) {
                    'FixturePresent'
                } else { 'FixtureAbsent' }
                $expectedSymbolsState = if (@(
                        $matchingFixtureArchives | Where-Object {
                            $_.role -ceq 'symbols'
                        }).Count -eq 1) {
                    'FixturePresent'
                } else { 'FixtureAbsent' }
                if ($null -ne $package.remoteState -or
                    $null -ne $package.remoteUrl -or
                    $package.fixtureState -isnot [string] -or
                    $package.fixtureState -cne $expectedMainState -or
                    $package.mainState -cne $package.fixtureState -or
                    $package.mainAction -cne $(
                        if ($package.fixtureState -ceq 'FixturePresent') {
                            'Collision'
                        } else { 'Push' }) -or
                    $package.symbolsState -cne $expectedSymbolsState -or
                    $package.symbolsAction -cne $(
                        if ($package.symbolsState -ceq 'FixturePresent') {
                            'Collision'
                        } else { 'Push' })) {
                    throw 'Fixture package decision is invalid.'
                }
            }
        }
    }

    $manifest = Get-Content -LiteralPath $artifacts[6].path -Raw | ConvertFrom-Json
    $manifestVersionAuthority = $manifest.versionAuthority
    if (-not (Test-SharpProofExactProperties `
            -Value $manifestVersionAuthority -Expected @(
                'schemaVersion','path','property','version','sha256')) -or
        ($manifestVersionAuthority.schemaVersion -isnot [int] -and
         $manifestVersionAuthority.schemaVersion -isnot [int64]) -or
        [int64]$manifestVersionAuthority.schemaVersion -ne
            [int64]$versionAuthority.schemaVersion -or
        $manifestVersionAuthority.path -isnot [string] -or
        [string]$manifestVersionAuthority.path -cne
            [string]$versionAuthority.path -or
        $manifestVersionAuthority.property -isnot [string] -or
        [string]$manifestVersionAuthority.property -cne
            [string]$versionAuthority.property -or
        $manifestVersionAuthority.version -isnot [string] -or
        [string]$manifestVersionAuthority.version -cne
            [string]$versionAuthority.version -or
        $manifestVersionAuthority.sha256 -isnot [string] -or
        [string]$manifestVersionAuthority.sha256 -cne
            [string]$versionAuthority.sha256 -or
        $manifest.packageVersion -isnot [string] -or
        $manifest.repository.commit -isnot [string] -or
        [string]$manifest.packageVersion -cne $version -or
        [string]$manifest.repository.commit -cne $commit) {
        throw 'Publication plan release manifest identity is stale.'
    }
    $manifestArtifacts = @($manifest.artifacts)
    foreach ($artifact in $manifestArtifacts) {
        if ($artifact.fileName -isnot [string] -or
            $artifact.sha256 -isnot [string]) {
            throw 'Publication plan release manifest schema is invalid.'
        }
    }
    foreach ($artifact in @($artifacts[0..5]) + @($artifacts[7])) {
        $row = @($manifestArtifacts | Where-Object {
            [string]$_.fileName -ceq [string]$artifact.fileName })
        if ($row.Count -ne 1 -or
            $row[0].bytes -isnot [int64] -or
            $row[0].bytes -ne $artifact.bytes -or
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
