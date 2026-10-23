[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
trap { Write-Host $_.ScriptStackTrace; break }
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Developer harness regressions require the canonical Linux container.'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperHarness.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.DeveloperPackageReproduction.psm1') -Force
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-developer-harness-test-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $fixture 'source'
$harnessRoot = Join-Path $sourceRoot 'artifacts/harness'
$payload = Join-Path $harnessRoot 'payload'
$core = Join-Path $sourceRoot 'artifacts/core'
$tool = Join-Path $sourceRoot 'artifacts/tool'
$codeFixes = Join-Path $sourceRoot 'artifacts/codefixes'
$joined = Join-Path $sourceRoot 'artifacts/joined'
$receiptPath = Join-Path $harnessRoot 'receipt.json'
$checks = 0

function Assert-DeveloperHarnessRejected([scriptblock]$Operation, [string]$Expected) {
    try { & $Operation | Out-Null }
    catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }
        return
    }
    throw "Developer harness accepted invalid input: $Expected"
}

function Write-DeveloperFixtureReceipt([object]$Receipt) {
    [IO.File]::WriteAllText($receiptPath, ($Receipt | ConvertTo-Json -Depth 14), [Text.UTF8Encoding]::new($false))
}

function Test-DeveloperFixtureReceipt {
    $null = Test-SharpProofDeveloperHarnessReceipt -HarnessRoot $harnessRoot -RepositoryRoot $sourceRoot -PackageSource $joined
}

function New-DeveloperFixturePackage([string]$Directory, [string]$Id, [string]$Extension) {
    $path = Join-Path $Directory ($Id + '.' + $version + $Extension)
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry($Id + '.nuspec')
        $writer = [IO.StreamWriter]::new($entry.Open())
        try { $writer.Write('<package><metadata><id>' + $Id + '</id><version>' + $version + '</version><repository type="git" url="https://github.com/alexyorke/SharpProof" commit="' + $commit + '" /></metadata></package>') }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Write-DeveloperFixtureResults([string[]]$Names, [string[]]$Outcomes) {
    $taskRoot = Join-Path $fixture 'results/all-tests'
    $discovery = Join-Path $taskRoot 'discovery'
    [IO.Directory]::CreateDirectory($discovery) | Out-Null
    $expected = @('SharpProof.DeveloperExperience.Tests.Fixture.First(1)', 'SharpProof.DeveloperExperience.Tests.Fixture.Second(2)')
    [IO.File]::WriteAllLines((Join-Path $taskRoot 'discovered-tests.txt'), $expected)
    $dump = '<test-run><test-suite><test-case id="1" fullname="' + $expected[0] + '"/><test-case id="2" fullname="' + $expected[1] + '"/></test-suite></test-run>'
    [IO.File]::WriteAllText((Join-Path $discovery 'SharpProof.DeveloperExperience.Test.dll.discovery.dump'), $dump)
    $definitions = [Text.StringBuilder]::new()
    $results = [Text.StringBuilder]::new()
    for ($index = 0; $index -lt $Names.Count; $index++) {
        $split = $Names[$index].LastIndexOf('.')
        [void]$definitions.Append('<UnitTest id="' + $index + '"><TestMethod codeBase="/private/net10.0/SharpProof.DeveloperExperience.Test.dll" className="' + $Names[$index].Substring(0, $split) + '" name="' + $Names[$index].Substring($split + 1) + '"/></UnitTest>')
        [void]$results.Append('<UnitTestResult testId="' + $index + '" executionId="execution-' + $index + '" testName="' + $Names[$index] + '" outcome="' + $Outcomes[$index] + '"/>')
    }
    $trx = '<TestRun><TestDefinitions>' + $definitions + '</TestDefinitions><Results>' + $results + '</Results><ResultSummary><Counters total="' + $Names.Count + '"/></ResultSummary></TestRun>'
    [IO.File]::WriteAllText((Join-Path $taskRoot 'results.trx'), $trx)
}

try {
    foreach ($directory in @($payload, $core, $tool, $codeFixes, $joined)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
    $sourceFiles = @('global.json', 'eng/container/toolchain.json', 'eng/Versions.props', 'Directory.Build.props', 'Directory.Build.targets', 'NuGet.config', 'SharpProof.slnx', 'eng/scripts/package-projects.json')
    foreach ($project in (Get-SharpProofDeveloperQualificationBuildProjects -RepositoryRoot $repositoryRoot -IncludeConsumerHarness)) { $sourceFiles += [string]$project }
    foreach ($relative in $sourceFiles) {
        $destination = Join-Path $sourceRoot $relative
        [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        [IO.File]::Copy((Join-Path $repositoryRoot $relative), $destination, $false)
    }
    [IO.File]::WriteAllLines((Join-Path $sourceRoot '.gitignore'), @('/artifacts/'))
    & git -C $sourceRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize developer harness fixture source.' }
    & git -C $sourceRoot add -- .
    if ($LASTEXITCODE -ne 0) { throw 'Cannot track developer harness fixture source.' }
    & git -C $sourceRoot -c commit.gpgsign=false -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m 'Developer harness fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot commit developer harness fixture source.' }
    $commit = (& git -C $sourceRoot rev-parse HEAD | Out-String).Trim()
    [xml]$versions = Get-Content -LiteralPath (Join-Path $sourceRoot 'eng/Versions.props') -Raw
    $version = $versions.SelectSingleNode('/Project/PropertyGroup/SharpProofPackageVersion').InnerText.Replace('$(SharpProofVersionPrefix)', $versions.SelectSingleNode('/Project/PropertyGroup/SharpProofVersionPrefix').InnerText)
    foreach ($id in @('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier')) { New-DeveloperFixturePackage $core $id '.nupkg' }
    foreach ($id in @('SharpProof.Attributes', 'SharpProof.Verifier')) { New-DeveloperFixturePackage $core $id '.snupkg' }
    New-DeveloperFixturePackage $tool 'SharpProof.Tool' '.nupkg'
    New-DeveloperFixturePackage $codeFixes 'SharpProof.CodeFixes' '.nupkg'
    foreach ($directory in @($core, $tool, $codeFixes)) {
        foreach ($package in Get-ChildItem -LiteralPath $directory -File) { [IO.File]::Copy($package.FullName, (Join-Path $joined $package.Name), $false) }
    }
    foreach ($name in @('SharpProof.DeveloperExperience.Test.dll', 'SharpProof.DeveloperExperience.Test.deps.json', 'SharpProof.DeveloperExperience.Test.runtimeconfig.json', 'nunit.framework.dll', 'NUnit3.TestAdapter.dll', 'testhost.dll', 'SharpProof.Reporting.dll')) {
        [IO.File]::WriteAllText((Join-Path $payload $name), 'fixture:' + $name)
    }
    $resources = Join-Path $payload 'ja'
    [IO.Directory]::CreateDirectory($resources) | Out-Null
    [IO.File]::WriteAllText((Join-Path $resources 'testhost.resources.dll'), 'fixture satellite')
    $started = [DateTime]::UtcNow.AddSeconds(-2)
    $receipt = New-SharpProofDeveloperHarnessReceipt -RepositoryRoot $sourceRoot -PackageSource $core -ToolPackageSource $tool -PreparedCodeFixesSource $codeFixes -PayloadDirectory $payload -BuildStartedUtc $started
    Write-DeveloperFixtureReceipt $receipt
    Test-DeveloperFixtureReceipt
    $null = Test-SharpProofDeveloperHarnessReceipt -HarnessRoot $harnessRoot -RepositoryRoot $sourceRoot -PackageSource $core -ToolPackageSource $tool -PreparedCodeFixesSource $codeFixes
    $original = [IO.File]::ReadAllText($receiptPath)
    $checks += 2

    $savedRunId = $env:GITHUB_RUN_ID
    $savedRunAttempt = $env:GITHUB_RUN_ATTEMPT
    $savedActions = $env:GITHUB_ACTIONS
    $savedProducerAttempt = $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT
    try {
        $env:GITHUB_RUN_ID = '12345'
        $env:GITHUB_RUN_ATTEMPT = '1'
        $env:GITHUB_ACTIONS = 'true'
        $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT = ''
        $retryReceipt = New-SharpProofDeveloperHarnessReceipt -RepositoryRoot $sourceRoot -PackageSource $core -ToolPackageSource $tool -PreparedCodeFixesSource $codeFixes -PayloadDirectory $payload -BuildStartedUtc $started
        Write-DeveloperFixtureReceipt $retryReceipt
        Test-DeveloperFixtureReceipt
        $env:GITHUB_RUN_ATTEMPT = '2'
        $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT = '1'
        Test-DeveloperFixtureReceipt
        $checks += 2
        foreach ($invalidAttempt in @('3', '0', '01')) {
            $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT = $invalidAttempt
            Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'producer attempt must be canonical'
            $checks++
        }
        $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT = '2'
        Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'context mismatch: runAttempt'
        $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT = '1'
        $changedRetry = $retryReceipt | ConvertTo-Json -Depth 14 | ConvertFrom-Json -AsHashtable
        $changedRetry.context.runAttempt = '2'
        Write-DeveloperFixtureReceipt $changedRetry
        Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'context mismatch: runAttempt'
        Write-DeveloperFixtureReceipt $retryReceipt
        $checks += 2
        $env:GITHUB_RUN_ID = '99999'
        Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'context mismatch: runId'
        $env:GITHUB_RUN_ID = ''
        Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'complete workflow run and attempt identity'
        $checks += 2
        $env:GITHUB_RUN_ID = '12345'
        $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT = '01'
        $actualProducer = New-SharpProofDeveloperHarnessReceipt -RepositoryRoot $sourceRoot -PackageSource $core -ToolPackageSource $tool -PreparedCodeFixesSource $codeFixes -PayloadDirectory $payload -BuildStartedUtc $started
        if ($actualProducer.context.runAttempt -cne '2') { throw 'Developer harness export must always bind the actual producer attempt.' }
        $checks++
    }
    finally {
        $env:GITHUB_RUN_ID = $savedRunId
        $env:GITHUB_RUN_ATTEMPT = $savedRunAttempt
        $env:GITHUB_ACTIONS = $savedActions
        $env:SHARPPROOF_DEVELOPER_PRODUCER_ATTEMPT = $savedProducerAttempt
        [IO.File]::WriteAllText($receiptPath, $original)
    }

    foreach ($field in @('repositoryCommit', 'sourceTree', 'configuration', 'platform', 'sdkVersion', 'packageVersion', 'runId', 'runAttempt', 'restore')) {
        $changed = $original | ConvertFrom-Json -AsHashtable
        $changed.context[$field] = 'unexpected'
        Write-DeveloperFixtureReceipt $changed
        Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } ('context mismatch: ' + $field)
        $checks++
    }
    foreach ($field in @('ContinuousIntegrationBuild', 'GeneratePackageOnBuild', 'RepositoryCommit', 'SourceRevisionId', 'SharpProofDeveloperPackages', 'SharpProofPackTool', 'RunAnalyzersDuringBuild')) {
        $changed = $original | ConvertFrom-Json -AsHashtable
        $changed.context.properties[$field] = 'unexpected'
        Write-DeveloperFixtureReceipt $changed
        Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } ('normal build property mismatch: ' + $field)
        $checks++
    }
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.context.buildProjects = @($changed.context.buildProjects[0])
    Write-DeveloperFixtureReceipt $changed
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'producer build roots changed'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.context.inputs[0].sha256 = '0' * 64
    Write-DeveloperFixtureReceipt $changed
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'source inputs'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.testSelection = 'some-filter'
    Write-DeveloperFixtureReceipt $changed
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'complete test selection'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.files[0].path = '../escape.dll'
    Write-DeveloperFixtureReceipt $changed
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'unsafe or duplicate path'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.files[0].path = $changed.files[1].path
    Write-DeveloperFixtureReceipt $changed
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'payload'
    $checks += 5
    [IO.File]::WriteAllText($receiptPath, $original)

    $assembly = Join-Path $payload 'SharpProof.DeveloperExperience.Test.dll'
    $contents = [IO.File]::ReadAllText($assembly)
    [IO.File]::WriteAllText($assembly, ('x' * $contents.Length))
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'payload'
    [IO.File]::WriteAllText($assembly, $contents)
    [IO.File]::WriteAllText((Join-Path $payload 'unexpected.dll'), 'unexpected')
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'payload'
    [IO.File]::Delete((Join-Path $payload 'unexpected.dll'))
    [IO.Directory]::CreateDirectory((Join-Path $payload 'obj')) | Out-Null
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'compiler intermediates'
    [IO.Directory]::Delete((Join-Path $payload 'obj'))
    $link = Join-Path $payload 'linked.dll'
    [void](New-Item -ItemType SymbolicLink -Path $link -Target $assembly)
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'must not contain links'
    [IO.File]::Delete($link)
    $checks += 4

    $package = Get-ChildItem -LiteralPath $joined -Filter 'SharpProof.Tool.*.nupkg' -File
    $packageBytes = [IO.File]::ReadAllBytes($package.FullName)
    $archive = [IO.Compression.ZipFile]::Open($package.FullName, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('changed.txt').Open())
        try { $writer.Write('different package bytes') } finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'packages'
    [IO.File]::WriteAllBytes($package.FullName, $packageBytes)
    [IO.File]::Delete($package.FullName)
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'exactly the seven'
    [IO.File]::WriteAllBytes($package.FullName, $packageBytes)
    $checks += 2

    [IO.File]::WriteAllText((Join-Path $sourceRoot 'untracked.cs'), 'untracked source')
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'clean exact-commit source'
    [IO.File]::Delete((Join-Path $sourceRoot 'untracked.cs'))
    [IO.File]::AppendAllText((Join-Path $sourceRoot 'eng/Versions.props'), ' ')
    Assert-DeveloperHarnessRejected { Test-DeveloperFixtureReceipt } 'clean exact-commit source'
    [IO.File]::Copy((Join-Path $repositoryRoot 'eng/Versions.props'), (Join-Path $sourceRoot 'eng/Versions.props'), $true)
    $checks += 2
    [IO.File]::SetLastWriteTimeUtc($assembly, $started.AddSeconds(-1))
    Assert-DeveloperHarnessRejected {
        New-SharpProofDeveloperHarnessReceipt -RepositoryRoot $sourceRoot -PackageSource $core -ToolPackageSource $tool -PreparedCodeFixesSource $codeFixes -PayloadDirectory $payload -BuildStartedUtc $started
    } 'predates the fresh normal producer build'
    [IO.File]::WriteAllText($assembly, $contents)
    Assert-DeveloperHarnessRejected {
        New-SharpProofDeveloperHarnessReceipt -RepositoryRoot $sourceRoot -PackageSource $core -ToolPackageSource $tool -PreparedCodeFixesSource $codeFixes -PayloadDirectory $payload -BuildStartedUtc ([DateTime]::UtcNow.AddMinutes(1))
    } 'past UTC'
    $checks += 2
    Test-DeveloperFixtureReceipt

    $names = @('SharpProof.DeveloperExperience.Tests.Fixture.First(1)', 'SharpProof.DeveloperExperience.Tests.Fixture.Second(2)')
    Write-DeveloperFixtureResults $names @('Passed', 'Passed')
    $result = Assert-SharpProofDeveloperHarnessResults -ResultsDirectory (Join-Path $fixture 'results')
    if ($result.cases -ne 2 -or $result.discovered -ne 2) { throw 'Developer harness did not reconcile all actual case identities.' }
    $checks++
    Write-DeveloperFixtureResults @($names[0]) @('Passed')
    Assert-DeveloperHarnessRejected { Assert-SharpProofDeveloperHarnessResults -ResultsDirectory (Join-Path $fixture 'results') } 'case counts differ'
    Write-DeveloperFixtureResults @($names[0], 'SharpProof.DeveloperExperience.Tests.Fixture.Other(2)') @('Passed', 'Passed')
    Assert-DeveloperHarnessRejected { Assert-SharpProofDeveloperHarnessResults -ResultsDirectory (Join-Path $fixture 'results') } 'case identities differ'
    foreach ($outcome in @('Failed', 'NotExecuted')) {
        Write-DeveloperFixtureResults $names @('Passed', $outcome)
        Assert-DeveloperHarnessRejected { Assert-SharpProofDeveloperHarnessResults -ResultsDirectory (Join-Path $fixture 'results') } 'every discovered developer case successfully'
        $checks++
    }
    $checks += 2
}
finally {
    if ([IO.Directory]::Exists($fixture)) { [IO.Directory]::Delete($fixture, $true) }
}
Write-Host "Developer harness regressions passed: $checks exact-source, build-profile, package, runtime, and complete-case controls."
