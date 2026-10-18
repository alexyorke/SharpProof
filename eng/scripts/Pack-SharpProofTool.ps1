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
& dotnet pack (Join-Path $repository 'src/SharpProof.Tool/SharpProof.Tool.csproj') `
    --configuration $Configuration --output $OutputDirectory -p:ContinuousIntegrationBuild=true -p:SharpProofPackTool=true
if ($LASTEXITCODE -ne 0) { throw 'Optional SharpProof.Tool packing failed.' }
Write-Host "Optional tool feed: $OutputDirectory"
Write-Host 'The main SharpProof application package graph does not depend on this tool.'
