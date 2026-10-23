[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageSource,
    [Parameter(Mandatory = $true)][string]$ToolPackageSource,
    [Parameter(Mandatory = $true)][string]$PreparedCodeFixesSource,
    [Parameter(Mandatory = $true)][DateTime]$BuildStartedUtc,
    [ValidateSet('Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/developer-harness'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperHarness.psm1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
Assert-SharpProofContainer 'Developer harness export requires the canonical Linux container.'
$assembly = Get-SharpProofTestAssemblyPath -ProjectPath (Join-Path $repositoryRoot 'src/SharpProof.DeveloperExperience.Tests/SharpProof.DeveloperExperience.Tests.csproj') -Configuration $Configuration
$source = Split-Path -Parent $assembly
$output = Resolve-SharpProofContainedPath -Root $repositoryRoot -Path $OutputDirectory -ParameterName 'OutputDirectory'
$artifacts = Join-Path $repositoryRoot 'artifacts'
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal) -or (Test-Path -LiteralPath $output)) {
    throw 'Developer harness export requires a new directory under artifacts.'
}
$files = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
if (@($files | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or ($_.PSIsContainer -and $_.Name -ieq 'obj') }).Count -ne 0) {
    throw 'Developer harness output must not contain links or compiler intermediates.'
}
$payload = Join-Path $output 'payload'
[IO.Directory]::CreateDirectory($payload) | Out-Null
foreach ($file in $files | Where-Object { -not $_.PSIsContainer }) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
    $destination = Join-Path $payload $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    [IO.File]::Copy($file.FullName, $destination, $false)
}
$receipt = New-SharpProofDeveloperHarnessReceipt -RepositoryRoot $repositoryRoot -PackageSource $PackageSource -ToolPackageSource $ToolPackageSource -PreparedCodeFixesSource $PreparedCodeFixesSource -PayloadDirectory $payload -BuildStartedUtc $BuildStartedUtc
[IO.File]::WriteAllText((Join-Path $output 'receipt.json'), ($receipt | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
$null = Test-SharpProofDeveloperHarnessReceipt -HarnessRoot $output -RepositoryRoot $repositoryRoot -PackageSource $PackageSource -ToolPackageSource $ToolPackageSource -PreparedCodeFixesSource $PreparedCodeFixesSource
Write-Host "Exported exact-source developer harness: $output"
