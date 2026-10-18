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
    $originalHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA512).Hash.ToLowerInvariant()
    $expectedContentHash = [Convert]::ToBase64String([Convert]::FromHexString($originalHash))
    Copy-Item -LiteralPath $packagePath -Destination (Join-Path $testRoot 'unsigned-backup')
    & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $testRoot
    $manifest = [IO.File]::ReadAllText((Join-Path $testRoot 'packages.tsv'))
    if ($manifest -notmatch '^fixture\t1\.0\.0\tFixture\.1\.0\.0\.nupkg\t[a-f0-9]{128}\t[A-Za-z0-9+/]{86}==\n$') {
        throw 'Package manifest did not preserve the nuspec identity and qualified hash.'
    }
    $fields = $manifest.Trim().Split("`t")
    $lock = Get-Content -LiteralPath (Join-Path $testRoot 'packages.lock.json') -Raw | ConvertFrom-Json
    if ($fields[3] -cne $originalHash -or $fields[4] -cne $expectedContentHash -or
        $lock.dependencies.release.fixture.contentHash -cne $expectedContentHash) {
        throw 'Original ZIP and NuGet content hashes were conflated or changed.'
    }
    # Synthetic signature bytes only test the transport/CLI process seam. Real
    # repository cryptography is separately probed with the official SDK.
    $envelope = Join-Path $testRoot 'Fixture.repository-envelope'
    Copy-Item -LiteralPath $packagePath -Destination $envelope
    $archive = [IO.Compression.ZipFile]::Open($envelope, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('.signature.p7s').Open())
        try { $writer.Write('synthetic repository envelope; not a cryptographic signature') }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
    & bash (Join-Path $PSScriptRoot 'Test-SharpProofReleaseSecurity.sh') $testRoot $envelope
    if ($LASTEXITCODE -ne 0) { throw 'Release security regression cases failed.' }
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA512).Hash.ToLowerInvariant() -cne $originalHash) {
        throw 'Verification changed the original qualified archive.'
    }
    foreach ($signatureName in @('.signature.p7s', '.SIGNATURE.P7S')) {
        $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Update)
        try { [void]$archive.CreateEntry($signatureName) }
        finally { $archive.Dispose() }
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $testRoot }
        catch { $rejected = $_.Exception.Message -match 'must be unsigned' }
        if (-not $rejected) { throw 'The unsigned release workflow accepted a pre-signed archive.' }
        Copy-Item -LiteralPath (Join-Path $testRoot 'unsigned-backup') -Destination $packagePath -Force
    }
    foreach ($invalidName in @('Fixture.nuspec', '../escape')) {
        $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Update)
        try { [void]$archive.CreateEntry($invalidName) }
        finally { $archive.Dispose() }
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $testRoot }
        catch { $rejected = $_.Exception.Message -match 'ambiguous, duplicate, or unsafe' }
        if (-not $rejected) { throw 'An ambiguous or unsafe ZIP member was accepted.' }
        Copy-Item -LiteralPath (Join-Path $testRoot 'unsigned-backup') -Destination $packagePath -Force
    }
    Remove-Item -LiteralPath $packagePath
    $archive = [IO.Compression.ZipFile]::Open($packagePath, [IO.Compression.ZipArchiveMode]::Create)
    $archive.Dispose()
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $testRoot }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'A package without a nuspec was accepted.' }
    [IO.File]::WriteAllBytes($packagePath, [byte[]]@(1, 2, 3))
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'New-SharpProofPackageManifest.ps1') -PackageDirectory $testRoot }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'A malformed original ZIP archive was accepted.' }
    & (Join-Path $PSScriptRoot 'Test-SharpProofNuGetRepositorySignatures.ps1')
    Write-Output 'Release security regressions passed.'
}
finally {
    # Only remove this test-owned, nonempty directory under the temp root.
    if ($testRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::Ordinal) -and
        [IO.Path]::GetFileName($testRoot).StartsWith('release-security-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
