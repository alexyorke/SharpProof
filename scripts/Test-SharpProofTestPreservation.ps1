<#
.SYNOPSIS
Verifies that the phase-two refactor has not removed or disabled tests.

.DESCRIPTION
The immutable phase-two commit is the source of truth. Test methods are
identified by tracked source path and method name; parameterized-test attribute
counts may grow but not shrink. Existing disable markers may be removed, but a
new marker is rejected.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$BaselinePath = 'scripts/production-reduction-phase2-baseline.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$baselineFile = if ([System.IO.Path]::IsPathRooted($BaselinePath))
{
    $BaselinePath
}
else
{
    Join-Path $repoRoot $BaselinePath
}
$baselineCommit = [string](Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json).baselineCommit

function Get-TestSourcePaths
{
    param([string]$Commit = '')

    $paths = if ([string]::IsNullOrEmpty($Commit))
    {
        @(git ls-files -- 'SharpProof.Test/*.cs' 'SharpProof.ToolingTest/*.cs')
    }
    else
    {
        @(git ls-tree -r --name-only $Commit -- 'SharpProof.Test' 'SharpProof.ToolingTest')
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not enumerate test sources for '$Commit'." }
    return @($paths | Where-Object { $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) })
}

function Get-SourceText
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$Commit = ''
    )

    if ([string]::IsNullOrEmpty($Commit))
    {
        return Get-Content -LiteralPath (Join-Path $repoRoot $Path) -Raw
    }

    $text = @(git show "${Commit}:$Path") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Could not read '$Path' from '$Commit'." }
    return $text
}

function Get-TestInventory
{
    param([string]$Commit = '')

    $methods = @{}
    $disableMarkers = New-Object System.Collections.Generic.List[string]
    foreach ($path in Get-TestSourcePaths $Commit)
    {
        $text = Get-SourceText $path $Commit
        foreach ($match in [regex]::Matches($text, '(?m)^\s*(?:\[(?:Explicit|Ignore)\s*\(|Assert\.Ignore\s*\()[^\r\n]*'))
        {
            $disableMarkers.Add(($match.Value -replace '\s+', ' ').Trim())
        }

        $lines = $text -split "\r?\n"
        $attributes = New-Object System.Collections.Generic.List[string]
        $declaration = New-Object System.Collections.Generic.List[string]
        $attributeDepth = 0
        $readingAttributes = $false
        $readingDeclaration = $false
        foreach ($line in $lines)
        {
            $trimmed = $line.Trim()
            if (-not $readingAttributes -and -not $readingDeclaration -and $trimmed.StartsWith('['))
            {
                $readingAttributes = $true
            }

            if ($readingAttributes)
            {
                $attributes.Add($trimmed)
                $attributeDepth += ([regex]::Matches($line, '\[')).Count
                $attributeDepth -= ([regex]::Matches($line, '\]')).Count
                if ($attributeDepth -le 0)
                {
                    $readingAttributes = $false
                    $readingDeclaration = $true
                }
                continue
            }

            if (-not $readingDeclaration) { continue }
            if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('//')) { continue }
            if ($trimmed.StartsWith('['))
            {
                $readingAttributes = $true
                $attributeDepth = 0
                $attributes.Add($trimmed)
                $attributeDepth += ([regex]::Matches($line, '\[')).Count
                $attributeDepth -= ([regex]::Matches($line, '\]')).Count
                if ($attributeDepth -le 0) { $readingAttributes = $false }
                continue
            }

            $declaration.Add($trimmed)
            $joinedDeclaration = $declaration -join ' '
            if (-not $joinedDeclaration.Contains('(') -and
                -not $joinedDeclaration.Contains('{') -and
                -not $joinedDeclaration.Contains('=>'))
            {
                continue
            }

            $joinedAttributes = $attributes -join ' '
            if ($joinedAttributes -match '\[(?:Test|TestCase|TestCaseSource|Theory)(?:\s|\(|\])')
            {
                $nameMatches = [regex]::Matches($joinedDeclaration, '([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]+>)?\s*\(')
                if ($nameMatches.Count -eq 0) { throw "Could not identify test method after attributes in '$path'." }
                $methodName = $nameMatches[$nameMatches.Count - 1].Groups[1].Value
                $key = "$path|$methodName"
                $caseCount = [regex]::Matches($joinedAttributes, '\[(?:TestCase|TestCaseSource|Theory)(?:\s|\(|\])').Count
                if ($methods.ContainsKey($key)) { throw "Duplicate test identity '$key'." }
                $methods[$key] = $caseCount
            }

            $attributes.Clear()
            $declaration.Clear()
            $readingDeclaration = $false
        }
    }

    return [pscustomobject]@{
        Methods = $methods
        DisableMarkers = @($disableMarkers | Sort-Object)
    }
}

Push-Location $repoRoot
try
{
    $baseline = Get-TestInventory $baselineCommit
    $current = Get-TestInventory
    $missing = @($baseline.Methods.Keys | Where-Object { -not $current.Methods.ContainsKey($_) } | Sort-Object)
    $reducedCases = @($baseline.Methods.Keys | Where-Object {
        $current.Methods.ContainsKey($_) -and $current.Methods[$_] -lt $baseline.Methods[$_]
    } | Sort-Object)

    $baselineDisableCounts = @{}
    foreach ($marker in $baseline.DisableMarkers)
    {
        $baselineDisableCounts[$marker] = 1 + $(if ($baselineDisableCounts.ContainsKey($marker))
        {
            $baselineDisableCounts[$marker]
        }
        else
        {
            0
        })
    }
    $currentDisableCounts = @{}
    foreach ($marker in $current.DisableMarkers)
    {
        $currentDisableCounts[$marker] = 1 + $(if ($currentDisableCounts.ContainsKey($marker))
        {
            $currentDisableCounts[$marker]
        }
        else
        {
            0
        })
    }
    $newDisableMarkers = @($currentDisableCounts.Keys | Where-Object {
        -not $baselineDisableCounts.ContainsKey($_) -or $currentDisableCounts[$_] -gt $baselineDisableCounts[$_]
    } | Sort-Object)

    if ($missing.Count -ne 0) { throw "Removed test methods:`n$($missing -join "`n")" }
    if ($reducedCases.Count -ne 0) { throw "Reduced parameterized test cases:`n$($reducedCases -join "`n")" }
    if ($newDisableMarkers.Count -ne 0) { throw "New test disable markers:`n$($newDisableMarkers -join "`n")" }

    "Preserved $($baseline.Methods.Count) baseline test methods; current test methods: $($current.Methods.Count)."
}
finally
{
    Pop-Location
}
