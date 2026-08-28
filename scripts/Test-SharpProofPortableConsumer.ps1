[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux', 'windows', 'macos')]
    [string]$OsFamily
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot `
    'SharpProof.ReleaseConfigurationEvidence.psm1') -Force
$evidencePath = Join-Path $repositoryRoot `
    "artifacts/release-qualification/portable-$OsFamily.json"
$receiptPath = Join-Path $repositoryRoot `
    "artifacts/release-qualification/qualification-receipts/portable-$OsFamily.json"
$portableOutputPaths = @($evidencePath, $receiptPath)
function Remove-PortableQualificationOutputs {
    foreach ($path in $portableOutputPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and
            (Test-Path -LiteralPath $path -PathType Leaf)) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }
}
trap {
    Remove-PortableQualificationOutputs
    throw
}
$null = foreach ($path in $portableOutputPaths) {
    if (Test-Path -LiteralPath $path -PathType Container) {
        throw "Portable qualification output path is a directory: $path"
    }
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}
$attemptId = [Guid]::NewGuid().ToString('N')
$runtimePlatform = Get-SharpProofRuntimePlatform
if ($OsFamily -cne $runtimePlatform.OsFamily) {
    throw "Portable qualification '$OsFamily' cannot run on '$($runtimePlatform.OsFamily)' hosts."
}
& (Join-Path $PSScriptRoot 'Test-SharpProofPackageConsumers.ps1') `
    -PackageSource $PackageSource -FrameworkConsumersOnly
if ($LASTEXITCODE -ne 0) { throw 'Portable package consumer failed.' }
$resolvedSource = (Resolve-Path -LiteralPath $PackageSource).Path
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($evidencePath)) |
    Out-Null
$packages = @(Get-ChildItem -LiteralPath $resolvedSource -File |
    Where-Object Extension -In @('.nupkg', '.snupkg') |
    Sort-Object Name |
    ForEach-Object {
        [ordered]@{
            fileName = $_.Name
            bytes = [int64]$_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).
                Hash.ToLowerInvariant()
        }
    })
[IO.File]::WriteAllText(
    $evidencePath,
    (([ordered]@{
        schemaVersion = 1
        status = 'passed'
        commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        osFamily = $OsFamily
        architecture = $runtimePlatform.Architecture
        attemptId = $attemptId
        packageArtifacts = $packages
    } | ConvertTo-Json -Depth 4) + "`n"),
    [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Write-SharpProofQualificationReceipt.ps1') `
    -Gate "portable-$OsFamily" -EvidencePath $evidencePath
if ($LASTEXITCODE -ne 0) { throw 'Portable qualification receipt failed.' }
