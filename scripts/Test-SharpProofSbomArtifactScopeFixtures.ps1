[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'canonical',
        'missing-symbol',
        'extra-symbol',
        'swapped-role',
        'broad-workflow-glob',
        'symbol-workflow-glob',
        'purl-substituted',
        'purl-duplicate',
        'purl-omitted',
        'purl-encoded',
        'purl-case',
        'purl-extra-field',
        'third-party-purl',
        'checked-in-workflow')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Test-SharpProofPackageDependencies.ps1')

$version = '1.0.0-preview.1'
$ids = @('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier')
$artifacts = [Collections.Generic.List[object]]::new()
$packages = [Collections.Generic.List[object]]::new()
$describes = [Collections.Generic.List[string]]::new()
foreach ($id in $ids) {
    $artifacts.Add([pscustomobject][ordered]@{
        fileName = "$id.$version.nupkg"
        kind = 'package'
        packageId = $id
    })
    $artifacts.Add([pscustomobject][ordered]@{
        fileName = "$id.$version.snupkg"
        kind = 'symbols'
        packageId = $id
    })
    $spdxId = Get-SharpProofDependencySpdxId -Name $id
    $packages.Add([pscustomobject][ordered]@{
        name = $id
        SPDXID = $spdxId
        versionInfo = $version
        externalRefs = @([pscustomobject][ordered]@{
            referenceCategory = 'PACKAGE-MANAGER'
            referenceType = 'purl'
            referenceLocator = Get-SharpProofNuGetPurl `
                -Name $id `
                -Version $version
        })
    })
    $describes.Add($spdxId)
}

$workflow = @'
jobs:
  attest:
    steps:
      - name: Attest package SBOM
        uses: actions/attest@example
        with:
          subject-path: nupkgs/*.nupkg
          sbom-path: nupkgs/SharpProof.spdx.json
'@
switch ($Mutation) {
    'missing-symbol' { $artifacts.RemoveAt($artifacts.Count - 1) }
    'extra-symbol' { $artifacts.Add($artifacts[1].PSObject.Copy()) }
    'swapped-role' { $artifacts[1].kind = 'package' }
    'broad-workflow-glob' {
        $workflow = $workflow.Replace('nupkgs/*.nupkg', 'nupkgs/*.*nupkg')
    }
    'symbol-workflow-glob' {
        $workflow = $workflow.Replace('nupkgs/*.nupkg', 'nupkgs/*.snupkg')
    }
    'purl-substituted' {
        $packages[0].externalRefs[0].referenceLocator =
            'pkg:nuget/Fabricated.Package@99.0.0'
    }
    'purl-duplicate' {
        $packages[0].externalRefs = @(
            $packages[0].externalRefs[0],
            $packages[0].externalRefs[0].PSObject.Copy())
    }
    'purl-omitted' { $packages[0].externalRefs = @() }
    'purl-encoded' {
        $packages[0].externalRefs[0].referenceLocator =
            'pkg:nuget/Sharp%50roof@1.0.0-preview.1'
    }
    'purl-case' {
        $packages[0].externalRefs[0].referenceType = 'PURL'
    }
    'purl-extra-field' {
        $packages[0].externalRefs[0] |
            Add-Member -NotePropertyName comment -NotePropertyValue 'decoy'
    }
    'third-party-purl' {
        $thirdParty = [pscustomobject][ordered]@{
            name = 'Microsoft.Z3'
            versionInfo = '4.12.2'
            externalRefs = @([pscustomobject][ordered]@{
                referenceCategory = 'PACKAGE-MANAGER'
                referenceType = 'purl'
                referenceLocator = 'pkg:nuget/Fabricated@4.12.2'
            })
        }
        Test-SharpProofSbomPackageUrls -SbomPackages @($thirdParty)
    }
    'checked-in-workflow' {
        $workflow = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot '.github/workflows/package-consumers.yml') -Raw
    }
}

Test-SharpProofSbomArtifactScope `
    -Artifacts @($artifacts) `
    -SbomPackages @($packages) `
    -DocumentDescribes @($describes) `
    -FirstPartyPackageIds $ids `
    -PackageVersion $version
Test-SharpProofSbomPackageUrls -SbomPackages @($packages)
Test-SharpProofSbomAttestationWorkflow -Workflow $workflow
Write-Host "SBOM artifact scope fixture passed: $Mutation"
