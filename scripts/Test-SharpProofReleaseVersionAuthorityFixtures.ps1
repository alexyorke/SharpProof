[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'canonical',
        'foreign-matching',
        'case-only-prerelease',
        'mixed-package',
        'stale-manifest',
        'stale-plan')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')

$expected = Get-SharpProofReleaseVersion -RepositoryRoot $repositoryRoot
$versions = @($expected) * 6
$manifestVersion = $expected
$planVersion = $expected
$identity = Get-SharpProofReleaseVersionAuthority -RepositoryRoot $repositoryRoot
switch ($Mutation) {
    'foreign-matching' {
        $versions = @('9.9.9-preview.9') * 6
        $manifestVersion = '9.9.9-preview.9'
        $planVersion = '9.9.9-preview.9'
    }
    'case-only-prerelease' { $versions[0] = $expected.ToUpperInvariant() }
    'mixed-package' { $versions[5] = '9.9.9-preview.9' }
    'stale-manifest' { $manifestVersion = '9.9.9-preview.9' }
    'stale-plan' { $planVersion = '9.9.9-preview.9' }
}

Test-SharpProofReleaseVersionSet `
    -ExpectedVersion $expected `
    -Versions $versions `
    -Owner 'fixture packages'
Test-SharpProofReleaseVersion `
    -ExpectedVersion $expected -ActualVersion $manifestVersion `
    -Owner 'fixture manifest'
Test-SharpProofReleaseVersion `
    -ExpectedVersion $expected -ActualVersion $planVersion `
    -Owner 'fixture plan'
Test-SharpProofReleaseVersionAuthority `
    -RepositoryRoot $repositoryRoot -Authority $identity
Write-Host "Release version fixture passed: $Mutation"
