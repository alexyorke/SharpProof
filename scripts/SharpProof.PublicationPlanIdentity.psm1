Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanTopology.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.PublicationDestination.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function Test-SharpProofPublicationCommitSyntax {
    param([Parameter(Mandatory = $true)][string]$Commit)

    return $Commit -cmatch '^[0-9a-f]{40}\z'
}

function Get-SharpProofPublicationHttpsValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    try {
        return Resolve-SharpProofPublicationHttpsDestination `
            -Value $Value `
            -Owner 'Registry publication destination'
    }
    catch {
        return $null
    }
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
    if (-not (Test-SharpProofReleaseVersionSyntax -Version $version) -or
        -not (Test-SharpProofPublicationCommitSyntax -Commit $commit)) {
        throw 'Publication plan version or commit identity is invalid.'
    }
    $versionAuthority = $Plan.versionAuthority
    if (-not (Test-SharpProofExactProperties `
            -Value $versionAuthority -Expected @(
                'schemaVersion','path','property','version')) -or
        ($versionAuthority.schemaVersion -isnot [int] -and
         $versionAuthority.schemaVersion -isnot [int64]) -or
        [int64]$versionAuthority.schemaVersion -ne 1 -or
        $versionAuthority.path -isnot [string] -or
        $versionAuthority.path -cne 'SharpProof.Release.props' -or
        $versionAuthority.property -isnot [string] -or
        $versionAuthority.property -cne 'SharpProofPackageVersion' -or
        $versionAuthority.version -isnot [string] -or
        $versionAuthority.version -cne $version) {
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
                if ($null -eq (Get-SharpProofPublicationHttpsValue -Value $value)) {
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
                $normalizedBaseAddress = Get-SharpProofPublicationHttpsValue `
                    -Value $destination.packageBaseAddress
                if ($null -eq $normalizedBaseAddress -or
                    $normalizedBaseAddress.TrimEnd('/') -cne
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
                        'archives')) -or
                $fixture.path -isnot [string] -or
                -not [IO.Path]::IsPathFullyQualified($fixture.path) -or
                [IO.Path]::GetFullPath($fixture.path) -cne $fixture.path -or
                $fixture.fileIdentity -isnot [string] -or
                $fixture.fileIdentity -cnotmatch '^[0-9]+:[0-9]+\z' -or
                ($fixture.entryCount -isnot [int] -and
                 $fixture.entryCount -isnot [int64]) -or
                [int64]$fixture.entryCount -lt 0) {
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
                    -not (Test-SharpProofReleaseVersionSyntax `
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
        'release-manifest')
    if ($artifacts.Count -ne $expectedRoles.Count) {
        throw 'Publication plan must bind exactly seven release files.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    for ($index = 0; $index -lt $artifacts.Count; $index++) {
        $artifact = $artifacts[$index]
        $properties = @($artifact.PSObject.Properties.Name)
        if (($properties -join '|') -cne
                'path|fileName|bytes|role|version|repositoryCommit') {
            throw 'Publication plan artifact schema is invalid.'
        }
        foreach ($property in @(
                'path','fileName','role','version','repositoryCommit')) {
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
        if ($artifact.bytes -isnot [int64] -or
            $artifact.bytes -ne [int64]$file.Length) {
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
                'schemaVersion','path','property','version')) -or
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
        $manifest.packageVersion -isnot [string] -or
        $manifest.repository.commit -isnot [string] -or
        [string]$manifest.packageVersion -cne $version -or
        [string]$manifest.repository.commit -cne $commit) {
        throw 'Publication plan release manifest identity is stale.'
    }
    $manifestArtifacts = @($manifest.artifacts)
    $manifestArtifactsByName =
        [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::Ordinal)
    $duplicateManifestArtifactNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($artifact in $manifestArtifacts) {
        if ($artifact.fileName -isnot [string]) {
            throw 'Publication plan release manifest schema is invalid.'
        }
        $fileName = [string]$artifact.fileName
        if ($manifestArtifactsByName.ContainsKey($fileName)) {
            [void]$duplicateManifestArtifactNames.Add($fileName)
        } else {
            $manifestArtifactsByName.Add($fileName, $artifact)
        }
    }
    foreach ($artifact in $artifacts[0..5]) {
        $fileName = [string]$artifact.fileName
        if ($duplicateManifestArtifactNames.Contains($fileName) -or
            -not $manifestArtifactsByName.ContainsKey($fileName)) {
            throw 'Publication plan does not agree with the release manifest.'
        }
        $row = $manifestArtifactsByName[$fileName]
        if ($row.bytes -isnot [int64] -or $row.bytes -ne $artifact.bytes) {
            throw 'Publication plan does not agree with the release manifest.'
        }
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
            [pscustomobject]@{ Path = Join-Path $Directory 'SharpProof.release.json'; Role = 'release-manifest' })) {
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
        role = $Role
        version = $Version
        repositoryCommit = $RepositoryCommit
    }
}

Export-ModuleMember -Function New-SharpProofPublicationPlanIdentities,Test-SharpProofPublicationPlanIdentity
