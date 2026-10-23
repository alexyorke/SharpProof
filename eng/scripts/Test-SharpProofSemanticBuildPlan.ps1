[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.SemanticBuildPlan.ps1')

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$solutionFilter = Join-Path $repositoryRoot '.sharpproof-build-plan-fixture.slnf'
$worker = 'src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj'
$gates = 'src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj'
$architecture = 'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj'
$allProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string](
    [string[]]@((Get-Content -LiteralPath (Join-Path $repositoryRoot 'SharpProof.Semantic.Tests.slnf') -Raw |
                ConvertFrom-Json).solution.projects | ForEach-Object { ([string]$_).Replace('\', '/') }))
$remainingProjects = [Collections.Immutable.ImmutableArray]::CreateRange[string](
    [string[]]@($allProjects | Where-Object { $_ -cne $gates }))

function Assert-BuildProjects {
    param(
        [string]$Name,
        [object[]]$Tasks,
        [Collections.Generic.IReadOnlyList[string]]$Projects,
        [string[]]$Expected
    )
    $actual = Get-SharpProofSemanticBuildProjects -Tasks $Tasks `
        -RepositoryRoot $repositoryRoot -SemanticSolutionFilter $solutionFilter `
        -SemanticProjects $Projects
    $expectedSorted = @($Expected | Sort-Object -CaseSensitive -Unique)
    if (($actual -join "`n") -cne ($expectedSorted -join "`n")) {
        throw "Semantic build roots differ for '$Name': $($actual -join ', ')."
    }
}

function Assert-BuildPlanFailure {
    param([scriptblock]$Action, [string]$Expected)
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -cne $Expected) { throw }
        return
    }
    throw "Semantic build plan accepted invalid input: $Expected"
}

# The two remainder partitions share a project, without compiling unrelated
# test roots. Repeated tasks still result in one MSBuild root.
foreach ($group in @('WorkerA', 'WorkerB')) {
    Assert-BuildProjects -Name $group -Projects $remainingProjects -Tasks @(
        ([ordered]@{ Name = 'worker-remainder-1'; Target = Join-Path $repositoryRoot $worker }).AsReadOnly(),
        ([ordered]@{ Name = 'worker-remainder-4'; Target = $worker.Replace('/', '\') }).AsReadOnly()) `
        -Expected @($worker)
}

# Projects retains its Worker core/manifest tasks in addition to the complete
# Remaining solution selection, including Architecture under a category filter.
Assert-BuildProjects -Name 'Projects' -Projects $remainingProjects -Tasks @(
    ([ordered]@{ Name = 'semantic-projects'; Target = $solutionFilter }).AsReadOnly(),
    ([ordered]@{ Name = 'worker-core'; Target = $worker }).AsReadOnly()) `
    -Expected @(@($remainingProjects) + $worker)
Assert-BuildProjects -Name 'GatesCorpus and GatesShards' -Projects $allProjects -Tasks @(
    ([ordered]@{ Name = 'gates-corpus'; Target = $gates }).AsReadOnly(),
    ([ordered]@{ Name = 'gates-remainder-1'; Target = $gates }).AsReadOnly()) `
    -Expected @($gates)
Assert-BuildProjects -Name 'ArchitectureOnly and Extras' -Projects $allProjects -Tasks @(
    ([ordered]@{ Name = 'architecture'; Target = $architecture }).AsReadOnly()) `
    -Expected @($architecture)
Assert-BuildProjects -Name 'custom-filter and unpartitioned solution' -Projects $allProjects -Tasks @(
    ([ordered]@{ Name = 'semantic-projects'; Target = $solutionFilter }).AsReadOnly(),
    ([ordered]@{ Name = 'worker-core'; Target = $worker }).AsReadOnly(),
    ([ordered]@{ Name = 'gates-corpus'; Target = $gates }).AsReadOnly()) `
    -Expected @(@($allProjects) + $worker)
Assert-BuildProjects -Name 'future direct project task' -Projects $allProjects -Tasks @(
    ([ordered]@{ Name = 'future'; Target = 'src/Future.Tests/Future.Tests.csproj' }).AsReadOnly()) `
    -Expected @('src/Future.Tests/Future.Tests.csproj')

$common = ([ordered]@{
        RepositoryRoot = $repositoryRoot; SemanticSolutionFilter = $solutionFilter
        SemanticProjects = $allProjects
    }).AsReadOnly()
Assert-BuildPlanFailure {
    Get-SharpProofSemanticBuildProjects @common -Tasks @()
} 'Semantic build plan selected no projects.'
Assert-BuildPlanFailure {
    Get-SharpProofSemanticBuildProjects -RepositoryRoot $repositoryRoot `
        -SemanticSolutionFilter $solutionFilter `
        -SemanticProjects ([Collections.Immutable.ImmutableArray[string]]::Empty) `
        -Tasks @(([ordered]@{ Target = $solutionFilter }).AsReadOnly())
} 'Semantic build plan selected no projects.'
Assert-BuildPlanFailure {
    Get-SharpProofSemanticBuildProjects @common -Tasks @(([ordered]@{ Target = '' }).AsReadOnly())
} 'Semantic build task has no target.'
Assert-BuildPlanFailure {
    Get-SharpProofSemanticBuildProjects @common -Tasks @(([ordered]@{ Target = 'foreign.slnf' }).AsReadOnly())
} "Semantic build task has an unsupported target: 'foreign.slnf'."
Assert-BuildPlanFailure {
    Get-SharpProofSemanticBuildProjects @common -Tasks @(([ordered]@{ Target = '../foreign/Foreign.Tests.csproj' }).AsReadOnly())
} "Semantic build project is outside the repository or is not a project: '$([IO.Path]::GetFullPath('../foreign/Foreign.Tests.csproj', $repositoryRoot))'."

Write-Host 'Semantic build plan regressions passed: selected dependency roots, complete project union, custom filters, deduplication, future tasks, and invalid targets.'
