[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$resolvedUpstream = (Resolve-Path -LiteralPath $UpstreamRoot).Path
$repositoryRoot = (Resolve-Path -LiteralPath (
    Join-Path $PSScriptRoot '..\..')).Path
$wrapper = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$project = Join-Path $repositoryRoot 'SharpProof.Gates\SharpProof.Gates.csproj'
$variableName = 'SHARPPROOF_OSS_CORPUS_SOURCE'
$previousValue = [Environment]::GetEnvironmentVariable(
    $variableName,
    [EnvironmentVariableTarget]::Process)

try {
    [Environment]::SetEnvironmentVariable(
        $variableName,
        $resolvedUpstream,
        [EnvironmentVariableTarget]::Process)
    & $wrapper run --project $project -c Release -- corpus-update
    if ($LASTEXITCODE -ne 0) {
        throw "OSS corpus import failed with exit code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        $variableName,
        $previousValue,
        [EnvironmentVariableTarget]::Process)
}
