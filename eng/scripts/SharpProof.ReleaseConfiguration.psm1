Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SharpProofEffectivePackageVersion {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    [xml]$properties = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/Versions.props') -Raw
    $versionNode = $properties.SelectSingleNode('/Project/PropertyGroup/SharpProofPackageVersion')
    $prefixNode = $properties.SelectSingleNode('/Project/PropertyGroup/SharpProofVersionPrefix')
    if ($null -eq $versionNode -or $null -eq $prefixNode) {
        throw 'Release version authority is missing.'
    }
    $version = $versionNode.InnerText.Replace('$(SharpProofVersionPrefix)', $prefixNode.InnerText)
    if (-not [string]::IsNullOrEmpty($env:SHARPPROOF_CI_PACKAGE_VERSION)) {
        if ($env:SHARPPROOF_CI_PACKAGE_VERSION -cnotmatch '\A0\.0\.0-ci\.[1-9][0-9]*\.[1-9][0-9]*\z') {
            throw 'CI validation versions require 0.0.0-ci.<run-id>.<attempt>.'
        }
        $version = $env:SHARPPROOF_CI_PACKAGE_VERSION
    }
    if ($version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\z') {
        throw 'Invalid effective package version.'
    }
    return $version
}

function Get-SharpProofExpectedRepositoryUrl {
    param()
    return 'https://github.com/alexyorke/SharpProof'
}

Export-ModuleMember -Function Get-SharpProofEffectivePackageVersion, Get-SharpProofExpectedRepositoryUrl
