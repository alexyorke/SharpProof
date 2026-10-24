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
        if ($env:SHARPPROOF_PRIVATE_PREVIEW -ceq 'true' -or
            $env:SHARPPROOF_CI_PACKAGE_VERSION -cnotmatch '\A0\.0\.0-ci\.[1-9][0-9]*\.[1-9][0-9]*\z') {
            throw 'CI validation versions require 0.0.0-ci.<run-id>.<attempt> and cannot be private release versions.'
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

    $requested = $env:SHARPPROOF_REPOSITORY_URL
    if ([string]::IsNullOrEmpty($requested) -or $requested -ceq 'https://github.com/alexyorke/SharpProof') {
        if ($env:SHARPPROOF_PRIVATE_PREVIEW -ceq 'true') {
            throw 'Private preview packages must link to their private distribution repository.'
        }
        return 'https://github.com/alexyorke/SharpProof'
    }
    if ($requested -cne 'https://github.com/alexyorke/SharpProof-preview' -or
        $env:SHARPPROOF_PRIVATE_PREVIEW -cne 'true' -or
        $env:GITHUB_REPOSITORY -cne 'alexyorke/SharpProof-preview' -or
        $env:SHARPPROOF_SOURCE_REPOSITORY -cne 'alexyorke/SharpProof' -or
        $env:SHARPPROOF_SOURCE_COMMIT -cnotmatch '\A[a-f0-9]{40}\z' -or
        $env:SHARPPROOF_WORKFLOW_COMMIT -cnotmatch '\A[a-f0-9]{40}\z') {
        throw 'Private repository metadata requires the explicit private preview workflow and bound source identities.'
    }
    return $requested
}

Export-ModuleMember -Function Get-SharpProofEffectivePackageVersion, Get-SharpProofExpectedRepositoryUrl
