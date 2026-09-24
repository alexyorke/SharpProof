if (-not (Get-Command Test-SharpProofReleaseVersionSyntax `
        -CommandType Function -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
}
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force

function Resolve-SharpProofPublicationHttpsDestination {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Owner
    )

    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -cne 'https' -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Query) -or
        -not [string]::IsNullOrEmpty($uri.Fragment)) {
        throw "$Owner must be an absolute HTTPS URI without user info, query, or fragment."
    }
    return $uri.AbsoluteUri
}

function Get-SharpProofPublicationFixtureAuthority {
    param(
        [Parameter(Mandatory = $true)][string]$FixtureDirectory,
        [Parameter(Mandatory = $true)][object]$InputSnapshot
    )

    $canonical = (& readlink -f -- $FixtureDirectory).Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($canonical) -or
        -not (Test-Path -LiteralPath $canonical -PathType Container) -or
        [string]$InputSnapshot.fixtureDirectory -cne $canonical) {
        throw 'Fixture directory identity is invalid.'
    }
    $directoryIdentity = (& stat -Lc '%d:%i' -- $canonical).Trim()
    if ($LASTEXITCODE -ne 0 -or $directoryIdentity -notmatch '^[0-9]+:[0-9]+$') {
        throw 'Fixture directory file identity is unavailable.'
    }
    $prefix = $canonical.TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $entries = @($InputSnapshot.entries | Where-Object {
        ([string]$_.path).StartsWith($prefix, [StringComparison]::Ordinal)
    } | Sort-Object path)
    return [pscustomobject][ordered]@{
        path = $canonical
        fileIdentity = $directoryIdentity
        entryCount = $entries.Count
        archives = @(Get-SharpProofPublicationFixtureArchiveCatalog `
            -FixtureDirectory $canonical)
    }
}

function Get-SharpProofPublicationFixtureArchiveCatalog {
    param(
        [Parameter(Mandatory = $true)][string]$FixtureDirectory
    )

    $catalog = [Collections.Generic.List[object]]::new()
    $identities = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $archives = @(
        Get-ChildItem -LiteralPath $FixtureDirectory -File -Recurse |
            Where-Object {
                $_.Extension -ieq '.nupkg' -or
                $_.Extension -ieq '.snupkg'
            } |
            Sort-Object FullName)
    foreach ($file in $archives) {
        $identity = Get-SharpProofPackageIdentity `
            -Path $file.FullName -RequireSingleIdentity
        $id = [string]$identity.Id
        $version = [string]$identity.Version
        try {
            $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
        }
        catch {
            throw "Fixture archive is malformed: '$($file.FullName)'."
        }
        try {
            if ($id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or
                -not (Test-SharpProofReleaseVersionSyntax `
                    -Version $version)) {
                throw "Fixture archive nuspec identity is invalid: '$($file.FullName)'."
            }
            $hasDll = $false
            $hasPdb = $false
            foreach ($entry in $archive.Entries) {
                if (-not $hasDll -and $entry.FullName.EndsWith(
                        '.dll', [StringComparison]::OrdinalIgnoreCase)) {
                    $hasDll = $true
                }
                if (-not $hasPdb -and $entry.FullName.EndsWith(
                        '.pdb', [StringComparison]::OrdinalIgnoreCase)) {
                    $hasPdb = $true
                }
                if ($hasDll -and $hasPdb) {
                    break
                }
            }
            if ($hasDll -eq $hasPdb) {
                throw "Fixture archive role is ambiguous: '$($file.FullName)'."
            }
            $role = if ($hasDll) { 'main' } else { 'symbols' }
            $key = $id + "`0" + $version + "`0" + $role
            if (-not $identities.Add($key)) {
                throw "Fixture archive identity and role are duplicated: '$id $version $role'."
            }
            $catalog.Add([pscustomobject][ordered]@{
                path = [IO.Path]::GetFullPath($file.FullName)
                packageId = $id
                version = $version
                role = $role
            })
        }
        finally { $archive.Dispose() }
    }
    return @($catalog)
}

function Get-SharpProofPublicationFixturePackageState {
    param(
        [AllowNull()][AllowEmptyCollection()][object[]]$Catalog,
        [Parameter(Mandatory = $true)][string]$PackageId,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $matching = @($Catalog | Where-Object {
        $null -ne $_ -and
        [string]::Equals(
            [string]$_.packageId, $PackageId,
            [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals(
            [string]$_.version, $Version,
            [StringComparison]::OrdinalIgnoreCase)
    })
    return [pscustomobject][ordered]@{
        mainState = if (@($matching | Where-Object {
                    [string]$_.role -ceq 'main'
                }).Count -eq 1) { 'FixturePresent' } else { 'FixtureAbsent' }
        symbolsState = if (@($matching | Where-Object {
                    [string]$_.role -ceq 'symbols'
                }).Count -eq 1) { 'FixturePresent' } else { 'FixtureAbsent' }
        remoteUrl = $null
    }
}

function New-SharpProofPublicationDestinationAuthority {
    param(
        [AllowNull()][string]$Source,
        [AllowNull()][string]$SymbolSource,
        [AllowNull()][string]$FixtureDirectory,
        [Parameter(Mandatory = $true)][object]$InputSnapshot
    )

    $hasMain = -not [string]::IsNullOrWhiteSpace($Source)
    $hasSymbols = -not [string]::IsNullOrWhiteSpace($SymbolSource)
    $hasFixture = -not [string]::IsNullOrWhiteSpace($FixtureDirectory)
    if ($hasFixture -and ($hasMain -or $hasSymbols)) {
        throw 'Fixture and registry publication modes are mutually exclusive.'
    }
    if (-not $hasMain -and $hasSymbols) {
        throw 'SymbolSource requires a main Source destination.'
    }
    if ($hasFixture) {
        return [pscustomobject][ordered]@{
            schemaVersion = 1
            mode = 'fixture'
            mainDestination = $null
            symbolDestination = $null
            packageBaseAddress = $null
            fixture = Get-SharpProofPublicationFixtureAuthority `
                -FixtureDirectory $FixtureDirectory `
                -InputSnapshot $InputSnapshot
        }
    }
    if (-not $hasMain) {
        return [pscustomobject][ordered]@{
            schemaVersion = 1
            mode = 'targetless'
            mainDestination = $null
            symbolDestination = $null
            packageBaseAddress = $null
            fixture = $null
        }
    }
    $main = Resolve-SharpProofPublicationHttpsDestination `
        -Value $Source -Owner 'Source'
    $symbols = if ($hasSymbols) {
        Resolve-SharpProofPublicationHttpsDestination `
            -Value $SymbolSource -Owner 'SymbolSource'
    }
    else { $main }
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        mode = 'registry'
        mainDestination = $main
        symbolDestination = $symbols
        packageBaseAddress = $null
        fixture = $null
    }
}

function Test-SharpProofPublicationDestinationAuthority {
    param(
        [Parameter(Mandatory = $true)][object]$Authority,
        [AllowNull()][string]$Source,
        [AllowNull()][string]$SymbolSource,
        [AllowNull()][string]$FixtureDirectory,
        [Parameter(Mandatory = $true)][object]$InputSnapshot
    )

    Test-SharpProofPublicationInputSnapshot -Snapshot $InputSnapshot
    $expected = New-SharpProofPublicationDestinationAuthority `
        -Source $Source -SymbolSource $SymbolSource `
        -FixtureDirectory $FixtureDirectory -InputSnapshot $InputSnapshot
    Assert-SharpProofCanonicalMatch `
        -Actual $Authority -Expected $expected -Depth 5 `
        -Message 'Publication destination authority is invalid.'
}

function New-SharpProofPublicationActionAuthority {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('targetless','fixture','registry')]
        [string]$Mode,

        [AllowNull()][string]$MainState,
        [AllowNull()][string]$FixtureMainState,
        [AllowNull()][string]$FixtureSymbolsState
    )

    if ($Mode -cne 'registry' -and
        -not [string]::IsNullOrEmpty($MainState)) {
        throw 'Only registry publication has a main remote state.'
    }
    if ($Mode -ceq 'registry' -and
        $MainState -cnotin @('Absent', 'Unchecked', 'VerifiedPresent')) {
        throw 'Registry main state is invalid.'
    }
    if ($Mode -ceq 'fixture') {
        if ([string]::IsNullOrEmpty($FixtureMainState)) {
            $FixtureMainState = 'FixtureAbsent'
        }
        if ([string]::IsNullOrEmpty($FixtureSymbolsState)) {
            $FixtureSymbolsState = 'FixtureAbsent'
        }
        if ($FixtureMainState -cnotin @('FixtureAbsent','FixturePresent') -or
            $FixtureSymbolsState -cnotin @('FixtureAbsent','FixturePresent')) {
            throw 'Fixture package states are invalid.'
        }
    }
    $authority = switch ($Mode) {
        'targetless' {
            [pscustomobject][ordered]@{
                mainState = 'NotTargeted'
                mainAction = 'None'
                symbolsState = 'NotTargeted'
                symbolsAction = 'None'
            }
        }
        'fixture' {
            [pscustomobject][ordered]@{
                mainState = $FixtureMainState
                mainAction = if ($FixtureMainState -ceq 'FixturePresent') {
                    'Collision'
                } else { 'Push' }
                symbolsState = $FixtureSymbolsState
                symbolsAction = if ($FixtureSymbolsState -ceq 'FixturePresent') {
                    'Collision'
                } else { 'Push' }
            }
        }
        'registry' {
            [pscustomobject][ordered]@{
                mainState = $MainState
                mainAction = if ($MainState -ceq 'Absent') {
                    'Push'
                }
                elseif ($MainState -ceq 'VerifiedPresent') {
                    'ReuseVerified'
                }
                else { 'PreflightThenPush' }
                symbolsState = 'Unchecked'
                symbolsAction = 'CollisionOnPush'
            }
        }
    }
    return $authority
}

function Test-SharpProofPublicationActionAuthority {
    param(
        [Parameter(Mandatory = $true)][object]$Authority,
        [Parameter(Mandatory = $true)]
        [ValidateSet('targetless','fixture','registry')]
        [string]$Mode,
        [AllowNull()][string]$MainState,
        [AllowNull()][string]$FixtureMainState,
        [AllowNull()][string]$FixtureSymbolsState
    )

    $expected = New-SharpProofPublicationActionAuthority `
        -Mode $Mode -MainState $MainState `
        -FixtureMainState $FixtureMainState `
        -FixtureSymbolsState $FixtureSymbolsState
    Assert-SharpProofCanonicalMatch `
        -Actual $Authority -Expected $expected -Depth 2 `
        -Message 'Publication action authority is invalid.'
}

function Test-SharpProofNuGetOrgSymbolPublishCapability {
    param(
        [Parameter(Mandatory = $true)][string]$MainDestination,
        [Parameter(Mandatory = $true)][string]$SymbolDestination,
        [AllowNull()][AllowEmptyCollection()][object[]]$Resources
    )

    $canonicalServiceIndex = 'https://api.nuget.org/v3/index.json'
    foreach ($destination in @($MainDestination, $SymbolDestination)) {
        $normalized = $null
        try {
            $normalized = Resolve-SharpProofPublicationHttpsDestination `
                -Value $destination `
                -Owner 'NuGet service index'
        }
        catch {
            return $false
        }
        if ($normalized -cne $canonicalServiceIndex) {
            return $false
        }
    }

    $symbolResources = @($Resources | Where-Object {
        @($_.'@type') -ccontains 'SymbolPackagePublish/4.9.0'
    })
    if ($symbolResources.Count -ne 1 -or
        $symbolResources[0].'@id' -isnot [string]) {
        return $false
    }
    $symbolEndpoint = $null
    if (-not [Uri]::TryCreate(
            [string]$symbolResources[0].'@id',
            [UriKind]::Absolute,
            [ref]$symbolEndpoint) -or
        $symbolEndpoint.Scheme -cne 'https' -or
        $symbolEndpoint.AbsoluteUri -cne
            'https://www.nuget.org/api/v2/symbolpackage') {
        return $false
    }
    return $true
}

function Test-SharpProofFileByteEquality {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedPath,
        [Parameter(Mandatory = $true)][string]$ActualPath
    )

    $expectedInfo = Get-Item -LiteralPath $ExpectedPath -ErrorAction Stop
    $actualInfo = Get-Item -LiteralPath $ActualPath -ErrorAction Stop
    if ($expectedInfo.Length -ne $actualInfo.Length) {
        return $false
    }
    $expected = [IO.File]::Open(
        $ExpectedPath,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        $actual = [IO.File]::Open(
            $ActualPath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        try {
            $expectedBuffer = [byte[]]::new(65536)
            $actualBuffer = [byte[]]::new(65536)
            while ($true) {
                $expectedRead = $expected.Read(
                    $expectedBuffer, 0, $expectedBuffer.Length)
                $actualRead = $actual.Read(
                    $actualBuffer, 0, $actualBuffer.Length)
                if ($expectedRead -ne $actualRead) {
                    return $false
                }
                if ($expectedRead -eq 0) {
                    return $true
                }
                for ($index = 0; $index -lt $expectedRead; $index++) {
                    if ($expectedBuffer[$index] -ne $actualBuffer[$index]) {
                        return $false
                    }
                }
            }
        }
        finally {
            $actual.Dispose()
        }
    }
    finally {
        $expected.Dispose()
    }
}

function Invoke-SharpProofPublicationPushSequence {
    param(
        [Parameter(Mandatory = $true)][object]$Package,
        [Parameter(Mandatory = $true)][string]$MainAction,
        [Parameter(Mandatory = $true)][scriptblock]$PushMain,
        [Parameter(Mandatory = $true)][scriptblock]$PushSymbols
    )

    if ($MainAction -ceq 'Push') {
        & $PushMain $Package.mainPath
    }
    elseif ($MainAction -cne 'ReuseVerified') {
        throw "Publication main action is not executable: '$MainAction'."
    }
    & $PushSymbols $Package.symbolsPath
}

function Get-SharpProofRemoteMainPackageUrl {
    param(
        [Parameter(Mandatory = $true)][string]$BaseAddress,
        [Parameter(Mandatory = $true)][string]$PackageId,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $normalizedBaseAddress = Resolve-SharpProofPublicationHttpsDestination `
        -Value $BaseAddress `
        -Owner 'NuGet PackageBaseAddress'
    $normalizedId = $PackageId.ToLowerInvariant()
    $normalizedVersion = $Version.ToLowerInvariant()
    return (
        $normalizedBaseAddress.TrimEnd('/') + '/' +
        [Uri]::EscapeDataString($normalizedId) + '/' +
        [Uri]::EscapeDataString($normalizedVersion) + '/' +
        [Uri]::EscapeDataString(
            "$normalizedId.$normalizedVersion.nupkg"))
}

function Invoke-SharpProofMainPackagePreflight {
    param(
        [Parameter(Mandatory = $true)][object]$Package,
        [Parameter(Mandatory = $true)][string]$BaseAddress,
        [Parameter(Mandatory = $true)][scriptblock]$Get,
        [Parameter()][scriptblock]$CanReuseExisting
    )

    $remoteUrl = Get-SharpProofRemoteMainPackageUrl `
        -BaseAddress $BaseAddress `
        -PackageId $Package.packageId `
        -Version $Package.version
    $response = & $Get $remoteUrl 'Head' $null
    $status = [int]$response.StatusCode
    if ($status -eq 404) {
        return [pscustomobject][ordered]@{
            state = 'Absent'
            remoteUrl = $remoteUrl
            verifiedMainSha256 = $null
        }
    }
    if ($status -eq 200 -and
        $null -ne $CanReuseExisting -and
        [bool](& $CanReuseExisting)) {
        if ($Package.mainPath -isnot [string] -or
            -not (Test-Path -LiteralPath $Package.mainPath -PathType Leaf)) {
            throw 'A staged main package is required to verify existing remote bytes.'
        }
        $downloadRoot = Join-Path `
            ([IO.Path]::GetTempPath()) `
            ('sharpproof-remote-main-' + [Guid]::NewGuid().ToString('N'))
        $downloadPath = Join-Path $downloadRoot 'remote.nupkg'
        try {
            [IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
            & chmod 0700 -- $downloadRoot
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not protect the private remote package comparison directory.'
            }
            $download = & $Get $remoteUrl 'Get' $downloadPath
            if ([int]$download.StatusCode -ne 200 -or
                -not (Test-Path -LiteralPath $downloadPath -PathType Leaf)) {
                throw (
                    'NuGet PackageBaseAddress did not return the existing ' +
                    "package bytes (HTTP $([int]$download.StatusCode)).")
            }
            if (-not (Test-SharpProofFileByteEquality `
                    -ExpectedPath $Package.mainPath `
                    -ActualPath $downloadPath)) {
                throw (
                    "Existing NuGet package bytes do not match the staged " +
                    "$($Package.packageId) $($Package.version) artifact.")
            }
            $sha256 = (Get-FileHash `
                -LiteralPath $Package.mainPath `
                -Algorithm SHA256).Hash.ToLowerInvariant()
            return [pscustomobject][ordered]@{
                state = 'VerifiedPresent'
                remoteUrl = $remoteUrl
                verifiedMainSha256 = $sha256
            }
        }
        finally {
            if (Test-Path -LiteralPath $downloadRoot -PathType Container) {
                Remove-Item -LiteralPath $downloadRoot -Recurse -Force
            }
        }
    }
    throw (
        "NuGet PackageBaseAddress returned HTTP $status for " +
        "$($Package.packageId) $($Package.version); only exact bytes from " +
        'the verified NuGet.org feed may be reused.')
}
