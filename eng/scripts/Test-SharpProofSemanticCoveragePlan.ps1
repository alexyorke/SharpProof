[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.SemanticCoveragePlan.ps1')
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-semantic-plan-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
function Assert-PartitionFailure([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null }
    catch { if ($_.Exception.Message -cne $Expected) { throw }; return }
    throw "Semantic coverage accepted invalid evidence: $Expected"
}
function Write-CorpusReceipt([string]$Part, [string[]]$Names) {
    $root = Join-Path $fixture "semantic-gates-corpus-$Part/semantic/gates-corpus"
    $definitions = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $results = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $index = 0
    foreach ($name in $Names) {
        $index++
        $position = $name.LastIndexOf('.')
        $class = $name.Substring(0, $position)
        $method = $name.Substring($position + 1)
        $definitions.Add("<UnitTest id='$index'><TestMethod codeBase='/tmp/net10.0/SharpProof.Gates.Test.dll' className='$class' name='$method' /></UnitTest>")
        $results.Add("<UnitTestResult testId='$index' executionId='$index' testName='$method' outcome='Passed' />")
    }
    $text = '<TestRun><TestDefinitions>' + ($definitions.ToImmutable() -join '') + '</TestDefinitions><Results>' + ($results.ToImmutable() -join '') + "</Results><ResultSummary><Counters total='$index' failed='0' error='0' /></ResultSummary></TestRun>"
    [IO.File]::WriteAllText((Join-Path $root 'cases.trx'), $text)
}
try {
    $buildProjects = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    $shards = Get-SharpProofSemanticCoverageShards
    foreach ($phase in $shards) {
        $selectedProjects = Get-SharpProofCoverageBuildProjects -RepositoryRoot $repositoryRoot -Phase $phase
        foreach ($project in $selectedProjects) { [void]$buildProjects.Add($project) }
    }
    $semanticProjects = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'SharpProof.Semantic.Tests.slnf') -Raw | ConvertFrom-Json).solution.projects
    $expectedProjects = @($semanticProjects | ForEach-Object { ([string]$_).Replace('\', '/') }) + @(
        'src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj',
        'src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj',
        'src/SharpProof.Architecture.Tests/SharpProof.Architecture.Tests.csproj',
        'src/SharpProof.Attributes.Tests/SharpProof.Attributes.Tests.csproj',
        'src/SharpProof.Package.IntegrationTests/SharpProof.Package.IntegrationTests.csproj')
    if ((@($buildProjects.ToImmutable() | Sort-Object -CaseSensitive) -join "`n") -cne (@($expectedProjects | Sort-Object -CaseSensitive -Unique) -join "`n")) { throw 'Coverage builds do not retain the complete test project union.' }
    # Explicit post-authentication unit state: manifests are tested separately.
    $plan = Get-SharpProofSemanticCoverageTasks -RepositoryRoot $repositoryRoot
    if ((Get-SharpProofSemanticCoverageShards).Count -ne 13 -or $plan.Count -ne 9) { throw 'Semantic coverage plan lost a shard.' }
    foreach ($phase in $plan.Keys) {
        $root = Join-Path $fixture "$phase/semantic"
        [IO.Directory]::CreateDirectory($root) | Out-Null
        ([ordered]@{ schemaVersion = 1; tasks = @($plan[$phase]) }).AsReadOnly() | ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $root 'coverage-cohort.json') -Encoding utf8NoBOM
    }
    ([ordered]@{ schemaVersion = 1; tasks = @('attributes', 'fuzz-console-cancellation') }).AsReadOnly() | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $fixture 'semantic-extras/coverage-cohort.json') -Encoding utf8NoBOM
    $names = @(
        'SharpProof.Gates.Test.CorpusSnapshotWorkflowTests.Case(1)',
        'SharpProof.Gates.Test.CorpusSnapshotWorkflowTests.Case(2)',
        'SharpProof.Gates.Test.GatesCommandWorkflowTests.Command',
        'SharpProof.Gates.Test.CorpusGateTests.Corpus',
        'SharpProof.Gates.Test.CorpusSnapshotWorkflowTests.Performance',
        'SharpProof.Gates.Test.GatesCommandWorkflowTests.Coverage')
    $cases = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    for ($index = 0; $index -lt $names.Count; $index++) {
        $category = if ($index -eq 4) { 'Performance' } elseif ($index -eq 5) { 'Coverage' } else { 'Corpus' }
        $cases.Add("<test-case id='$index' fullname='$($names[$index])'><properties><property name='Category' value='$category' /></properties></test-case>")
    }
    foreach ($part in @('a', 'b')) {
        $root = Join-Path $fixture "semantic-gates-corpus-$part/semantic/gates-corpus"
        [IO.Directory]::CreateDirectory((Join-Path $root 'discovery')) | Out-Null
        [IO.File]::WriteAllLines((Join-Path $root 'discovered-tests.txt'), $names)
        [IO.File]::WriteAllText((Join-Path $root 'discovery/SharpProof.Gates.Test.dll.discovery.dump'), '<test-run>' + ($cases.ToImmutable() -join '') + '</test-run>')
    }
    Write-CorpusReceipt 'a' $names[0..1]
    Write-CorpusReceipt 'b' $names[2..3]
    Assert-SharpProofSemanticCoveragePartitions -Root $fixture -RepositoryRoot $repositoryRoot
    Write-CorpusReceipt 'b' @($names[1], $names[2])
    Assert-PartitionFailure { Assert-SharpProofSemanticCoveragePartitions -Root $fixture -RepositoryRoot $repositoryRoot } 'Corpus coverage execution differs from its exact discovery partition.'
    Write-CorpusReceipt 'b' $names[2..3]
    $receipt = Join-Path $fixture 'semantic-gates-corpus-b/semantic/gates-corpus/cases.trx'
    $valid = [IO.File]::ReadAllText($receipt)
    [IO.File]::WriteAllText($receipt, $valid.Replace("failed='0'", "failed='1'"))
    Assert-PartitionFailure { Assert-SharpProofSemanticCoveragePartitions -Root $fixture -RepositoryRoot $repositoryRoot } 'Corpus coverage has failed or invalid counters.'
    [IO.File]::WriteAllText($receipt, $valid)
    $selection = Join-Path $fixture 'semantic-gates-corpus-b/semantic/gates-corpus/discovered-tests.txt'
    [IO.File]::WriteAllLines($selection, $names[1..5])
    Assert-PartitionFailure { Assert-SharpProofSemanticCoveragePartitions -Root $fixture -RepositoryRoot $repositoryRoot } 'Corpus coverage shards discovered different test universes.'
    [IO.File]::WriteAllLines($selection, $names)
    $taskReceipt = Join-Path $fixture 'semantic-analyzer/semantic/coverage-cohort.json'
    [IO.File]::WriteAllText($taskReceipt, '{"schemaVersion":1,"tasks":[]}')
    Assert-PartitionFailure { Assert-SharpProofSemanticCoveragePartitions -Root $fixture -RepositoryRoot $repositoryRoot } "Semantic coverage shard 'semantic-analyzer' does not match its complete task partition."
    Write-Host 'Semantic coverage plan regressions passed: complete task sets, parameter cases, category exclusions, exact union, duplicate/missing assignment, failure counters, and discovery mismatch.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::Ordinal) -or $resolved -eq [IO.Path]::GetFullPath([IO.Path]::GetTempPath())) { throw 'Invalid semantic fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
