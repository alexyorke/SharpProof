[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-release-fixture-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $temporary 'packages'
$evidence = Join-Path $temporary 'evidence'
$inventoryPath = Join-Path $temporary 'inventory.json'
[IO.Directory]::CreateDirectory($source) | Out-Null
$commit = 'a' * 40
$native = [Collections.Immutable.ImmutableArray]::CreateRange[byte]([Text.Encoding]::UTF8.GetBytes('fixture-native'))
$nativeHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]@($native))).ToLowerInvariant()

function New-Package([string]$Id, [string]$Extension, [string]$Vendor = 'fixture-vendor', [string]$Directory = $source) {
    $path = Join-Path $Directory "$Id.1.0.0.$Extension"
    if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $metadata = '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>' + $Id + '</id><version>1.0.0</version><repository type="git" url="https://github.com/alexyorke/SharpProof" commit="' + $commit + '" /></metadata></package>'
        $contents = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, Collections.Immutable.ImmutableArray[byte]]([StringComparer]::OrdinalIgnoreCase)
        $contents.Add("$Id.nuspec", [Collections.Immutable.ImmutableArray]::CreateRange[byte]([Text.Encoding]::UTF8.GetBytes($metadata)))
        if ($Id -ceq 'SharpProof.Verifier' -and $Extension -ceq 'nupkg') {
            $contents.Add('tools/Vendor.dll', [Collections.Immutable.ImmutableArray]::CreateRange[byte]([Text.Encoding]::UTF8.GetBytes($Vendor)))
            $contents.Add('tools/native/win-x64/libz3.dll', $native)
        }
        $payloads = $contents.ToImmutable()
        foreach ($name in $payloads.Keys) {
            $stream = $archive.CreateEntry($name).Open()
            try { $stream.Write([byte[]]@($payloads[$name])) }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

function Assert-Rejected([scriptblock]$Operation, [string]$Expected) {
    $rejected = $false
    try { & $Operation }
    catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Invalid release evidence accepted: $Expected" }
}

try {
    foreach ($id in [Collections.Immutable.ImmutableArray]::Create[string]('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier')) { New-Package $id 'nupkg' }
    foreach ($id in [Collections.Immutable.ImmutableArray]::Create[string]('SharpProof.Attributes', 'SharpProof.Verifier')) { New-Package $id 'snupkg' }
    @{
        schemaVersion = 1
        packages      = @{
            SharpProof = @(); 'SharpProof.Attributes' = @()
            'SharpProof.Verifier' = @(
                @{ id = 'Vendor'; version = '1'; license = 'MIT'; entries = @('tools/Vendor.dll') },
                @{ id = 'Runtime'; version = '2'; license = 'BSD-2-Clause'; linkage = 'static'; entries = @('tools/Vendor.dll') },
                @{ id = 'Z3'; version = '4.12.2'; license = 'MIT'; entries = @('tools/native/win-x64/libz3.dll'); entrySha256 = @(@{ path = 'tools/native/win-x64/libz3.dll'; sha256 = $nativeHash }) }
            )
        }
    } | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $inventoryPath -Encoding utf8NoBOM
    $arguments = ([ordered]@{ PackageSource = $source; RepositoryCommit = $commit; InventoryPath = $inventoryPath }).AsReadOnly()
    & (Join-Path $PSScriptRoot 'New-SharpProofReleaseEvidence.ps1') @arguments -OutputDirectory $evidence -Timestamp '2026-01-01T00:00:00Z'
    & (Join-Path $PSScriptRoot 'Test-SharpProofReleaseEvidence.ps1') @arguments -EvidenceDirectory $evidence
    $toolSource = Join-Path $temporary 'tool'
    [IO.Directory]::CreateDirectory($toolSource) | Out-Null
    New-Package 'SharpProof.Tool' 'nupkg' 'unused' $toolSource
    & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $toolSource
    & (Join-Path $PSScriptRoot 'New-SharpProofToolReleaseEvidence.ps1') -PackageSource $toolSource -OutputDirectory $evidence -RepositoryCommit $commit
    $tool = Get-Content -LiteralPath (Join-Path $evidence 'tool-release-manifest.json') -Raw | ConvertFrom-Json
    if ($tool.packageId -cne 'SharpProof.Tool' -or $tool.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Separate tool evidence is incomplete.' }
    $extraTool = Join-Path $toolSource 'SharpProof.Tool.duplicate.nupkg'
    Copy-Item -LiteralPath (Join-Path $toolSource 'SharpProof.Tool.1.0.0.nupkg') -Destination $extraTool
    Assert-Rejected { & (Join-Path $PSScriptRoot 'New-SharpProofToolReleaseEvidence.ps1') -PackageSource $toolSource -OutputDirectory $evidence -RepositoryCommit $commit } 'exactly one separately qualified nupkg'
    Remove-Item -LiteralPath $extraTool
    $extraBinary = Join-Path $toolSource 'Unexpected.dll'
    [IO.File]::WriteAllBytes($extraBinary, [byte[]]@($native))
    Assert-Rejected { & (Join-Path $PSScriptRoot 'New-SharpProofToolReleaseEvidence.ps1') -PackageSource $toolSource -OutputDirectory $evidence -RepositoryCommit $commit } 'Unexpected file'
    Remove-Item -LiteralPath $extraBinary
    Assert-Rejected { & (Join-Path $PSScriptRoot 'New-SharpProofToolReleaseEvidence.ps1') -PackageSource $toolSource -OutputDirectory $evidence -RepositoryCommit ('b' * 40) } 'differs from the qualified release'
    $sbomPath = Join-Path $evidence 'sbom.spdx.json'
    $original = [IO.File]::ReadAllText($sbomPath)
    $sbom = $original | ConvertFrom-Json
    if ($sbom.spdxVersion -cne 'SPDX-2.3' -or $sbom.packages.Count -ne 8 -or $sbom.files.Count -ne 2 -or
        @($sbom.relationships | Where-Object relationshipType -CEQ 'STATIC_LINK').Count -ne 1) {
        throw 'The SBOM lost archive/component hashes or composite static-link relationships.'
    }
    $sbom.packages = @($sbom.packages | Select-Object -SkipLast 1)
    $sbom | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $sbomPath -Encoding utf8NoBOM
    Assert-Rejected { & (Join-Path $PSScriptRoot 'Test-SharpProofReleaseEvidence.ps1') @arguments -EvidenceDirectory $evidence } 'differs from the actual qualified package payload'
    [IO.File]::WriteAllText($sbomPath, $original)
    New-Package 'SharpProof.Verifier' 'nupkg' 'altered-vendor'
    Assert-Rejected { & (Join-Path $PSScriptRoot 'Test-SharpProofReleaseEvidence.ps1') @arguments -EvidenceDirectory $evidence } 'differs from the actual qualified package payload'
    Assert-Rejected { & (Join-Path $PSScriptRoot 'New-SharpProofReleaseEvidence.ps1') -PackageSource $source -InventoryPath $inventoryPath -OutputDirectory $evidence -RepositoryCommit ('b' * 40) } 'repository commit differs'
    Remove-Item -LiteralPath (Join-Path $source 'SharpProof.Attributes.1.0.0.snupkg')
    Assert-Rejected { & (Join-Path $PSScriptRoot 'New-SharpProofReleaseEvidence.ps1') @arguments -OutputDirectory $evidence } 'exactly three main and two symbol packages'
    Write-Host 'Release evidence regressions passed: complete archive/component graph, composite binary, omitted SBOM component, changed archive bytes, wrong commit and missing symbols.'
    # Pinned actions/attest derives SPDX predicates from the actual document's
    # spdxVersion, e.g. SPDX-2.3 -> https://spdx.dev/Document/v2.3.
    $expectedPredicate = 'https://spdx.dev/Document/v' + ([string]$sbom.spdxVersion).Substring('SPDX-'.Length)
    & bash (Join-Path $PSScriptRoot 'Test-SharpProofReleaseAttestationsRegression.sh') $expectedPredicate
    if ($LASTEXITCODE -ne 0) { throw 'Attestation policy regressions failed.' }
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
