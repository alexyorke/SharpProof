[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$ComparisonRef = '',

    [switch]$PlanOnly,

    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1800
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Changed-project testing requires the canonical Linux container.'
}
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$parallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'

function Invoke-GitLines {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $lines = @(& git -C $repositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    return @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Invoke-RequiredDotnet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if ([string]::IsNullOrWhiteSpace($ComparisonRef)) {
    $configured = [Environment]::GetEnvironmentVariable(
        'SHARPPROOF_CHANGED_BASE_REF',
        [EnvironmentVariableTarget]::Process)
    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        $ComparisonRef = $configured
    }
    elseif (@(Invoke-GitLines @('status', '--porcelain')).Count -gt 0) {
        $ComparisonRef = 'HEAD'
    }
    else {
        $ComparisonRef = 'HEAD^'
    }
}

Invoke-GitLines @('rev-parse', '--verify', $ComparisonRef) | Out-Null
$changedPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($path in Invoke-GitLines @(
        'diff', '--name-only', $ComparisonRef, '--')) {
    [void]$changedPaths.Add($path.Replace('\', '/'))
}
foreach ($path in Invoke-GitLines @(
        'ls-files', '--others', '--exclude-standard')) {
    [void]$changedPaths.Add($path.Replace('\', '/'))
}
if ($changedPaths.Count -eq 0) {
    Write-Host "No changes found relative to $ComparisonRef."
    return
}

$projectPaths = @(Invoke-GitLines @('ls-files', '*.csproj'))
$projects = @{}
foreach ($relativePath in $projectPaths) {
    $relative = $relativePath.Replace('\', '/')
    $fullPath = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $relative))
    [xml]$xml = Get-Content -LiteralPath $fullPath -Raw
    $references = @(
        $xml.SelectNodes("//*[local-name()='ProjectReference']") |
            ForEach-Object {
                [IO.Path]::GetFullPath((Join-Path (
                    Split-Path -Parent $fullPath) `
                    ([string]$_.GetAttribute('Include'))))
            })
    $compiledFiles = @(
        $xml.SelectNodes("//*[local-name()='Compile']") |
            ForEach-Object { [string]$_.GetAttribute('Include') } |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                -not $_.Contains('*', [StringComparison]::Ordinal)
            } |
            ForEach-Object {
                [IO.Path]::GetFullPath((Join-Path (
                    Split-Path -Parent $fullPath) $_))
            })
    $projects[$fullPath] = [pscustomobject]@{
        FullPath = $fullPath
        RelativePath = $relative
        Directory = Split-Path -Parent $fullPath
        References = $references
        CompiledFiles = $compiledFiles
    }
}

$testProjects = @($projects.Values | Where-Object {
        $_.RelativePath -match '(^|/)SharpProof\.[^/]+\.Test/' -or
        $_.RelativePath -match '(^|/)SharpProof\.ArchitectureTest/'
    })
$changedProjectPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$globalImpact = $false
$scriptOrDocumentationImpact = $false
foreach ($changedPath in $changedPaths) {
    $fullChangedPath = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $changedPath))
    if ($changedPath -match '^Directory\.' -or
        $changedPath -match '^[^/]+\.(props|targets)$' -or
        $changedPath -in @('global.json', 'NuGet.Config', 'SharpProof.sln')) {
        $globalImpact = $true
        continue
    }
    if ($changedPath.StartsWith('.github/', [StringComparison]::Ordinal) -or
        $changedPath.StartsWith('eng/', [StringComparison]::Ordinal) -or
        $changedPath.StartsWith('scripts/', [StringComparison]::Ordinal) -or
        $changedPath.StartsWith('docs/', [StringComparison]::Ordinal) -or
        $changedPath.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
        $scriptOrDocumentationImpact = $true
    }
    foreach ($project in $projects.Values) {
        $directoryPrefix = $project.Directory +
            [IO.Path]::DirectorySeparatorChar
        if ($fullChangedPath.StartsWith(
                $directoryPrefix,
                [StringComparison]::Ordinal) -or
            $project.CompiledFiles -contains $fullChangedPath) {
            [void]$changedProjectPaths.Add($project.FullPath)
        }
    }
}

$selected = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
if ($globalImpact) {
    foreach ($testProject in $testProjects) {
        [void]$selected.Add($testProject.FullPath)
    }
}
else {
    foreach ($testProject in $testProjects) {
        $visited = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $pending = [Collections.Generic.Stack[string]]::new()
        $pending.Push($testProject.FullPath)
        $matches = $false
        while ($pending.Count -gt 0 -and -not $matches) {
            $candidate = $pending.Pop()
            if (-not $visited.Add($candidate)) {
                continue
            }
            if ($changedProjectPaths.Contains($candidate)) {
                $matches = $true
                break
            }
            if ($projects.ContainsKey($candidate)) {
                foreach ($reference in $projects[$candidate].References) {
                    $pending.Push($reference)
                }
            }
        }
        if ($matches) {
            [void]$selected.Add($testProject.FullPath)
        }
    }
}

$architectureProject = [IO.Path]::GetFullPath((Join-Path $repositoryRoot (
    'SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj')))
$packageProject = [IO.Path]::GetFullPath((Join-Path $repositoryRoot (
    'SharpProof.Package.Test/SharpProof.Package.Test.csproj')))
if ($scriptOrDocumentationImpact -or $globalImpact) {
    [void]$selected.Add($architectureProject)
}
if (@($changedPaths | Where-Object {
            $_.StartsWith('scripts/', [StringComparison]::Ordinal) -or
            $_.StartsWith('eng/container/', [StringComparison]::Ordinal) -or
            $_.StartsWith('SharpProof.Package/', [StringComparison]::Ordinal) -or
            $_.StartsWith('SharpProof.Verifier/', [StringComparison]::Ordinal)
        }).Count -gt 0) {
    [void]$selected.Add($packageProject)
}
if ($selected.Count -eq 0) {
    [void]$selected.Add($architectureProject)
}

$runPackageTests = $selected.Remove($packageProject)
$selectedRelative = @($selected | ForEach-Object {
        [IO.Path]::GetRelativePath($repositoryRoot, $_).Replace('/', '\')
    } | Sort-Object)
Write-Host "Changed paths relative to ${ComparisonRef}: $($changedPaths.Count)"
Write-Host "Selected test projects: $($selectedRelative.Count)"
$selectedRelative | ForEach-Object { Write-Host "  $_" }
if ($runPackageTests) {
    Write-Host '  SharpProof.Package.Test (duration-aware sharder)'
}
if ($PlanOnly) {
    return
}

if ($selectedRelative.Count -gt 0) {
    $filterPath = Join-Path $repositoryRoot (
        '.sharpproof-changed-' + [Guid]::NewGuid().ToString('N') + '.slnf')
    try {
        [pscustomobject]@{
            solution = [ordered]@{
                path = 'SharpProof.sln'
                projects = $selectedRelative
            }
        } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $filterPath -Encoding utf8NoBOM
        Invoke-RequiredDotnet @('restore', $filterPath, '--locked-mode')
        Invoke-RequiredDotnet @(
            'test', $filterPath,
            '-c', $Configuration,
            '--no-restore',
            "/m:$parallelism",
            '--filter',
            'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus')
    }
    finally {
        Remove-Item -LiteralPath $filterPath -Force -ErrorAction SilentlyContinue
    }
}

if ($runPackageTests) {
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
        -Configuration $Configuration `
        -TimeoutSeconds $TimeoutSeconds
    if ($LASTEXITCODE -ne 0) {
        throw 'Changed package tests failed.'
    }
}
