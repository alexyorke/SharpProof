[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $UpstreamRoot,

    # Write newly selected methods with Unspecified support and their
    # observed verdicts for review instead of rejecting them.
    [switch] $Draft
)

$ErrorActionPreference = 'Stop'

$resolvedUpstream = (Resolve-Path -LiteralPath $UpstreamRoot).Path
$repositoryRoot = (Resolve-Path -LiteralPath (
        Join-Path $PSScriptRoot '..\..\..')).Path
$wrapper = Join-Path $repositoryRoot 'eng\scripts\Invoke-SharpProofDotnet.ps1'
$project = Join-Path $repositoryRoot 'tests\SharpProof.Gates\SharpProof.Gates.csproj'
$variableName = 'SHARPPROOF_OSS_CORPUS_SOURCE'
$draftVariableName = 'SHARPPROOF_OSS_CORPUS_DRAFT'
$previousValue = [Environment]::GetEnvironmentVariable(
    $variableName,
    [EnvironmentVariableTarget]::Process)
$previousDraft = [Environment]::GetEnvironmentVariable(
    $draftVariableName,
    [EnvironmentVariableTarget]::Process)

try {
    [Environment]::SetEnvironmentVariable(
        $variableName,
        $resolvedUpstream,
        [EnvironmentVariableTarget]::Process)
    [Environment]::SetEnvironmentVariable(
        $draftVariableName,
        $(if ($Draft) { '1' } else { $null }),
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
    [Environment]::SetEnvironmentVariable(
        $draftVariableName,
        $previousDraft,
        [EnvironmentVariableTarget]::Process)
}
