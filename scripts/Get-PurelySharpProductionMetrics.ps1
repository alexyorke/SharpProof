<#
.SYNOPSIS
Reports SharpProof production source size by module.

.DESCRIPTION
This is a read-only inventory aid for refactoring. It excludes tests, generated
output, bin/obj, dot-prefixed local folders, demo, and smoke projects.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [switch]$Json,

    [Parameter()]
    [ValidateRange(1, 200)]
    [int]$Top = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Convert-ToRepoPath
{
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.Substring($repoRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

function Get-ModuleName
{
    param([Parameter(Mandatory = $true)][string]$Path)

    switch -Regex ($Path)
    {
        '^PurelySharp\.Analyzer/' { return 'Analyzer' }
        '^PurelySharp\.Symbolic/' { return 'Symbolic' }
        '^SearchLib/' { return 'SearchLib' }
        '^Tools/' { return 'Tools' }
        '^Shared/' { return 'Shared' }
        '^PurelySharp\.CodeFixes/' { return 'CodeFixes' }
        '^PurelySharp\.Attributes/' { return 'Attributes' }
        '^PurelySharp\.Package/' { return 'Packaging' }
        '^PurelySharp\.Vsix/' { return 'VSIX' }
        default { return 'Other' }
    }
}

Push-Location $repoRoot
try
{
    $files = Get-ChildItem -Path $repoRoot -Recurse -Filter '*.cs' |
        Where-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            $repoPath -notmatch '(^|/)(bin|obj)/' -and
            $repoPath -notmatch '(^|/)\.[^/]+/' -and
            $repoPath -notmatch '^PurelySharp\.(Test|ToolingTest)/' -and
            $repoPath -notmatch '^PurelySharp\.(Demo|Smoke\.Net472)/'
        } |
        ForEach-Object {
            $repoPath = Convert-ToRepoPath $_.FullName
            [pscustomobject]@{
                path = $repoPath
                module = Get-ModuleName $repoPath
                lines = (Get-Content -LiteralPath $_.FullName | Measure-Object -Line).Lines
            }
        } |
        Sort-Object path

    $modules = $files |
        Group-Object module |
        ForEach-Object {
            [pscustomobject]@{
                module = $_.Name
                files = $_.Count
                lines = ($_.Group | Measure-Object lines -Sum).Sum
            }
        } |
        Sort-Object lines -Descending

    $largestFiles = $files |
        Sort-Object lines -Descending |
        Select-Object -First $Top

    $report = [ordered]@{
        schemaVersion = 1
        totalFiles = $files.Count
        totalLines = ($files | Measure-Object lines -Sum).Sum
        modules = @($modules)
        largestFiles = @($largestFiles)
    }

    if ($Json)
    {
        $report | ConvertTo-Json -Depth 4
        exit 0
    }

    "Production source: $($report.totalLines) lines across $($report.totalFiles) files"
    ''
    'Modules'
    $modules | Format-Table -AutoSize | Out-String
    'Largest files'
    $largestFiles | Format-Table -AutoSize | Out-String
}
finally
{
    Pop-Location
}
