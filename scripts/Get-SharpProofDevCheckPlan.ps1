[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageManifest = Get-Content -LiteralPath (Join-Path `
    $repositoryRoot 'scripts/package-projects.json') -Raw |
    ConvertFrom-Json
if ([int]$packageManifest.schemaVersion -ne 1) {
    throw 'Unsupported package-project manifest schema.'
}
$packageProjects = @($packageManifest.projects)
if ($packageProjects.Count -ne 3) {
    throw 'Developer-check plan requires exactly three package projects.'
}

$commands = [Collections.Generic.List[object]]::new()
function Add-Command {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$CommandConfiguration,
        [Parameter(Mandatory)][bool]$NoBuild
    )

    $commands.Add([ordered]@{
        id = $Id
        phase = $Phase
        configuration = $CommandConfiguration
        noBuild = $NoBuild
    })
}

Add-Command 'restore' 'restore' $Configuration $false
Add-Command 'solution-build' 'build' $Configuration $false
Add-Command 'semantic-tests' 'semantic-tests' $Configuration $true
if ($Configuration -eq 'Debug') {
    Add-Command 'package-restore' 'package-tests' $Configuration $false
    Add-Command 'package-test-build' 'package-tests' $Configuration $false
}
foreach ($project in $packageProjects) {
    $packageId = [IO.Path]::GetFileNameWithoutExtension([string]$project)
    Add-Command (
        'package-pack:' + $packageId) 'package-tests' 'Release' (
        $Configuration -eq 'Release')
}
Add-Command 'performance-smoke' 'performance-smoke' $Configuration $true

[ordered]@{
    schemaVersion = 1
    command = 'check'
    configuration = $Configuration
    commands = @($commands)
} | ConvertTo-Json -Depth 5
