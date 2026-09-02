function Test-SharpProofReleaseVersionSyntax {
    param([Parameter(Mandatory = $true)][string]$Version)

    return $Version -cmatch (
        '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.' +
        '(0|[1-9][0-9]*)(?:-(?:(?:0|[1-9][0-9]*)|' +
        '(?:[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))' +
        '(?:\.(?:(?:0|[1-9][0-9]*)|' +
        '(?:[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)))*)?\z')
}

function Get-SharpProofReleaseVersion {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $path = Join-Path $root 'SharpProof.Release.props'
    [xml]$document = Get-Content -LiteralPath $path -Raw -ErrorAction Stop
    $prefixes = @($document.SelectNodes(
        '/Project/PropertyGroup/SharpProofVersionPrefix'))
    $versions = @($document.SelectNodes(
        '/Project/PropertyGroup/SharpProofPackageVersion'))
    if ($prefixes.Count -ne 1 -or $versions.Count -ne 1) {
        throw 'SharpProof.Release.props must contain one version authority.'
    }
    $prefix = [string]$prefixes[0].InnerText
    $template = [string]$versions[0].InnerText
    $version = $template.Replace('$(SharpProofVersionPrefix)', $prefix)
    if ([string]::IsNullOrWhiteSpace($prefix) -or
        $template.IndexOf('$(SharpProofVersionPrefix)',
            [StringComparison]::Ordinal) -lt 0 -or
        $version.Contains('$(', [StringComparison]::Ordinal) -or
        -not (Test-SharpProofReleaseVersionSyntax -Version $version)) {
        throw 'SharpProof.Release.props has an invalid package version.'
    }
    return $version
}

function Get-SharpProofReleaseVersionAuthority {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        path = 'SharpProof.Release.props'
        property = 'SharpProofPackageVersion'
        version = Get-SharpProofReleaseVersion -RepositoryRoot $root
    }
}

function Test-SharpProofReleaseVersion {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string]$ActualVersion,
        [Parameter(Mandatory = $true)][string]$Owner
    )
    if (-not $ActualVersion.Equals(
            $ExpectedVersion,
            [StringComparison]::Ordinal)) {
        throw "$Owner version '$ActualVersion' does not equal release authority '$ExpectedVersion'."
    }
}

function Test-SharpProofReleaseVersionSet {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedVersion,
        [Parameter(Mandatory = $true)][string[]]$Versions,
        [Parameter(Mandatory = $true)][string]$Owner
    )
    if ($Versions.Count -eq 0) {
        throw "$Owner version set is empty."
    }
    foreach ($version in $Versions) {
        Test-SharpProofReleaseVersion `
            -ExpectedVersion $ExpectedVersion `
            -ActualVersion $version `
            -Owner $Owner
    }
}

function Test-SharpProofReleaseVersionAuthority {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][object]$Authority
    )
    $expected = Get-SharpProofReleaseVersionAuthority `
        -RepositoryRoot $RepositoryRoot
    $actualProperties = @($Authority.PSObject.Properties.Name | Sort-Object)
    $expectedProperties = @($expected.PSObject.Properties.Name | Sort-Object)
    if (($actualProperties -join "`0") -cne
            ($expectedProperties -join "`0") -or
        [int]$Authority.schemaVersion -ne 1 -or
        [string]$Authority.path -cne [string]$expected.path -or
        [string]$Authority.property -cne [string]$expected.property -or
        [string]$Authority.version -cne [string]$expected.version) {
        throw 'Release version authority evidence is invalid.'
    }
}
