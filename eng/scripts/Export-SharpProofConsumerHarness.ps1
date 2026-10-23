[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/consumer-harness'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ConsumerHarness.psm1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
Assert-SharpProofContainer 'Consumer harness export requires the canonical Linux container.'
if ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [Runtime.InteropServices.Architecture]::X64 -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'Consumer harness export requires Linux amd64.'
}
$assembly = Get-SharpProofTestAssemblyPath -ProjectPath (Join-Path $repositoryRoot `
        'src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj') -Configuration $Configuration
$source = Split-Path -Parent $assembly
$output = Resolve-SharpProofContainedPath -Root $repositoryRoot -Path $OutputDirectory -ParameterName 'OutputDirectory'
$artifacts = Join-Path $repositoryRoot 'artifacts'
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or
    (Test-Path -LiteralPath $output)) {
    throw 'Consumer harness export requires a new directory under artifacts.'
}
# Copy the complete test output tree, including VSTest/NUnit adapters, runtime
# dependencies, satellite resources and native assets. No obj or tool caches.
$files = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
if (@($files | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            ($_.PSIsContainer -and $_.Name -ieq 'obj') }).Count -ne 0) {
    throw 'Consumer harness output must not contain links or compiler intermediates.'
}
$payload = Join-Path $output 'payload'
[IO.Directory]::CreateDirectory($payload) | Out-Null
foreach ($file in $files | Where-Object { -not $_.PSIsContainer }) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
    $destination = Join-Path $payload $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    [IO.File]::Copy($file.FullName, $destination, $false)
}
$receipt = New-SharpProofConsumerHarnessReceipt -RepositoryRoot $repositoryRoot -Configuration $Configuration `
    -PackageSource $PackageSource -PayloadDirectory $payload
[IO.File]::WriteAllText((Join-Path $output 'receipt.json'),
    ($receipt | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
$null = Test-SharpProofConsumerHarnessReceipt -HarnessRoot $output -RepositoryRoot $repositoryRoot `
    -Configuration $Configuration -PackageSource $PackageSource
Write-Host "Exported exact-package consumer harness: $output"
