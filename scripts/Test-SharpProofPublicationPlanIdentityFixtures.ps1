[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('canonical','changed-symbol','stale-manifest','stale-sbom',
        'stale-checksums','missing-identity','duplicate-identity','two-bundle')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PublicationPlanIdentity.psm1') -Force
$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-plan-identity-' + [Guid]::NewGuid().ToString('N'))
$version = '1.0.0-preview.1'
$commit = '0123456789abcdef0123456789abcdef01234567'
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $packages = [Collections.Generic.List[object]]::new()
    $artifactRows = [Collections.Generic.List[object]]::new()
    foreach ($id in @('SharpProof.Attributes','SharpProof','SharpProof.Verifier')) {
        $main = Join-Path $root "$id.$version.nupkg"
        $symbols = Join-Path $root "$id.$version.snupkg"
        [IO.File]::WriteAllText($main, "main:$id")
        [IO.File]::WriteAllText($symbols, "symbols:$id")
        $packages.Add([pscustomobject]@{ mainPath = $main; symbolsPath = $symbols })
        foreach ($path in @($main,$symbols)) {
            $file = Get-Item -LiteralPath $path
            $artifactRows.Add([pscustomobject][ordered]@{
                fileName = $file.Name
                bytes = [int64]$file.Length
                sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
            })
        }
    }
    $sbom = Join-Path $root 'SharpProof.spdx.json'
    [IO.File]::WriteAllText($sbom, '{"spdxVersion":"SPDX-2.3"}')
    $sbomFile = Get-Item $sbom
    $artifactRows.Add([pscustomobject][ordered]@{
        fileName = $sbomFile.Name
        bytes = [int64]$sbomFile.Length
        sha256 = (Get-FileHash $sbom -Algorithm SHA256).Hash.ToLowerInvariant()
    })
    $manifestPath = Join-Path $root 'SharpProof.release.json'
    $manifest = [pscustomobject][ordered]@{
        packageVersion = $version
        repository = [pscustomobject][ordered]@{ commit = $commit }
        artifacts = @($artifactRows)
    }
    [IO.File]::WriteAllText(
        $manifestPath,
        (($manifest | ConvertTo-Json -Depth 5) -replace "`r`n","`n") + "`n")
    $sums = Join-Path $root 'SHA256SUMS'
    $sumText = [Text.StringBuilder]::new()
    foreach ($row in $artifactRows) {
        [void]$sumText.Append("$($row.sha256)  $($row.fileName)`n")
    }
    [IO.File]::WriteAllText($sums, $sumText.ToString())
    $identities = @(New-SharpProofPublicationPlanIdentities `
        -Packages @($packages) -Directory $root -Version $version `
        -RepositoryCommit $commit)
    $plan = [pscustomobject][ordered]@{
        schemaVersion = 2
        packageVersion = $version
        repositoryCommit = $commit
        artifacts = $identities
    }
    switch ($Mutation) {
        'changed-symbol' { [IO.File]::AppendAllText($packages[0].symbolsPath, 'changed') }
        'stale-manifest' { [IO.File]::AppendAllText($manifestPath, 'changed') }
        'stale-sbom' { [IO.File]::AppendAllText($sbom, 'changed') }
        'stale-checksums' { [IO.File]::AppendAllText($sums, 'changed') }
        'missing-identity' { $plan.artifacts = @($plan.artifacts | Select-Object -Skip 1) }
        'duplicate-identity' { $plan.artifacts[1].path = $plan.artifacts[0].path }
        'two-bundle' {
            $first = ($plan.artifacts | ConvertTo-Json -Depth 4)
            [IO.File]::AppendAllText($packages[0].mainPath, 'other')
            $second = @(New-SharpProofPublicationPlanIdentities `
                -Packages @($packages) -Directory $root -Version $version `
                -RepositoryCommit $commit) | ConvertTo-Json -Depth 4
            if ($first -ceq $second) { throw 'Distinct bundle bytes produced one plan identity.' }
            Write-Host 'Publication plan identity fixture passed: two-bundle'
            return
        }
    }
    Test-SharpProofPublicationPlanIdentity -Plan $plan
    Write-Host "Publication plan identity fixture passed: $Mutation"
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
