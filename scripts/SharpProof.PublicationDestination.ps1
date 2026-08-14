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
    $json = $entries | ConvertTo-Json -Compress
    $digest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData(
            [Text.Encoding]::UTF8.GetBytes($json))).ToLowerInvariant()
    return [pscustomobject][ordered]@{
        path = $canonical
        fileIdentity = $directoryIdentity
        entryCount = $entries.Count
        entriesSha256 = $digest
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
