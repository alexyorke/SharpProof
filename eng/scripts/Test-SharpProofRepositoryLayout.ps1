[CmdletBinding()]
param(
    [switch]$EvaluateProjects,
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Repository layout validation requires the canonical Linux container.'
$inventoryText = Invoke-SharpProofGitText -RepositoryRoot $repositoryRoot `
    -Arguments @('ls-files', '--cached', '--others', '--exclude-standard', '-z') `
    -FailureMessage 'Could not inventory repository source files.'
$files = @($inventoryText.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries) |
        Sort-Object -Unique | Where-Object {
            Test-Path -LiteralPath (Join-Path $repositoryRoot $_) -PathType Leaf
        })
$failures = [Collections.Generic.List[string]]::new()
$records = [Collections.Generic.List[object]]::new()
$evaluations = [Collections.Generic.List[object]]::new()
function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $failures.Add($Message) }
}
$required = @('global.json', 'NuGet.config', 'Directory.Build.props',
    'Directory.Build.targets', 'Directory.Packages.props', 'LICENSE',
    'build.sh', 'build.cmd', 'build.ps1', 'restore.sh', 'restore.cmd',
    'test.sh', 'test.cmd', 'eng/build.sh', 'eng/build.ps1',
    'eng/cibuild.sh', 'eng/CIBuild.cmd', 'eng/Build.props',
    'eng/Versions.props', 'eng/arcade-source.json')
foreach ($path in $required) {
    Require (Test-Path -LiteralPath (Join-Path $repositoryRoot $path) -PathType Leaf) "Missing required file: $path"
}
$globalJson = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
$upstream = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/arcade-source.json') -Raw | ConvertFrom-Json
Require ($globalJson.'msbuild-sdks'.'Microsoft.DotNet.Arcade.Sdk' -ceq $upstream.sdkVersion) 'Arcade SDK and common-script provenance versions differ.'
Require ($globalJson.tools.dotnet -ceq $globalJson.sdk.version) 'Arcade tools.dotnet and the pinned .NET SDK differ.'
Require ($globalJson.sdk.rollForward -ceq 'disable') 'The canonical SDK must remain exactly pinned.'
foreach ($item in $upstream.commonFiles) {
    $path = Join-Path $repositoryRoot $item.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("Missing upstream Arcade file: $($item.path)")
        continue
    }
    Require ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $item.sha256) "Modified upstream Arcade file: $($item.path)"
}
$actualCommon = @($files | Where-Object { $_.StartsWith('eng/common/', [StringComparison]::Ordinal) })
$expectedCommon = @($upstream.commonFiles | ForEach-Object { [string]$_.path })
Require (@(Compare-Object $actualCommon $expectedCommon).Count -eq 0) 'The vendored common-file inventory differs from the pinned upstream tree.'

[xml]$props = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
[xml]$targets = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.targets') -Raw
Require ($null -ne $props.SelectSingleNode('/Project/Import[@Sdk="Microsoft.DotNet.Arcade.Sdk" and @Project="Sdk.props"]')) 'Directory.Build.props must import Arcade Sdk.props.'
Require ($null -ne $targets.SelectSingleNode('/Project/Import[@Sdk="Microsoft.DotNet.Arcade.Sdk" and @Project="Sdk.targets"]')) 'Directory.Build.targets must import Arcade Sdk.targets.'
Require (@($props.SelectNodes('/Project/PropertyGroup/PackageLicenseExpression') | Where-Object { $_.InnerText -ceq 'MIT' }).Count -gt 0) 'The repository must declare its MIT package license globally.'
[xml]$packages = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Packages.props') -Raw
[xml]$versions = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/Versions.props') -Raw
foreach ($package in $packages.SelectNodes('//PackageVersion')) {
    $value = [string]$package.GetAttribute('Version')
    $match = [regex]::Match($value, '^\$\(([A-Za-z0-9_]+)\)$')
    Require $match.Success "Package version is not centralized in eng/Versions.props: $($package.Include)"
    if ($match.Success) {
        Require ($null -ne $versions.SelectSingleNode('/Project/PropertyGroup/' + $match.Groups[1].Value)) "Missing package-version property: $value"
    }
}
$coveredProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($solutionPath in @($files | Where-Object { $_.EndsWith('.slnx', [StringComparison]::Ordinal) })) {
    $full = Join-Path $repositoryRoot $solutionPath
    [xml]$solution = Get-Content -LiteralPath $full -Raw
    foreach ($project in $solution.SelectNodes('//Project')) {
        $absolute = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $full) ([string]$project.Path)))
        Require (Test-Path -LiteralPath $absolute -PathType Leaf) "Missing solution project: $solutionPath -> $($project.Path)"
        [void]$coveredProjects.Add([IO.Path]::GetRelativePath($repositoryRoot, $absolute).Replace('\', '/'))
    }
}
$projectNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in $files) {
    $classification = if ($relative.StartsWith('eng/common/', [StringComparison]::Ordinal)) { 'pinned-upstream-bootstrapper' }
    elseif ($relative.StartsWith('src/', [StringComparison]::Ordinal)) { 'source-project-or-source-asset' }
    elseif ($relative.StartsWith('eng/', [StringComparison]::Ordinal)) { 'engineering' }
    elseif ($relative.StartsWith('docs/', [StringComparison]::Ordinal) -or $relative.EndsWith('.md', [StringComparison]::Ordinal)) { 'documentation' }
    elseif ($relative.StartsWith('tests/', [StringComparison]::Ordinal)) { 'test-data' }
    elseif ($relative.StartsWith('.github/', [StringComparison]::Ordinal)) { 'ci' }
    else { 'repository-metadata-or-developer-configuration' }
    $records.Add([ordered]@{ path = $relative; classification = $classification })
    Require (-not ($relative -match '(^|/)(bin|obj)/')) "Build output is tracked or unignored: $relative"
    Require (-not ($relative -match '^(scripts|tools|samples)/')) "Source path uses a retired root: $relative"
    if ($classification -eq 'pinned-upstream-bootstrapper') { continue }
    $absolute = Join-Path $repositoryRoot $relative
    if ($relative.EndsWith('.csproj', [StringComparison]::Ordinal)) {
        Require ($relative.StartsWith('src/', [StringComparison]::Ordinal)) "Source project is outside src: $relative"
        Require ($coveredProjects.Contains($relative)) "Project is absent from all checked-in solutions: $relative"
        $name = [IO.Path]::GetFileNameWithoutExtension($relative)
        Require ($projectNames.Add($name)) "Duplicate project output key: $name"
        [xml]$projectDocument = Get-Content -LiteralPath $absolute -Raw
        Require (($projectDocument.DocumentElement.GetAttribute('Sdk') -ceq 'Microsoft.NET.Sdk') -or ($null -ne $projectDocument.SelectSingleNode('/Project/Import[@Sdk="Microsoft.NET.Sdk" and @Project="Sdk.props"]'))) "Unsupported source project SDK: $relative"
        $assembly = $projectDocument.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
        if ($null -ne $assembly -and ([string]$assembly.InnerText -match '(\.Test$|ArchitectureTest$)')) {
            Require ($name -match '\.(Tests|UnitTests|IntegrationTests|PerformanceTests)$') "Test project filename violates Arcade conventions: $relative"
        }
        $projectDirectory = Split-Path -Parent $absolute
        Require (-not (Test-Path -LiteralPath (Join-Path $projectDirectory 'bin'))) "Project-local bin output exists: $relative"
        Require (-not (Test-Path -LiteralPath (Join-Path $projectDirectory 'obj'))) "Project-local obj output exists: $relative"
        if ($EvaluateProjects) {
            $query = @(& dotnet msbuild $absolute -nologo -p:Configuration=Release `
                    '-getProperty:MSBuildProjectName,AssemblyName,TargetPath,BaseOutputPath,BaseIntermediateOutputPath,PackageOutputPath,ArtifactsDir,IsTestProject,IsShipping,Version,PackageVersion,AssemblyVersion')
            if ($LASTEXITCODE -ne 0) {
                $failures.Add("MSBuild evaluation failed: $relative")
            }
            else {
                $properties = ($query -join "`n" | ConvertFrom-Json).Properties
                $artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
                foreach ($property in @('BaseOutputPath', 'BaseIntermediateOutputPath', 'PackageOutputPath')) {
                    $value = [string]$properties.$property
                    $resolved = [IO.Path]::GetFullPath($value)
                    Require ($resolved.StartsWith($artifactRoot, [StringComparison]::Ordinal)) "$property escapes artifacts for $relative`: $value"
                }
                $evaluations.Add([ordered]@{ project = $relative; properties = $properties })
            }
        }
    }
    if ([IO.Path]::GetExtension($relative) -in @('.csproj', '.props', '.targets')) {
        [xml]$document = Get-Content -LiteralPath $absolute -Raw
        foreach ($node in $document.SelectNodes('//ProjectReference | //Compile | //EmbeddedResource | //None | //Import | //AdditionalFiles')) {
            $attribute = if ($node.LocalName -eq 'Import') { 'Project' } else { 'Include' }
            $value = [string]$node.GetAttribute($attribute)
            $value = $value.Replace('$(MSBuildThisFileDirectory)', (Split-Path -Parent $absolute) + [IO.Path]::DirectorySeparatorChar)
            if ($relative.EndsWith('.csproj', [StringComparison]::Ordinal)) {
                $value = $value.Replace('$(MSBuildProjectDirectory)', (Split-Path -Parent $absolute))
            }
            if ([string]::IsNullOrWhiteSpace($value) -or $value -match '[$@%*?;]' -or $node.GetAttribute('Sdk')) { continue }
            $value = $value.Replace('\', '/')
            $target = if ([IO.Path]::IsPathRooted($value)) { [IO.Path]::GetFullPath($value) }
            else { [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $absolute) $value)) }
            if ($target.StartsWith((Join-Path $repositoryRoot 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { continue }
            Require (Test-Path -LiteralPath $target) "Missing static MSBuild input: $relative -> $value"
        }
    }
}
foreach ($relative in @($files | Where-Object { $_.EndsWith('.slnf', [StringComparison]::Ordinal) })) {
    $filter = Get-Content -LiteralPath (Join-Path $repositoryRoot $relative) -Raw | ConvertFrom-Json
    foreach ($project in $filter.solution.projects) {
        Require ($coveredProjects.Contains(([string]$project).Replace('\', '/'))) "Unknown solution-filter project: $relative -> $project"
    }
}
$report = [ordered]@{
    schemaVersion               = 1
    standard                    = 'Microsoft.DotNet.Arcade.Sdk repository requirements'
    arcadeVersion               = $upstream.sdkVersion
    upstreamCommit              = $upstream.commit
    sourceFiles                 = $files.Count
    projects                    = $projectNames.Count
    upstreamFilesVerified       = $expectedCommon.Count
    evaluatedProjects           = $evaluations.Count
    excludedFromSourceInventory = @('Git administration', 'ignored generated outputs and local caches', 'separate registered or embedded Git worktrees')
    files                       = @($records)
    evaluations                 = @($evaluations)
    failures                    = @($failures)
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot 'artifacts/layout-audit/report.json'
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath)) | Out-Null
[IO.File]::WriteAllText($OutputPath, ($report | ConvertTo-Json -Depth 12) + "`n", [Text.UTF8Encoding]::new($false))
if ($failures.Count -gt 0) {
    throw ("Arcade repository layout failed with $($failures.Count) issues:`n" + (($failures | Select-Object -First 20) -join "`n"))
}
Write-Host "Arcade repository layout passed: $($files.Count) source files, $($projectNames.Count) projects, $($expectedCommon.Count) pinned common files, $($evaluations.Count) evaluated projects."
