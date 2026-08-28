[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseChecksums.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-release-input-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $packages = @(
        'SharpProof.1.0.0.nupkg',
        'SharpProof.Attributes.1.0.0.nupkg',
        'SharpProof.Verifier.1.0.0.nupkg',
        'SharpProof.1.0.0.snupkg',
        'SharpProof.Attributes.1.0.0.snupkg',
        'SharpProof.Verifier.1.0.0.snupkg')
    foreach ($name in $packages) {
        [IO.File]::WriteAllText(
            (Join-Path $root $name),
            '',
            [Text.UTF8Encoding]::new($false))
    }

    Test-SharpProofReleasePackageInput `
        -Directory $root `
        -PackageNames $packages

    foreach ($name in @(
            'SharpProof.release.json',
            'SharpProof.spdx.json',
            'SHA256SUMS')) {
        [IO.File]::WriteAllText(
            (Join-Path $root $name),
            '',
            [Text.UTF8Encoding]::new($false))
    }
    Test-SharpProofReleasePackageInput `
        -Directory $root `
        -PackageNames $packages `
        -AllowGeneratedEvidence

    try {
        Test-SharpProofReleasePackageInput `
            -Directory $root `
            -PackageNames $packages
        throw 'A prior release bundle was accepted as a six-file package source.'
    }
    catch {
        if ($_.Exception.Message -like 'A prior release bundle*') {
            throw
        }
    }

    [IO.File]::WriteAllText(
        (Join-Path $root 'unexpected.txt'),
        '',
        [Text.UTF8Encoding]::new($false))
    try {
        Test-SharpProofReleasePackageInput `
            -Directory $root `
            -PackageNames $packages `
            -AllowGeneratedEvidence
        throw 'An unrelated input file was accepted.'
    }
    catch {
        if ($_.Exception.Message -eq 'An unrelated input file was accepted.') {
            throw
        }
    }

    Write-Host 'Release package input fixtures passed.'
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
}
