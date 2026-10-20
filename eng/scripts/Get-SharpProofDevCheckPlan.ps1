[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$packageManifest = Get-Content -LiteralPath (Join-Path `
        $repositoryRoot 'eng/scripts/package-projects.json') -Raw |
    ConvertFrom-Json
if ([int]$packageManifest.schemaVersion -ne 1) {
    throw 'Unsupported package-project manifest schema.'
}
$packageProjects = @($packageManifest.projects)
if ($packageProjects.Count -ne 3) {
    throw 'Developer-check plan requires exactly three package projects.'
}

$commands = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
function Add-Command {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][string]$CommandConfiguration,
        [Parameter(Mandatory)][bool]$NoBuild
    )

    $commands.Add(([ordered]@{
                id            = $Id
                phase         = $Phase
                configuration = $CommandConfiguration
                noBuild       = $NoBuild
            }).AsReadOnly())
}

Add-Command 'restore' 'restore' $Configuration $false
Add-Command 'solution-build' 'build' $Configuration $false
Add-Command 'semantic-tests' 'semantic-tests' $Configuration $true
if ($Configuration -eq 'Debug') {
    Add-Command 'package-product-build' 'package-tests' 'Release' $false
}
foreach ($project in $packageProjects) {
    $packageId = [IO.Path]::GetFileNameWithoutExtension([string]$project)
    Add-Command (
        'package-pack:' + $packageId) 'package-tests' 'Release' $true
}

([ordered]@{
    schemaVersion = 1
    command       = 'check'
    configuration = $Configuration
    commands      = $commands.ToImmutable()
}).AsReadOnly() | ConvertTo-Json -Depth 5
