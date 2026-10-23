[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ConsumerHarness.psm1') -Force
Assert-SharpProofContainer 'Consumer harness regressions require the canonical Linux container.'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-consumer-harness-' + [Guid]::NewGuid().ToString('N'))
$harnessRoot = Join-Path $temporary 'harness'
$payload = Join-Path $harnessRoot 'payload'
$source = Join-Path $temporary 'packages'
$receiptPath = Join-Path $harnessRoot 'receipt.json'
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the consumer harness fixture commit.' }
[IO.Directory]::CreateDirectory($payload) | Out-Null
[IO.Directory]::CreateDirectory($source) | Out-Null

function Write-FixtureReceipt([object]$Receipt) {
    [IO.File]::WriteAllText($receiptPath, ($Receipt | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function Test-FixtureReceipt {
    $null = Test-SharpProofConsumerHarnessReceipt -HarnessRoot $harnessRoot -RepositoryRoot $repositoryRoot `
        -Configuration Release -PackageSource $source
}

function Assert-Rejected([scriptblock]$Operation, [string]$Expected) {
    $rejected = $false
    try { & $Operation }
    catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Invalid consumer harness accepted: $Expected" }
}

function New-FixturePackage([string]$Id, [string]$Extension) {
    $path = Join-Path $source "$Id.1.0.0.$Extension"
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry("$Id.nuspec")
        $writer = [IO.StreamWriter]::new($entry.Open())
        try {
            $writer.Write('<package><metadata><id>' + $Id + '</id><version>1.0.0</version>' +
                '<repository type="git" url="https://github.com/alexyorke/SharpProof" commit="' + $commit + '" /></metadata></package>')
        }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Write-FixtureTrx([string[]]$Names, [string[]]$Outcomes, [string]$RunOutcome = 'Completed') {
    $results = [Text.StringBuilder]::new()
    $definitions = [Text.StringBuilder]::new()
    for ($index = 0; $index -lt $Names.Count; $index++) {
        $split = $Names[$index].LastIndexOf('.')
        $class = $Names[$index].Substring(0, $split)
        $method = $Names[$index].Substring($split + 1)
        [void]$results.Append('<UnitTestResult testId="' + $index + '" outcome="' + $Outcomes[$index] + '" />')
        [void]$definitions.Append('<UnitTest id="' + $index + '"><TestMethod className="' + $class + '" name="' + $method + '" /></UnitTest>')
    }
    $path = Join-Path $temporary 'consumer.trx'
    [IO.File]::WriteAllText($path, '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>' +
        $results.ToString() + '</Results><TestDefinitions>' + $definitions.ToString() +
        '</TestDefinitions><ResultSummary outcome="' + $RunOutcome + '" /></TestRun>')
    return $path
}

try {
    foreach ($name in @('SharpProof.Package.Test.dll', 'SharpProof.Package.Test.deps.json',
            'SharpProof.Package.Test.runtimeconfig.json', 'nunit.framework.dll', 'NUnit3.TestAdapter.dll', 'testhost.dll')) {
        [IO.File]::WriteAllText((Join-Path $payload $name), "fixture:$name")
    }
    $resources = Join-Path $payload 'ja'
    [IO.Directory]::CreateDirectory($resources) | Out-Null
    [IO.File]::WriteAllText((Join-Path $resources 'testhost.resources.dll'), 'satellite-resource')
    foreach ($id in @('SharpProof', 'SharpProof.Attributes', 'SharpProof.Verifier')) { New-FixturePackage $id 'nupkg' }
    foreach ($id in @('SharpProof.Attributes', 'SharpProof.Verifier')) { New-FixturePackage $id 'snupkg' }
    $receipt = New-SharpProofConsumerHarnessReceipt -RepositoryRoot $repositoryRoot -Configuration Release `
        -PackageSource $source -PayloadDirectory $payload
    Write-FixtureReceipt $receipt
    Test-FixtureReceipt
    $original = [IO.File]::ReadAllText($receiptPath)

    foreach ($field in @('repositoryCommit', 'configuration', 'platform', 'sdkVersion')) {
        $changed = $original | ConvertFrom-Json -AsHashtable
        $changed.context[$field] = 'unexpected'
        Write-FixtureReceipt $changed
        Assert-Rejected { Test-FixtureReceipt } "context mismatch: $field"
    }
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.context.inputs[0].sha256 = '0' * 64
    Write-FixtureReceipt $changed
    Assert-Rejected { Test-FixtureReceipt } 'source inputs'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.files[0].path = '../escape.dll'
    Write-FixtureReceipt $changed
    Assert-Rejected { Test-FixtureReceipt } 'unsafe or duplicate path'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.files[0].path = $changed.files[1].path
    Write-FixtureReceipt $changed
    Assert-Rejected { Test-FixtureReceipt } 'payload'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.tests = @($changed.tests[0])
    Write-FixtureReceipt $changed
    Assert-Rejected { Test-FixtureReceipt } 'complete focused consumer test set'
    $changed = $original | ConvertFrom-Json -AsHashtable
    $changed.assembly = '../SharpProof.Package.Test.dll'
    Write-FixtureReceipt $changed
    Assert-Rejected { Test-FixtureReceipt } 'schema or assembly'
    [IO.File]::WriteAllText($receiptPath, $original.Replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'))
    Assert-Rejected { Test-FixtureReceipt } 'duplicate property'
    [IO.File]::WriteAllText($receiptPath, $original)

    $dependency = Join-Path $payload 'testhost.dll'
    $dependencyBytes = [IO.File]::ReadAllBytes($dependency)
    [IO.File]::WriteAllText($dependency, 'changed-dependency')
    Assert-Rejected { Test-FixtureReceipt } 'payload'
    [IO.File]::Delete($dependency)
    Assert-Rejected { Test-FixtureReceipt } 'missing runtime dependency'
    [IO.File]::WriteAllBytes($dependency, $dependencyBytes)
    $extra = Join-Path $payload 'unexpected.dll'
    [IO.File]::WriteAllText($extra, 'unexpected')
    Assert-Rejected { Test-FixtureReceipt } 'file set differs'
    [IO.File]::Delete($extra)
    $outside = Join-Path $harnessRoot 'unexpected.dll'
    [IO.File]::WriteAllText($outside, 'unexpected')
    Assert-Rejected { Test-FixtureReceipt } 'unexpected files outside'
    [IO.File]::Delete($outside)
    $intermediates = Join-Path $payload 'obj'
    [IO.Directory]::CreateDirectory($intermediates) | Out-Null
    Assert-Rejected { Test-FixtureReceipt } 'compiler intermediates'
    [IO.Directory]::Delete($intermediates)
    $link = Join-Path $payload 'linked.dll'
    [IO.File]::CreateSymbolicLink($link, $dependency) | Out-Null
    Assert-Rejected { Test-FixtureReceipt } 'must not contain links'
    [IO.File]::Delete($link)

    $package = Join-Path $source 'SharpProof.1.0.0.nupkg'
    $packageBytes = [IO.File]::ReadAllBytes($package)
    $archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Update)
    try { $null = $archive.CreateEntry('changed.txt') }
    finally { $archive.Dispose() }
    Assert-Rejected { Test-FixtureReceipt } 'packages'
    [IO.File]::WriteAllBytes($package, $packageBytes)
    Test-FixtureReceipt

    $tests = @(Get-SharpProofConsumerHarnessTests)
    $path = Write-FixtureTrx $tests @('Passed', 'Passed', 'Passed')
    Assert-SharpProofConsumerHarnessResults -Path $path
    foreach ($outcome in @('Failed', 'NotExecuted', 'Inconclusive')) {
        $path = Write-FixtureTrx $tests @('Passed', 'Passed', $outcome)
        Assert-Rejected { Assert-SharpProofConsumerHarnessResults -Path $path } 'unexpected, duplicate, skipped, or failed'
    }
    $path = Write-FixtureTrx @($tests[0]) @('Passed')
    Assert-Rejected { Assert-SharpProofConsumerHarnessResults -Path $path } 'did not execute every'
    $path = Write-FixtureTrx @($tests[0], $tests[0], $tests[2]) @('Passed', 'Passed', 'Passed')
    Assert-Rejected { Assert-SharpProofConsumerHarnessResults -Path $path } 'unexpected, duplicate, skipped, or failed'
    $path = Write-FixtureTrx $tests @('Passed', 'Passed', 'Passed') 'Aborted'
    Assert-Rejected { Assert-SharpProofConsumerHarnessResults -Path $path } 'did not complete'
    Write-Host 'Consumer harness regressions passed: commit, SDK, config, inputs, package bytes, runtime dependencies, paths, and exact executed test identities.'
}
finally {
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('/', '\')
    if ((Split-Path -Parent $temporary) -cne $parent -or
        -not ([IO.Path]::GetFileName($temporary)).StartsWith('sharpproof-consumer-harness-', [StringComparison]::Ordinal)) {
        throw 'Unexpected consumer harness fixture directory.'
    }
    if ([IO.Directory]::Exists($temporary)) { [IO.Directory]::Delete($temporary, $true) }
}
