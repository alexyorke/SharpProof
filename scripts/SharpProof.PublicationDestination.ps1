if (-not (Get-Command Test-SharpProofReleaseVersionSyntax `
        -CommandType Function -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
}

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
    $json = ConvertTo-Json `
        -InputObject ([object[]]$entries) `
        -Compress
    $digest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes($json))).ToLowerInvariant()
    return [pscustomobject][ordered]@{
        path = $canonical
        fileIdentity = $directoryIdentity
        entryCount = $entries.Count
        entriesSha256 = $digest
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
        try {
            $archive = [IO.Compression.ZipFile]::OpenRead($file.FullName)
        }
        catch {
            throw "Fixture archive is malformed: '$($file.FullName)'."
        }
        try {
            $nuspecEntries = @($archive.Entries | Where-Object {
                $_.FullName.EndsWith(
                    '.nuspec', [StringComparison]::OrdinalIgnoreCase)
            })
            if ($nuspecEntries.Count -ne 1) {
                throw "Fixture archive must contain exactly one nuspec: '$($file.FullName)'."
            }
            $reader = [IO.StreamReader]::new($nuspecEntries[0].Open())
            try { [xml]$nuspec = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
            $namespaces = [Xml.XmlNamespaceManager]::new($nuspec.NameTable)
            $namespaces.AddNamespace(
                'n', [string]$nuspec.DocumentElement.NamespaceURI)
            $metadata = $nuspec.SelectSingleNode(
                '/n:package/n:metadata', $namespaces)
            if ($null -eq $metadata) {
                throw "Fixture archive nuspec identity is incomplete: '$($file.FullName)'."
            }
            $ids = @($metadata.SelectNodes('n:id', $namespaces))
            $versions = @($metadata.SelectNodes('n:version', $namespaces))
            if ($ids.Count -ne 1 -or $versions.Count -ne 1) {
                throw "Fixture archive nuspec identity is incomplete: '$($file.FullName)'."
            }
            $id = [string]$ids[0].InnerText
            $version = [string]$versions[0].InnerText
            if ($id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or
                -not (Test-SharpProofReleaseVersionSyntax `
                    -Version $version)) {
                throw "Fixture archive nuspec identity is invalid: '$($file.FullName)'."
            }
            $hasDll = @($archive.Entries | Where-Object {
                $_.FullName.EndsWith(
                    '.dll', [StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
            $hasPdb = @($archive.Entries | Where-Object {
                $_.FullName.EndsWith(
                    '.pdb', [StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
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

    $entries = @($Catalog | Where-Object { $null -ne $_ })
    $matching = @($entries | Where-Object {
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
    $actualNames = @($Authority.PSObject.Properties.Name | Sort-Object)
    $expectedNames = @($expected.PSObject.Properties.Name | Sort-Object)
    if (($actualNames -join "`0") -cne ($expectedNames -join "`0") -or
        ($Authority | ConvertTo-Json -Compress -Depth 5) -cne
            ($expected | ConvertTo-Json -Compress -Depth 5)) {
        throw 'Publication destination authority is invalid.'
    }
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
        $MainState -cnotin @('Absent', 'Unchecked', 'ExactPresent', 'Collision')) {
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
                mainAction = switch ($MainState) {
                    'Absent' { 'Push' }
                    'Unchecked' { 'PreflightThenPush' }
                    'ExactPresent' { 'None' }
                    'Collision' { 'Collision' }
                }
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
    $actualNames = @($Authority.PSObject.Properties.Name)
    $expectedNames = @($expected.PSObject.Properties.Name)
    if (($actualNames -join "`0") -cne ($expectedNames -join "`0") -or
        ($Authority | ConvertTo-Json -Compress) -cne
            ($expected | ConvertTo-Json -Compress)) {
        throw 'Publication action authority is invalid.'
    }
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
        [Parameter(Mandatory = $true)][scriptblock]$Get
    )

    $remoteUrl = Get-SharpProofRemoteMainPackageUrl `
        -BaseAddress $BaseAddress `
        -PackageId $Package.packageId `
        -Version $Package.version
    $temporaryPath = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::Delete($temporaryPath)
        $response = & $Get $remoteUrl $temporaryPath
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
        $localPath = $Package.PSObject.Properties['mainPath']
        if ($null -eq $localPath -or
            -not [IO.File]::Exists([string]$localPath.Value) -or
            -not [IO.File]::Exists($temporaryPath)) {
            throw (
                "Remote main package identity cannot be compared for " +
                "$($Package.packageId) $($Package.version).")
        }
        $localHash = (Get-FileHash `
                -LiteralPath ([string]$localPath.Value) `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        $remoteHash = (Get-FileHash `
                -LiteralPath $temporaryPath `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        return [pscustomobject][ordered]@{
            state = if ($localHash -ceq $remoteHash) {
                'ExactPresent'
            }
            else {
                'Collision'
            }
            remoteUrl = $remoteUrl
        }
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}
