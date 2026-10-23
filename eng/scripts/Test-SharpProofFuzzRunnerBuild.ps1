[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Fuzz runner build regressions require the canonical Linux container.'
. (Join-Path $PSScriptRoot 'SharpProof.FuzzRunnerBuild.ps1')

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-fuzz-runner-build-' + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $fixture 'artifacts/bin/SharpProof.Fuzz/Release/net10.0'
$requiredNames = @('SharpProof.Fuzz.dll', 'SharpProof.Fuzz.deps.json', 'SharpProof.Fuzz.runtimeconfig.json')
$checks = 0

function Assert-FuzzBuildRejected([scriptblock]$Action, [string]$Expected) {
    try { & $Action | Out-Null }
    catch {
        if (-not $_.Exception.Message.Contains($Expected, [StringComparison]::Ordinal)) { throw }
        return
    }
    throw "Prepared fuzz runner accepted invalid input: $Expected"
}

try {
    [IO.Directory]::CreateDirectory($output) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture '.gitignore'), "/artifacts/`n")
    [IO.File]::WriteAllText((Join-Path $fixture 'source.txt'), 'exact fixture source')
    & git -C $fixture init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize the fuzz runner fixture.' }
    & git -C $fixture add -- .gitignore source.txt
    if ($LASTEXITCODE -ne 0) { throw 'Cannot track the fuzz runner fixture.' }
    & git -C $fixture -c commit.gpgsign=false -c user.name=Fixture `
        -c user.email=fixture@example.invalid commit --quiet -m 'Fuzz runner fixture'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot commit the fuzz runner fixture.' }
    $commit = Get-SharpProofCleanFuzzSourceCommit -RepositoryRoot $fixture
    $started = [DateTime]::UtcNow.AddSeconds(-2)
    foreach ($name in @($requiredNames + 'SharpProof.Worker.dll')) {
        [IO.File]::WriteAllText((Join-Path $output $name), "fresh:$name")
    }
    $nested = Join-Path $output 'runtimes/linux-x64/native'
    [IO.Directory]::CreateDirectory($nested) | Out-Null
    [IO.File]::WriteAllText((Join-Path $nested 'libz3.so'), 'fresh native dependency')
    $receipt = New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture `
        -SourceCommit $commit -BuildStartedUtc $started
    Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $receipt
    if (-not $receipt.IsReadOnly -or $receipt.files.Length -ne 5 -or
        @($receipt.files | Where-Object { $_.path -ceq 'runtimes/linux-x64/native/libz3.so' }).Count -ne 1) {
        throw 'The fuzz runner receipt must freeze the complete fixed runtime output.'
    }
    $checks++

    Assert-FuzzBuildRejected {
        New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture `
            -SourceCommit $commit -BuildStartedUtc ([DateTime]::UtcNow.AddMinutes(1))
    } 'past UTC build start'
    Assert-FuzzBuildRejected {
        New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture `
            -SourceCommit $commit -BuildStartedUtc ([DateTime]::SpecifyKind($started, [DateTimeKind]::Unspecified))
    } 'past UTC build start'
    $checks += 2

    foreach ($name in $requiredNames) {
        $path = Join-Path $output $name
        $contents = [IO.File]::ReadAllText($path)
        [IO.File]::SetLastWriteTimeUtc($path, $started.AddSeconds(-1))
        Assert-FuzzBuildRejected {
            New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -BuildStartedUtc $started
        } 'missing, empty, or stale'
        [IO.File]::WriteAllText($path, $contents)
        [IO.File]::Delete($path)
        Assert-FuzzBuildRejected {
            New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -BuildStartedUtc $started
        } 'missing, empty, or stale'
        [IO.File]::WriteAllText($path, '')
        Assert-FuzzBuildRejected {
            New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -BuildStartedUtc $started
        } 'missing, empty, or stale'
        [IO.File]::WriteAllText($path, $contents)
        $checks += 3
    }
    Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $receipt

    foreach ($relative in @('SharpProof.Fuzz.dll', 'SharpProof.Worker.dll', 'runtimes/linux-x64/native/libz3.so')) {
        $path = Join-Path $output $relative
        $contents = [IO.File]::ReadAllText($path)
        [IO.File]::WriteAllText($path, ('x' * $contents.Length))
        Assert-FuzzBuildRejected {
            Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $receipt
        } 'runtime file identity changed'
        [IO.File]::WriteAllText($path, $contents)
        $checks++
    }
    $dependency = Join-Path $output 'SharpProof.Worker.dll'
    [IO.File]::Delete($dependency)
    Assert-FuzzBuildRejected {
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $receipt
    } 'runtime file inventory changed'
    [IO.File]::WriteAllText($dependency, 'fresh:SharpProof.Worker.dll')
    $extra = Join-Path $output 'unexpected.dll'
    [IO.File]::WriteAllText($extra, 'unexpected runtime input')
    Assert-FuzzBuildRejected {
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $receipt
    } 'runtime file inventory changed'
    [IO.File]::Delete($extra)
    $checks += 2

    $link = Join-Path $output 'redirected.dll'
    [void](New-Item -ItemType SymbolicLink -Path $link -Target (Join-Path $fixture 'source.txt'))
    Assert-FuzzBuildRejected {
        New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -BuildStartedUtc $started
    } 'must not contain links'
    [IO.File]::Delete($link)
    $checks++

    $incomplete = [ordered]@{
        repositoryRoot = $fixture
        sourceCommit   = $commit
        files          = $receipt.files
    }
    Assert-FuzzBuildRejected {
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $incomplete
    } 'unexpected property set'
    $checks++

    Assert-FuzzBuildRejected {
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit ('0' * 40) -Receipt $receipt
    } 'different source workspace or build'
    Assert-FuzzBuildRejected {
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot ([IO.Path]::GetTempPath()) -SourceCommit $commit -Receipt $receipt
    } 'different source workspace or build'
    Assert-FuzzBuildRejected {
        New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit 'master' -BuildStartedUtc $started
    } 'source commit does not match'
    $checks += 3

    $source = Join-Path $fixture 'source.txt'
    [IO.File]::WriteAllText($source, 'changed source')
    Assert-FuzzBuildRejected {
        Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $receipt
    } 'clean repository source tree'
    [IO.File]::WriteAllText($source, 'exact fixture source')
    $untracked = Join-Path $fixture 'untracked-source.txt'
    [IO.File]::WriteAllText($untracked, 'untracked source')
    Assert-FuzzBuildRejected {
        New-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -BuildStartedUtc $started
    } 'clean repository source tree'
    [IO.File]::Delete($untracked)
    $checks += 2
    Assert-SharpProofFuzzRunnerBuildReceipt -RepositoryRoot $fixture -SourceCommit $commit -Receipt $receipt
}
finally {
    if ([IO.Directory]::Exists($fixture)) { [IO.Directory]::Delete($fixture, $true) }
}
Write-Host "Fuzz runner build regressions passed: $checks fresh-build, exact-source, and runtime identity controls."
