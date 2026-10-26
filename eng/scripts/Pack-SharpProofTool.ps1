[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Pack the optional tool in the canonical Linux amd64 container.'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repository 'artifacts/tool-packages' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
foreach ($project in @('Tool', 'CodeFixes', 'Templates')) {
    & dotnet pack (Join-Path $repository "src/SharpProof.$project/SharpProof.$project.csproj") `
        --configuration $Configuration --output $OutputDirectory -p:ContinuousIntegrationBuild=true `
        -p:SharpProofPackTool=true -p:SharpProofDeveloperPackages=true
    if ($LASTEXITCODE -ne 0) { throw "Optional SharpProof.$project packing failed." }
}
Write-Host "Optional developer package feed: $OutputDirectory"
Write-Host 'The main SharpProof application package graph does not depend on these packages.'
