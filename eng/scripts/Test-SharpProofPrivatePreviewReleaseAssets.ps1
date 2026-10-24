[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Private release asset regressions require the canonical container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PrivatePreviewReleaseAssets.psm1') -Force
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-release-assets-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$checks = 0
function Assert-AssetRejection([scriptblock]$Action, [string]$Expected) {
    $rejected = $false
    try { & $Action } catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Private release asset boundary was not enforced.' }
    $script:checks++
}
try {
    $original = Join-Path $scratch 'original.nupkg'
    $empty = Join-Path $scratch 'empty.txt'
    $boundary = Join-Path $scratch 'evidence.tar'
    [IO.File]::WriteAllText($original, 'Exact qualified bytes must remain unchanged.', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes($empty, [byte[]]::new(0))
    $originalHash = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
    $originalTime = (Get-Item -LiteralPath $original).LastWriteTimeUtc
    $stream = [IO.File]::Open($boundary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        # Sparse lengths exercise the real 2 GiB boundary without exporting,
        # allocating, or hashing a multi-gigabyte qualified image archive.
        $stream.SetLength(2147483647L)
        $stream.Flush()
        Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths @($original, $boundary, $empty)
        $checks++
        $stream.SetLength(2147483648L)
        $stream.Flush()
        Assert-AssetRejection { Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths @($boundary) } 'strictly under 2 GiB: evidence.tar'
        $stream.SetLength(2147483649L)
        $stream.Flush()
        Assert-AssetRejection { Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths @($original, $empty, $boundary) } 'strictly under 2 GiB: evidence.tar'
        $stream.SetLength(0)
        $stream.Flush()
    }
    finally { $stream.Dispose() }
    Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths @($original, $empty, $boundary)
    $checks++
    if ((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -cne $originalHash -or (Get-Item -LiteralPath $original).LastWriteTimeUtc -ne $originalTime) { throw 'Asset preflight changed the qualified bytes or write time.' }
    $checks++
    Assert-AssetRejection { Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths @($scratch) } 'regular files without links'
    Assert-AssetRejection { Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths @((Join-Path $scratch 'missing.tar')) } 'missing.tar'
    $link = Join-Path $scratch 'linked.tar'
    [void](New-Item -ItemType SymbolicLink -Path $link -Target $original)
    Assert-AssetRejection { Assert-SharpProofPrivatePreviewReleaseAssetSizes -Paths @($link) } 'regular files without links'
    Write-Host "Private release asset regressions passed: $checks actual file-size, strict 2 GiB boundary, and unchanged-byte controls. Sparse fixtures only; no publication, signing, or image export."
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-release-assets-[a-f0-9]{32}\z') { throw 'Refusing unsafe release asset fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
