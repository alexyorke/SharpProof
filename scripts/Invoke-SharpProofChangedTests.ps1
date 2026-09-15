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

function Invoke-GitPaths {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    # Read raw NUL-delimited output: line-based native output parsing loses
    # embedded newlines, and ordinary Git output quotes non-ASCII paths.
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    foreach ($argument in @('-C', $repositoryRoot) + $Arguments) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $output = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
        $errorText = $errorOutput.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "git $($Arguments -join ' ') failed: $errorText"
        }
        return $output.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)
    }
    finally {
        $process.Dispose()
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
foreach ($path in Invoke-GitPaths @(
        'diff', '-z', '--no-renames', '--name-only', $ComparisonRef, '--')) {
    [void]$changedPaths.Add($path.Replace('\', '/'))
}
foreach ($path in Invoke-GitPaths @(
        'ls-files', '-z', '--others', '--exclude-standard')) {
    [void]$changedPaths.Add($path.Replace('\', '/'))
}
if ($changedPaths.Count -eq 0) {
    Write-Host "No changes found relative to $ComparisonRef."
    return
}

$projectPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($projectPath in Invoke-GitPaths @('ls-files', '-z', '*.csproj')) {
    [void]$projectPaths.Add($projectPath.Replace('\', '/'))
}
foreach ($changedPath in $changedPaths) {
    if ($changedPath.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
        [void]$projectPaths.Add($changedPath)
    }
}
$projects = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
$projectInventoryIncomplete = $false
foreach ($relativePath in $projectPaths) {
    $relative = $relativePath.Replace('\', '/')
    $fullPath = [IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $relative))
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        $projectInventoryIncomplete = $true
        continue
    }
    $buildFiles = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $pendingImports = [Collections.Generic.Stack[string]]::new()
    $pendingImports.Push($fullPath)
    # MSBuild searches upward independently for the nearest props and targets.
    # Include their item declarations even without an explicit project Import.
    foreach ($implicitName in @('Directory.Build.props', 'Directory.Build.targets')) {
        $searchDirectory = Split-Path -Parent $fullPath
        while (-not [string]::IsNullOrEmpty($searchDirectory)) {
            $implicitPath = Join-Path $searchDirectory $implicitName
            if (Test-Path -LiteralPath $implicitPath -PathType Leaf) {
                $pendingImports.Push($implicitPath)
                break
            }
            if ($searchDirectory -ceq $repositoryRoot) { break }
            $searchDirectory = Split-Path -Parent $searchDirectory
        }
    }
    $items = [Collections.Generic.List[object]]::new()
    while ($pendingImports.Count -gt 0) {
        $buildFile = $pendingImports.Pop()
        if (-not $buildFiles.Add($buildFile)) { continue }
        if (-not (Test-Path -LiteralPath $buildFile -PathType Leaf)) {
            $projectInventoryIncomplete = $true
            continue
        }
        [xml]$xml = Get-Content -LiteralPath $buildFile -Raw
        foreach ($override in $xml.SelectNodes(
                "//*[local-name()='DirectoryBuildPropsPath' or local-name()='DirectoryBuildTargetsPath']")) {
            $overridePath = $override.InnerText.Trim()
            if ([string]::IsNullOrWhiteSpace($overridePath)) { continue }
            # Relative overrides depend on the SDK's importing file location.
            if (-not [IO.Path]::IsPathRooted($overridePath) -or
                $overridePath -match '[$@%]\(|%[0-9a-f]{2}|[*?;]') {
                $projectInventoryIncomplete = $true
                continue
            }
            $pendingImports.Push([IO.Path]::GetFullPath($overridePath))
        }
        foreach ($item in $xml.SelectNodes(
                "//*[local-name()='Compile' or local-name()='ProjectReference']")) {
            $items.Add($item)
        }
        foreach ($import in $xml.SelectNodes("//*[local-name()='Import']")) {
            $importPath = [string]$import.GetAttribute('Project')
            if ([string]::IsNullOrWhiteSpace($importPath) -or
                $importPath -match '[$@%]\(|%[0-9a-f]{2}|[*?;]' -or
                $import.HasAttribute('Sdk')) {
                $projectInventoryIncomplete = $true
                continue
            }
            $pendingImports.Push([IO.Path]::GetFullPath((Join-Path (
                Split-Path -Parent $buildFile) $importPath)))
        }
    }
    # Imported item paths are relative to the consuming project, whereas
    # nested Import paths are relative to the file containing the import.
    foreach ($item in $items) {
        $include = [string]$item.GetAttribute('Include')
        if ($include -match '[$@%]\(|%[0-9a-f]{2}' -or
            ($item.LocalName -eq 'ProjectReference' -and $include -match '[*?]')) {
            # Text parsing cannot evaluate MSBuild expressions, decode item
            # escapes, or expand project-reference globs. Missing edges must
            # not prune tests.
            $projectInventoryIncomplete = $true
        }
    }
    $references = @(
        $items | Where-Object { $_.LocalName -eq 'ProjectReference' } |
            ForEach-Object { ([string]$_.GetAttribute('Include')).Split(';') } |
            ForEach-Object { $_.Trim() } |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_ -notmatch '[$@%]\(|%[0-9a-f]{2}|[*?]'
            } |
            ForEach-Object {
                [IO.Path]::GetFullPath((Join-Path (
                    Split-Path -Parent $fullPath) $_))
            })
    $compiledFilePatterns = @(
        $items | Where-Object { $_.LocalName -eq 'Compile' } |
            ForEach-Object { ([string]$_.GetAttribute('Include')).Split(';') } |
            ForEach-Object { $_.Trim() } |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_ -notmatch '[$@%]\(|%[0-9a-f]{2}'
            } |
            ForEach-Object {
                [IO.Path]::GetFullPath((Join-Path (
                    Split-Path -Parent $fullPath) $_)).Replace('\', '/')
            } |
            ForEach-Object {
                # Match the changed path itself, including deleted files.
                # A recursive directory wildcard also matches zero directories;
                # ordinary wildcards cannot cross directory separators.
                $pattern = [regex]::Escape($_).
                    Replace('\*\*/', '(?:.*/)?').
                    Replace('\*\*', '.*').
                    Replace('\*', '[^/]*').
                    Replace('\?', '[^/]')
                [regex]::new('\A' + $pattern + '\z',
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant)
            })
    $projects[$fullPath] = [pscustomobject]@{
        FullPath = $fullPath
        RelativePath = $relative
        Directory = Split-Path -Parent $fullPath
        References = $references
        CompiledFilePatterns = $compiledFilePatterns
        BuildFiles = $buildFiles
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
    # Imported build files can affect projects outside their own directory.
    # Without an evaluated import graph, retain the full test graph.
    if ($changedPath -match '^Directory\.' -or
        $changedPath -match '\.(props|targets)$' -or
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
        if ($project.BuildFiles.Contains($fullChangedPath) -or
            $fullChangedPath.StartsWith(
                $directoryPrefix,
                [StringComparison]::Ordinal) -or
            @($project.CompiledFilePatterns | Where-Object {
                $_.IsMatch($fullChangedPath.Replace('\', '/'))
            }).Count -ne 0) {
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
    $reverseReferences = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
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
