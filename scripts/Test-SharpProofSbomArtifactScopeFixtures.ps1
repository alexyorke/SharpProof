[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'canonical',
        'missing-symbol',
        'extra-symbol',
        'swapped-role',
        'symbol-checksum',
        'fabricated-symbol-row',
        'broad-workflow-glob',
        'symbol-workflow-glob',
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
    $mainHash = ('a' + $id.Length) * 32
    $mainHash = $mainHash.Substring(0, 64)
    $symbolHash = ('b' + $id.Length) * 32
    $symbolHash = $symbolHash.Substring(0, 64)
    $artifacts.Add([pscustomobject][ordered]@{
        fileName = "$id.$version.nupkg"
        kind = 'package'
        packageId = $id
        sha256 = $mainHash
    })
    $artifacts.Add([pscustomobject][ordered]@{
        fileName = "$id.$version.snupkg"
        kind = 'symbols'
        packageId = $id
        sha256 = $symbolHash
    })
    $spdxId = Get-SharpProofDependencySpdxId -Name $id
    $packages.Add([pscustomobject][ordered]@{
        name = $id
        SPDXID = $spdxId
        versionInfo = $version
        checksums = @([pscustomobject][ordered]@{
            algorithm = 'SHA256'
            checksumValue = $mainHash
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
    'symbol-checksum' {
        $packages[0].checksums[0].checksumValue = $artifacts[1].sha256
    }
    'fabricated-symbol-row' {
        $copy = $packages[0].PSObject.Copy()
        $copy.checksums = @([pscustomobject][ordered]@{
            algorithm = 'SHA256'
            checksumValue = $artifacts[1].sha256
        })
        $packages.Add($copy)
        $describes.Add([string]$copy.SPDXID)
    }
    'broad-workflow-glob' {
        $workflow = $workflow.Replace('nupkgs/*.nupkg', 'nupkgs/*.*nupkg')
    }
    'symbol-workflow-glob' {
        $workflow = $workflow.Replace('nupkgs/*.nupkg', 'nupkgs/*.snupkg')
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
Test-SharpProofSbomAttestationWorkflow -Workflow $workflow
Write-Host "SBOM artifact scope fixture passed: $Mutation"
