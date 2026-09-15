[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$ComparisonRef = '',

    [switch]$PlanOnly,

    [switch]$NoBuild,

    [switch]$Fast,

    [int]$TimeoutSeconds
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer `
    'Changed-project testing requires the canonical Linux container.'
Assert-SharpProofTestSwitches -Fast:$Fast -NoBuild:$NoBuild
$TimeoutSeconds = Resolve-SharpProofSolutionTestTimeoutSeconds `
    -RepositoryRoot $repositoryRoot `
    -TimeoutSeconds $TimeoutSeconds `
    -WasSpecified $PSBoundParameters.ContainsKey('TimeoutSeconds')
$parallelism = Get-SharpProofSemanticTestParallelism `
    -RepositoryRoot $repositoryRoot

function Invoke-GitLines {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $lines = @(& git -C $repositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    return @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
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
        'diff', '--no-renames', '--name-only', $ComparisonRef, '--')) {
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

$projectPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($projectPath in Invoke-GitLines @('ls-files', '*.csproj')) {
    [void]$projectPaths.Add($projectPath.Replace('\', '/'))
}
foreach ($changedPath in $changedPaths) {
    if ($changedPath.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
        [void]$projectPaths.Add($changedPath)
    }
}
$projects = @{}
$projectInventoryIncomplete = $false
foreach ($relativePath in $projectPaths) {
    $relative = $relativePath.Replace('\', '/')
    $fullPath = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $relative))
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $projectInventoryIncomplete = $true
        continue
    }
    [xml]$xml = Get-Content -LiteralPath $fullPath -Raw
    $references = @(
        $xml.SelectNodes("//*[local-name()='ProjectReference']") |
            ForEach-Object { ([string]$_.GetAttribute('Include')).Split(';') } |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object {
                [IO.Path]::GetFullPath((Join-Path (
                    Split-Path -Parent $fullPath) $_))
            })
    $compiledFiles = @(
        $xml.SelectNodes("//*[local-name()='Compile']") |
            ForEach-Object { ([string]$_.GetAttribute('Include')).Split(';') } |
            ForEach-Object { $_.Trim() } |
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
$validationConsumersByPath = @{
    'SharpProof.DeclarativeModels.catalog.json' = @(
        'SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj'
    )
}
$explicitValidationProjects = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$changedProjectPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$globalImpact = $false
$scriptOrDocumentationImpact = $false
foreach ($changedPath in $changedPaths) {
    $fullChangedPath = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $changedPath))
    if ($validationConsumersByPath.ContainsKey($changedPath)) {
        foreach ($relativeValidationProject in
            $validationConsumersByPath[$changedPath]) {
            [void]$explicitValidationProjects.Add(
                [IO.Path]::GetFullPath(
                    (Join-Path $repositoryRoot $relativeValidationProject)))
        }
    }
    if ($changedPath.StartsWith('eng/testing/', [StringComparison]::Ordinal)) {
        # These sources are injected by Directory.Build.props into multiple
        # test projects, so a path-based project walk cannot identify every
        # consumer. Treat the shared test infrastructure as global impact.
        $globalImpact = $true
        continue
    }
    if ($changedPath -match '^Directory\.' -or
        $changedPath -match '^[^/]+\.(props|targets)$' -or
        $changedPath -in @('global.json', 'NuGet.Config', 'SharpProof.slnx')) {
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
    $reverseReferences = @{}
    foreach ($project in $projects.Values) {
        if (-not $reverseReferences.ContainsKey($project.FullPath)) {
            $reverseReferences[$project.FullPath] =
                [Collections.Generic.List[string]]::new()
        }
        foreach ($reference in $project.References) {
            if (-not $reverseReferences.ContainsKey($reference)) {
                $reverseReferences[$reference] =
                    [Collections.Generic.List[string]]::new()
            }
            $reverseReferences[$reference].Add($project.FullPath)
        }
    }
    $affectedProjects = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $pending = [Collections.Generic.Stack[string]]::new()
    foreach ($changedProject in $changedProjectPaths) {
        if ($affectedProjects.Add($changedProject)) {
            $pending.Push($changedProject)
        }
    }
    while ($pending.Count -gt 0) {
        $candidate = $pending.Pop()
        if (-not $reverseReferences.ContainsKey($candidate)) {
            continue
        }
        foreach ($dependent in $reverseReferences[$candidate]) {
            if ($affectedProjects.Add($dependent)) {
                $pending.Push($dependent)
            }
        }
    }
    foreach ($testProject in $testProjects) {
        if ($affectedProjects.Contains($testProject.FullPath)) {
            [void]$selected.Add($testProject.FullPath)
        }
    }
}
if ($projectInventoryIncomplete) {
    foreach ($testProject in $testProjects) {
        [void]$selected.Add($testProject.FullPath)
    }
}

$architectureProject = [IO.Path]::GetFullPath((Join-Path $repositoryRoot (
    'SharpProof.ArchitectureTest/SharpProof.ArchitectureTest.csproj')))
$packageProject = [IO.Path]::GetFullPath((Join-Path $repositoryRoot (
    'SharpProof.Package.Test/SharpProof.Package.Test.csproj')))
if (($scriptOrDocumentationImpact -or $globalImpact) -and
    $projects.ContainsKey($architectureProject)) {
    [void]$selected.Add($architectureProject)
}
if (@($changedPaths | Where-Object {
            $_.StartsWith('scripts/', [StringComparison]::Ordinal) -or
            $_.StartsWith('eng/container/', [StringComparison]::Ordinal) -or
            $_.StartsWith('SharpProof.Package/', [StringComparison]::Ordinal) -or
            $_.StartsWith('SharpProof.Verifier/', [StringComparison]::Ordinal)
        }).Count -gt 0 -and $projects.ContainsKey($packageProject)) {
    [void]$selected.Add($packageProject)
}
foreach ($validationProject in $explicitValidationProjects) {
    if ($projects.ContainsKey($validationProject)) {
        [void]$selected.Add($validationProject)
    }
}
if ($selected.Count -eq 0) {
    if ($projects.ContainsKey($architectureProject)) {
        [void]$selected.Add($architectureProject)
    }
    else {
        foreach ($testProject in $testProjects) {
            [void]$selected.Add($testProject.FullPath)
        }
    }
}
if ($selected.Count -eq 0) {
    throw 'No surviving test project is available for changed-test selection.'
}

$runPackageTests = $selected.Remove($packageProject)
$selectedRelative = @($selected | ForEach-Object {
        [IO.Path]::GetRelativePath($repositoryRoot, $_).Replace('/', '\')
    } | Sort-Object)
if ($PlanOnly) {
    Write-Host "Changed paths relative to ${ComparisonRef}: $($changedPaths.Count)"
    Write-Host "Selected test projects: $($selectedRelative.Count)"
    $selectedRelative | ForEach-Object { Write-Host "  $_" }
    if ($runPackageTests) {
        Write-Host '  SharpProof.Package.Test (duration-aware sharder)'
    }
    return
}
Write-Host (
    "Running changed tests for {0} changed path(s), {1} project(s){2}." -f
    $changedPaths.Count,
    $selectedRelative.Count,
    $(if ($runPackageTests) { ' plus package shards' } else { '' }))

if ($selectedRelative.Count -gt 0) {
    $directChangedProject = $selectedRelative.Count -eq 1
    $filterPath = ''
    if (-not $directChangedProject) {
        $filterPath = Join-Path $repositoryRoot (
            '.sharpproof-changed-' + [Guid]::NewGuid().ToString('N') + '.slnf')
        [pscustomobject]@{
            solution = [ordered]@{
                path = 'SharpProof.slnx'
                projects = $selectedRelative
            }
        } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $filterPath -Encoding utf8NoBOM
    }
    try {
        if (-not $NoBuild) {
            $restoreTarget = if ($directChangedProject) {
                $selectedRelative[0]
            }
            else {
                $filterPath
            }
            Invoke-SharpProofRequiredDotnet `
                -Arguments @('restore', $restoreTarget, '--locked-mode') `
                -TimeoutSeconds $TimeoutSeconds `
                -Quiet
        }
        $semanticFilter =
            'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
        if ($directChangedProject) {
            $directChangedProjectIsArchitecture =
                [IO.Path]::GetFileName($selectedRelative[0]) -ceq
                    'SharpProof.ArchitectureTest.csproj'
            if (-not $NoBuild) {
                $changedProjectBuildArguments = @(
                    'build', $selectedRelative[0],
                    '-c', $Configuration, '--no-restore')
                if ($Fast) {
                    $changedProjectBuildArguments +=
                        '-p:RunAnalyzersDuringBuild=false'
                }
                Invoke-SharpProofRequiredDotnet `
                    -Arguments $changedProjectBuildArguments `
                    -TimeoutSeconds $TimeoutSeconds `
                    -Quiet
            }
            if ($directChangedProjectIsArchitecture) {
                & (Join-Path $PSScriptRoot `
                    'Invoke-SharpProofSemanticTests.ps1') `
                    -Configuration $Configuration `
                    -NoBuild `
                    -ArchitectureOnly `
                    -Quiet `
                    -TimeoutSeconds $TimeoutSeconds
                if ($LASTEXITCODE -ne 0) {
                    throw 'Changed architecture tests failed.'
                }
                $testArguments = @()
            }
            else {
                $assembly = Get-SharpProofTestAssemblyPath `
                    -ProjectPath $selectedRelative[0] `
                    -Configuration $Configuration
                $testArguments = @('vstest', $assembly)
                $testArguments += '/TestCaseFilter:' + $semanticFilter
            }
        }
        else {
            $testArguments = @(
                'test', $filterPath,
                '-c', $Configuration,
                '--no-restore',
                "/m:$parallelism",
                '--filter', $semanticFilter)
            if ($Fast) {
                $testArguments += '-p:RunAnalyzersDuringBuild=false'
            }
            if ($NoBuild) {
                $testArguments += '--no-build'
            }
        }
        if ($testArguments.Count -gt 0) {
            Invoke-SharpProofRequiredDotnet `
                -Arguments $testArguments `
                -TimeoutSeconds $TimeoutSeconds `
                -Quiet
        }
    }
    finally {
        if (-not [string]::IsNullOrWhiteSpace($filterPath)) {
            Remove-Item -LiteralPath $filterPath -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($runPackageTests) {
    $packageArguments = @{
        Configuration = $Configuration
        TimeoutSeconds = $TimeoutSeconds
    }
    if ($NoBuild) {
        $packageArguments.NoBuild = $true
    }
    if ($Fast) {
        $packageArguments.Fast = $true
    }
    $packageArguments.Quiet = $true
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
        @packageArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Changed package tests failed.'
    }
}

Write-Host 'Changed tests passed.'
