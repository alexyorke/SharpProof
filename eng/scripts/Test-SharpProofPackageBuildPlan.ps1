[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.PackageBuildPlan.ps1')

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$packageRoots = Get-SharpProofPackageBuildProjects -RepositoryRoot $repositoryRoot
$harnessRoot = 'src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj'
$buildRoots = Get-SharpProofPackageBuildProjects -RepositoryRoot $repositoryRoot -IncludeConsumerHarness
if ($packageRoots.Length -ne 3 -or $buildRoots.Length -ne 4 -or $buildRoots -cnotcontains $harnessRoot -or
    @($packageRoots | Where-Object { $buildRoots -cnotcontains $_ }).Count -ne 0) {
    throw 'Package build roots must preserve all three published projects and the optional consumer harness.'
}

# Follow every explicit edge, including ReferenceOutputAssembly=false. Every
# managed payload producer named by a nuspec must remain reachable without
# relying on unrelated projects having been built earlier in the solution.
$closure = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$pending = [Collections.Generic.Queue[string]]::new()
foreach ($project in $packageRoots) { $pending.Enqueue($project) }
while ($pending.Count -gt 0) {
    $project = $pending.Dequeue()
    if (-not $closure.Add($project)) { continue }
    $projectPath = Join-Path $repositoryRoot $project
    [xml]$projectXml = [IO.File]::ReadAllText($projectPath)
    foreach ($reference in $projectXml.SelectNodes('/Project/ItemGroup/ProjectReference')) {
        $include = $reference.GetAttribute('Include').Replace('\', '/')
        if ($include.Contains('$') -or $include.Contains('*')) {
            throw "Package closure reference requires an evaluated graph check: '$include'."
        }
        $absolute = [IO.Path]::GetFullPath($include, (Split-Path -Parent $projectPath))
        $pending.Enqueue([IO.Path]::GetRelativePath($repositoryRoot, $absolute).Replace('\', '/'))
    }
}
$producers = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($project in $packageRoots) {
    $nuspecs = @(Get-ChildItem -LiteralPath (Split-Path -Parent (Join-Path $repositoryRoot $project)) -Filter '*.nuspec' -File)
    foreach ($nuspec in $nuspecs) {
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText($nuspec.FullName), 'artifacts\\bin\\([^\\]+)\\')) {
            [void]$producers.Add($match.Groups[1].Value)
        }
    }
}
if ($producers.Count -eq 0) { throw 'Package build plan found no managed nuspec payload producers.' }
foreach ($producer in $producers) {
    if (-not $closure.Contains("src/$producer/$producer.csproj")) {
        throw "Package payload producer is not in the package build closure: '$producer'."
    }
}

function Assert-PackageBuildPlanFailure([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -cne $Expected) { throw }
        return
    }
    throw "Package build plan accepted invalid input: $Expected"
}

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-package-build-plan-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'eng/scripts')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $fixture 'src/Future.Package')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'src/Future.Package/Future.Package.csproj'), '<Project />')
    [IO.File]::WriteAllText((Join-Path $fixture 'SharpProof.slnx'), '<Solution><Project Path="src/Future.Package/Future.Package.csproj" /></Solution>')
    $catalogPath = Join-Path $fixture 'eng/scripts/package-projects.json'
    [IO.File]::WriteAllText($catalogPath, '{"schemaVersion":1,"projects":["src/Future.Package/Future.Package.csproj"]}')
    $futureRoots = Get-SharpProofPackageBuildProjects -RepositoryRoot $fixture
    if ($futureRoots.Length -ne 1 -or $futureRoots[0] -cne 'src/Future.Package/Future.Package.csproj') {
        throw 'Package build plan did not derive a new package root from the catalog.'
    }
    foreach ($invalid in @(
            '{"schemaVersion":1,"projects":[]}',
            '{"schemaVersion":1,"projects":"src/Future.Package/Future.Package.csproj"}',
            '{"schemaVersion":2,"projects":["src/Future.Package/Future.Package.csproj"]}')) {
        [IO.File]::WriteAllText($catalogPath, $invalid)
        Assert-PackageBuildPlanFailure { Get-SharpProofPackageBuildProjects -RepositoryRoot $fixture } `
            'Package project catalog requires schema 1 and a nonempty project array.'
    }
    foreach ($invalidRoot in @('../outside.csproj', '/outside.csproj', 'src/../outside.csproj',
            'src/Future.Package/../Future.Package/Future.Package.csproj', 'src/Future.Package/file.txt')) {
        [IO.File]::WriteAllText($catalogPath, (([ordered]@{ schemaVersion = 1; projects = @($invalidRoot) }) | ConvertTo-Json))
        Assert-PackageBuildPlanFailure { Get-SharpProofPackageBuildProjects -RepositoryRoot $fixture } `
            'Package build roots must be repository-relative projects under src.'
    }
    [IO.File]::WriteAllText($catalogPath, '{"schemaVersion":1,"projects":["src/Missing/Missing.csproj"]}')
    Assert-PackageBuildPlanFailure { Get-SharpProofPackageBuildProjects -RepositoryRoot $fixture } `
        "Package build project is missing from the source solution: 'src/Missing/Missing.csproj'."
    [IO.File]::WriteAllText($catalogPath, '{"schemaVersion":1,"projects":["src/Future.Package/Future.Package.csproj","src/Future.Package/Future.Package.csproj"]}')
    Assert-PackageBuildPlanFailure { Get-SharpProofPackageBuildProjects -RepositoryRoot $fixture } `
        "Package build project is duplicated: 'src/Future.Package/Future.Package.csproj'."
    [IO.File]::WriteAllText($catalogPath, '{"schemaVersion":1,"schemaVersion":1,"projects":["src/Future.Package/Future.Package.csproj"]}')
    Assert-PackageBuildPlanFailure { Get-SharpProofPackageBuildProjects -RepositoryRoot $fixture } `
        "Package project catalog contains duplicate property 'schemaVersion'."
}
finally {
    if ([IO.Directory]::Exists($fixture)) { [IO.Directory]::Delete($fixture, $true) }
}
Write-Host "Package build plan retains all $($packageRoots.Length) package roots, the harness, and $($producers.Count) nuspec payload producers."
