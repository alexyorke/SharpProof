[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Release security tests require the canonical Linux container.'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('release-security-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
try {
    $packagePath = Join-Path $testRoot 'Fixture.1.0.0.nupkg'
    $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('Fixture.nuspec')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>Fixture</id><version>1.0.0</version></metadata></package>') }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
    & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $testRoot
    $manifest = [IO.File]::ReadAllText((Join-Path $testRoot 'packages.tsv'))
    if ($manifest -notmatch '^fixture\t1\.0\.0\tFixture\.1\.0\.0\.nupkg\t[a-f0-9]{128}\n$') {
        throw 'Package manifest did not preserve the nuspec identity and qualified hash.'
    }
    & bash (Join-Path $PSScriptRoot 'Test-SharpProofReleaseSecurity.sh') $testRoot
    if ($LASTEXITCODE -ne 0) { throw 'Release security regression cases failed.' }
    Remove-Item -LiteralPath $packagePath
    $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
    $archive.Dispose()
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $testRoot }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'A package without a nuspec was accepted.' }
    Write-Output 'Release security regressions passed.'
}
finally {
    # Only remove this test-owned, nonempty directory under the temp root.
    if ($testRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::Ordinal) -and
        [IO.Path]::GetFileName($testRoot).StartsWith('release-security-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
