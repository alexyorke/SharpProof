[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Get-SharpProofPilotPackageAuthority.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPilotReport.ps1')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sp-pilot-' + [Guid]::NewGuid().ToString('N'))
$packages = Join-Path $fixture 'packages'
$commit = '1111111111111111111111111111111111111111'
$version = '1.0.0-preview.1'

function Write-Package([string]$Id, [string]$Extension, [string]$Commit = $commit,
    [string]$PackageVersion = $version) {
    $path = Join-Path $packages "$Id.$version$Extension"
    $archive = [IO.Compression.ZipFile]::Open($path, 'Create')
    try {
        $entry = $archive.CreateEntry("$Id.nuspec")
        $writer = [IO.StreamWriter]::new($entry.Open())
        try {
            $writer.Write("<package><metadata><id>$Id</id><version>$PackageVersion</version><repository commit=`"$Commit`" /></metadata></package>")
        } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
}

function Reset-Packages {
    if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }
    [IO.Directory]::CreateDirectory($packages) | Out-Null
    foreach ($id in @('SharpProof.Attributes', 'SharpProof', 'SharpProof.Verifier')) {
        Write-Package $id '.nupkg'; Write-Package $id '.snupkg'
    }
}

function Require-Failure([scriptblock]$Action, [string]$Name) {
    try { & $Action; throw "Fixture '$Name' was accepted." }
    catch { if ($_.Exception.Message -eq "Fixture '$Name' was accepted.") { throw } }
}

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Reset-Packages
    $valid = @(Get-SharpProofPilotPackageAuthority $packages $version $commit)
    if ($valid.Count -ne 6) { throw 'Canonical package authority failed.' }
    $target = Join-Path $packages "SharpProof.$version.nupkg"
    [IO.File]::AppendAllText($target, 'changed')
    $changed = @(Get-SharpProofPilotPackageAuthority $packages $version $commit)
    if (($changed | Where-Object fileName -eq "SharpProof.$version.nupkg").sha256 -eq
        ($valid | Where-Object fileName -eq "SharpProof.$version.nupkg").sha256) {
        throw 'Changed package bytes retained their identity.'
    }
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.snupkg")
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } missing-package
    Reset-Packages; Copy-Item (Join-Path $packages "SharpProof.$version.nupkg") (Join-Path $packages 'extra.nupkg')
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } extra-package
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.nupkg"); Write-Package SharpProof '.nupkg' ('2' * 40)
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } stale-commit
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.nupkg"); Write-Package Wrong '.nupkg'
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } wrong-id
    Reset-Packages; Remove-Item (Join-Path $packages "SharpProof.$version.nupkg"); Write-Package SharpProof '.nupkg' $commit '9.9.9'
    Require-Failure { Get-SharpProofPilotPackageAuthority $packages $version $commit } wrong-version

    # Restore canonical after the wrong-version case.
    Reset-Packages; $artifacts = @(Get-SharpProofPilotPackageAuthority $packages $version $commit)
    $evidence = @('request','result','compilerManifest','sarif') | ForEach-Object {
        [pscustomobject]@{ kind=$_; path="artifacts/pilots/$_.json"; bytes=1; sha256=('a' * 64) }
    }
    $report = [pscustomobject]@{
        schemaVersion=2; runId=('1' * 32); commit=$commit; packageVersion=$version; pilotCount=5
        packageArtifacts=$artifacts
        pilots=@(1..5 | ForEach-Object { [pscustomobject]@{ id="pilot-$_"; runStatus='Complete'; sarifProduced=$true; evidence=$evidence } })
    }
    if (-not (Test-SharpProofPilotReport $report $commit)) { throw 'Canonical pilot report failed.' }
    $report.pilots[0].evidence = @($report.pilots[0].evidence | Select-Object -Skip 1)
    if (Test-SharpProofPilotReport $report $commit) { throw 'Stale/incomplete outputs were accepted.' }
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'ambient/sharpproof/1.0.0-preview.1')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'ambient/sharpproof/1.0.0-preview.1/SharpProof.dll'), 'foreign')
    if ($valid[0].sha256 -eq (Get-FileHash (Join-Path $fixture 'ambient/sharpproof/1.0.0-preview.1/SharpProof.dll')).Hash.ToLowerInvariant()) {
        throw 'Ambient collision test is invalid.'
    }
    Write-Host 'Pilot package/output authority fixtures passed.'
}
finally { if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force } }
