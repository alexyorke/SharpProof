[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Test-SharpProofThirdPartyInventory.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-inventory-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$inventoryPath = Join-Path $temporary 'inventory.json'
$nativeBytes = [Text.Encoding]::UTF8.GetBytes('synthetic-native-payload')
$nativeDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($nativeBytes)).ToLowerInvariant()

function New-FixturePackage([string]$Id, [hashtable]$Files) {
    $path = Join-Path $temporary "$Id.1.0.0.nupkg"
    if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $metadata = '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>' + $Id + '</id><version>1.0.0</version></metadata></package>'
        $contents = @{ "$Id.nuspec" = [Text.Encoding]::UTF8.GetBytes($metadata) }
        foreach ($name in $Files.Keys) { $contents[$name] = $Files[$name] }
        foreach ($name in $contents.Keys) {
            $stream = $archive.CreateEntry($name).Open()
            try { $stream.Write([byte[]]$contents[$name]) }
            finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}

function New-FixtureInventory {
    return @{
        schemaVersion = 1
        packages = @{
            SharpProof = @()
            'SharpProof.Attributes' = @()
            'SharpProof.Verifier' = @(
                @{ id = 'Vendor'; version = '1'; license = 'MIT'; entries = @('tools/Vendor.dll') },
                @{ id = 'StaticRuntime'; version = '2'; license = 'BSD-2-Clause'; linkage = 'static'; entries = @('tools/Vendor.dll') },
                @{ id = 'Z3'; version = '4.12.2'; license = 'MIT'; entries = @('tools/native/win-x64/libz3.dll'); entrySha256 = @(@{ path = 'tools/native/win-x64/libz3.dll'; sha256 = $nativeDigest }) }
            )
        }
    }
}

function Assert-Rejected([hashtable]$Inventory, [string]$Expected) {
    $Inventory | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $inventoryPath -Encoding utf8NoBOM
    $rejected = $false
    try { & $validator -PackageSource $temporary -InventoryPath $inventoryPath }
    catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Invalid inventory was accepted: $Expected" }
}

try {
    New-FixturePackage 'SharpProof' @{}
    New-FixturePackage 'SharpProof.Attributes' @{ 'lib/SharpProof.Attributes.dll' = $nativeBytes }
    $verifierFiles = @{ 'tools/Vendor.dll' = $nativeBytes; 'tools/native/win-x64/libz3.dll' = $nativeBytes; 'tools/SharpProof.Worker.dll' = $nativeBytes }
    New-FixturePackage 'SharpProof.Verifier' $verifierFiles
    $valid = New-FixtureInventory
    $valid | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $inventoryPath -Encoding utf8NoBOM
    & $validator -PackageSource $temporary -InventoryPath $inventoryPath

    $invalid = New-FixtureInventory
    $invalid.packages['SharpProof.Verifier'][0].entries = @('tools/missing.dll')
    Assert-Rejected $invalid 'declares missing package entry'
    $invalid = New-FixtureInventory
    $invalid.packages['SharpProof.Verifier'][2].entrySha256[0].sha256 = '0' * 64
    Assert-Rejected $invalid 'SHA-256 mismatch'
    $invalid = New-FixtureInventory
    $invalid.packages['SharpProof.Verifier'][0].entries = @('../Vendor.dll')
    Assert-Rejected $invalid 'Unsafe third-party inventory path'
    $invalid = New-FixtureInventory
    $invalid.packages['SharpProof.Verifier'][0].entries = @('tools/Vendor.dll', 'tools/Vendor.dll')
    Assert-Rejected $invalid 'repeats entry'
    $invalid = New-FixtureInventory
    $invalid.packages['SharpProof.Verifier'][2].entrySha256[0].path = 'tools/Vendor.dll'
    Assert-Rejected $invalid 'invalid or foreign entry digest'
    $invalid = New-FixtureInventory
    $invalid.packages['SharpProof.Verifier'][2].Remove('entrySha256')
    Assert-Rejected $invalid 'lacks a declared SHA-256 pin'
    $verifierFiles['tools/Orphan.dll'] = $nativeBytes
    New-FixturePackage 'SharpProof.Verifier' $verifierFiles
    Assert-Rejected (New-FixtureInventory) 'unlisted: [tools/Orphan.dll]'
    Write-Host 'Inventory regressions passed: composite DLL, missing/unsafe/duplicate entries, altered/foreign/missing digest and orphan binary.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::Ordinal) -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('sharpproof-inventory-', [StringComparison]::Ordinal)) {
        throw 'Refusing to remove an unexpected inventory fixture directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
