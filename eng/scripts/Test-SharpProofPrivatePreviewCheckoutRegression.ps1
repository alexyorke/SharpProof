[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Private checkout regressions require the canonical container.'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-private-checkout-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $scratch 'repository'
[void][IO.Directory]::CreateDirectory($fixture)
$environmentNames = @('SHARPPROOF_SOURCE_COMMIT', 'GITHUB_OUTPUT', 'NEXT_ACTION_SENTINEL', 'GIT_DIR', 'GIT_COMMON_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_NAMESPACE', 'GITHUB_REPOSITORY', 'GITHUB_EVENT_NAME', 'GITHUB_REF', 'SHARPPROOF_PRIVATE_PREVIEW', 'SHARPPROOF_WORKFLOW_COMMIT', 'GITHUB_WORKFLOW_SHA', 'GITHUB_WORKSPACE', 'RUNNER_TEMP', 'COMPOSE_PROJECT_NAME', 'PATH', 'DOCKER_MOCK_LOG', 'MOCK_MISSING_PAYLOAD', 'ACTIONS_RUNTIME_TOKEN', 'ACTIONS_CACHE_URL', 'ACTIONS_RUNTIME_URL', 'ACTIONS_RESULTS_URL')
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    if ($name -cne 'PATH') { Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue }
}
$controls = 0
function Invoke-FixtureGit([string[]]$Arguments, [string]$Directory = $fixture) {
    $observed = @(& git -C $Directory -c core.hooksPath=/dev/null @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Disposable Git fixture command failed: $($observed -join ' ')" }
    return ($observed -join "`n")
}
function Test-CheckoutGuard([string]$Name, [string]$ExpectedSource, [bool]$ShouldPass, [string]$Directory = $fixture) {
    $env:SHARPPROOF_SOURCE_COMMIT = $ExpectedSource
    $env:GITHUB_OUTPUT = Join-Path $scratch "$Name.outputs"
    $env:NEXT_ACTION_SENTINEL = Join-Path $scratch "$Name.next-action"
    Push-Location $Directory
    try {
        & bash $guardPath *> (Join-Path $scratch "$Name.log")
        $observedExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($ShouldPass) {
        if ($observedExit -ne 0 -or -not (Test-Path -LiteralPath $env:NEXT_ACTION_SENTINEL) -or
            [IO.File]::ReadAllText($env:GITHUB_OUTPUT) -cne "qualified_source=yes`n") { throw "Qualified checkout did not reach preparation: $Name" }
    }
    elseif ($observedExit -eq 0 -or (Test-Path -LiteralPath $env:NEXT_ACTION_SENTINEL) -or (Test-Path -LiteralPath $env:GITHUB_OUTPUT)) {
        throw "Unqualified checkout reached outputs or subsequent action execution: $Name"
    }
    $script:controls++
}
function Get-WorkflowRunBlock([string]$Name) {
    $result = [Collections.Generic.List[string]]::new()
    $insideStep = $false
    $insideRun = $false
    foreach ($line in [IO.File]::ReadAllLines($workflowPath)) {
        if ($line -ceq "      - name: $Name") { $insideStep = $true; continue }
        if (-not $insideStep) { continue }
        if (-not $insideRun) {
            if ($line -ceq '        run: |') { $insideRun = $true }
            elseif ($line.StartsWith('      - ', [StringComparison]::Ordinal)) { break }
            continue
        }
        if ($line.StartsWith('          ', [StringComparison]::Ordinal)) { $result.Add($line.Substring(10)) }
        elseif ($line.Length -eq 0) { $result.Add('') }
        else { break }
    }
    if ($result.Count -lt 6) { throw "Cannot locate actual private workflow block: $Name" }
    return $result.ToArray()
}
function Test-InlinePreparation([string]$Name, [bool]$ShouldPass, [scriptblock]$Mutation = $null, [switch]$MayInvokeDocker) {
    $caseRoot = Join-Path $scratch "preparation-$Name"
    [void][IO.Directory]::CreateDirectory((Join-Path $caseRoot 'runner'))
    $env:GITHUB_REPOSITORY = 'alexyorke/SharpProof-preview'
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:SHARPPROOF_PRIVATE_PREVIEW = 'true'
    $env:SHARPPROOF_SOURCE_COMMIT = $second
    $env:SHARPPROOF_WORKFLOW_COMMIT = $privateWorkflow
    $env:GITHUB_WORKFLOW_SHA = $privateWorkflow
    $env:GITHUB_WORKSPACE = $fixture
    $env:RUNNER_TEMP = Join-Path $caseRoot 'runner'
    $env:GITHUB_OUTPUT = Join-Path $caseRoot 'preparation.outputs'
    $env:COMPOSE_PROJECT_NAME = 'synthetic-private-preparation'
    $env:DOCKER_MOCK_LOG = Join-Path $caseRoot 'docker.calls'
    $env:MOCK_MISSING_PAYLOAD = 'false'
    $env:PATH = $stubDirectory + [IO.Path]::PathSeparator + $previousEnvironment['PATH']
    foreach ($cacheName in @('ACTIONS_RUNTIME_TOKEN', 'ACTIONS_CACHE_URL', 'ACTIONS_RUNTIME_URL', 'ACTIONS_RESULTS_URL')) {
        [Environment]::SetEnvironmentVariable($cacheName, 'SYNTHETIC-FIXTURE-ONLY')
    }
    if ($null -ne $Mutation) { & $Mutation }
    Push-Location $fixture
    try {
        & bash $preparationPath *> (Join-Path $caseRoot 'preparation.log')
        $observedExit = $LASTEXITCODE
    }
    finally { Pop-Location }
    $outputPath = Join-Path $caseRoot 'preparation.outputs'
    if ($ShouldPass) {
        $expected = 'windows-native-path=' + (Join-Path $caseRoot 'runner/sharpproof-private-windows-native') + "`n"
        if ($observedExit -ne 0 -or -not (Test-Path -LiteralPath $outputPath) -or
            [IO.File]::ReadAllText($outputPath) -cne $expected -or
            [IO.File]::ReadAllLines($env:DOCKER_MOCK_LOG).Count -ne 4) { throw "Actual inline preparation failed: $Name" }
    }
    elseif ($observedExit -eq 0 -or (Test-Path -LiteralPath $outputPath) -or
        (-not $MayInvokeDocker -and (Test-Path -LiteralPath $env:DOCKER_MOCK_LOG))) { throw "Rejected preparation reached outputs or execution: $Name" }
    $script:controls++
}
try {
    $workflowPath = Join-Path $PSScriptRoot '../../.github/workflows/private-preview.yml'
    $guard = [Collections.Generic.List[string]]::new([string[]]@(Get-WorkflowRunBlock 'Require checkout to equal the approved immutable source SHA'))
    $guard.AddRange([string[]]@('printf ''%s\n'' ''qualified_source=yes'' >> "$GITHUB_OUTPUT"', 'printf ''%s\n'' ''next-action-executed'' > "$NEXT_ACTION_SENTINEL"'))
    $guardPath = Join-Path $scratch 'guard.sh'
    [IO.File]::WriteAllLines($guardPath, $guard, [Text.UTF8Encoding]::new($false))
    [void](Invoke-FixtureGit @('init', '--initial-branch=master'))
    [void](Invoke-FixtureGit @('remote', 'add', 'origin', 'https://github.com/alexyorke/SharpProof-preview'))
    [IO.File]::WriteAllText((Join-Path $fixture 'fixture.txt'), 'first fixture revision')
    [void](Invoke-FixtureGit @('add', '--', 'fixture.txt'))
    [void](Invoke-FixtureGit @('-c', 'user.name=SharpProof synthetic fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '--no-gpg-sign', '-m', 'Synthetic first revision'))
    $first = Invoke-FixtureGit @('rev-parse', 'HEAD')
    Test-CheckoutGuard 'exact-approved-source' $first $true
    [void](Invoke-FixtureGit @('remote', 'set-url', 'origin', 'https://github.com/alexyorke/SharpProof-preview.git'))
    Test-CheckoutGuard 'canonical-git-suffix' $first $true
    [IO.File]::WriteAllText((Join-Path $fixture 'fixture.txt'), 'second fixture revision')
    [void](Invoke-FixtureGit @('add', '--', 'fixture.txt'))
    [void](Invoke-FixtureGit @('-c', 'user.name=SharpProof synthetic fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '--no-gpg-sign', '-m', 'Synthetic moved master'))
    $second = Invoke-FixtureGit @('rev-parse', 'HEAD')
    [void](Invoke-FixtureGit @('checkout', '--detach', $first))
    Test-CheckoutGuard 'frozen-source-after-master-moves' $first $true
    [void](Invoke-FixtureGit @('checkout', '--detach', $second))
    Test-CheckoutGuard 'checkout-differs-from-approved-source' $first $false
    Test-CheckoutGuard 'unapproved-sha' ('0' * 40) $false
    Test-CheckoutGuard 'newline-sha' ($second + "`n") $false
    Test-CheckoutGuard 'empty-sha' '' $false
    Test-CheckoutGuard 'nonhex-sha' ('x' * 40) $false
    [void](Invoke-FixtureGit @('remote', 'set-url', 'origin', 'https://github.com/other/SharpProof'))
    Test-CheckoutGuard 'foreign-origin' $second $false
    $empty = Join-Path $scratch 'not-a-repository'
    [void][IO.Directory]::CreateDirectory($empty)
    Test-CheckoutGuard 'missing-git-checkout' $second $false $empty
    [void](Invoke-FixtureGit @('remote', 'set-url', 'origin', 'https://github.com/alexyorke/SharpProof-preview'))
    Test-CheckoutGuard 'new-exact-approved-source' $second $true
    $orchestration = Join-Path $fixture 'artifacts/private-release-orchestration'
    [void][IO.Directory]::CreateDirectory($orchestration)
    [void](Invoke-FixtureGit @('init', '--initial-branch=main') $orchestration)
    [IO.File]::WriteAllText((Join-Path $orchestration 'fixture.txt'), 'Synthetic distinct orchestration revision')
    [void](Invoke-FixtureGit @('add', '--', 'fixture.txt') $orchestration)
    [void](Invoke-FixtureGit @('-c', 'user.name=SharpProof synthetic fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '--no-gpg-sign', '-m', 'Synthetic private orchestration') $orchestration)
    $privateWorkflow = Invoke-FixtureGit @('rev-parse', 'HEAD') $orchestration
    $preparationPath = Join-Path $scratch 'preparation.sh'
    [IO.File]::WriteAllLines($preparationPath, [string[]]@(Get-WorkflowRunBlock 'Build the canonical source toolchain without shared caches'), [Text.UTF8Encoding]::new($false))
    $stubDirectory = Join-Path $scratch 'stub-bin'
    [void][IO.Directory]::CreateDirectory($stubDirectory)
    $dockerStub = Join-Path $stubDirectory 'docker'
    $stub = @'
#!/usr/bin/env bash
set -euo pipefail
[[ ! -v ACTIONS_RUNTIME_TOKEN && ! -v ACTIONS_CACHE_URL && ! -v ACTIONS_RUNTIME_URL && ! -v ACTIONS_RESULTS_URL ]]
printf '%s\n' "$*" >> "$DOCKER_MOCK_LOG"
case "$1" in
  buildx) [[ "$2" == build && " $* " == *' --no-cache '* && "$*" != *--cache-from* && "$*" != *--cache-to* ]] ;;
  create) printf '%064d\n' 0 ;;
  cp)
    mkdir -p "$3/win-x64" "$3/win-arm64"
    printf 'synthetic x64 payload' > "$3/win-x64/libz3.dll"
    if [[ "$MOCK_MISSING_PAYLOAD" != true ]]; then printf 'synthetic arm64 payload' > "$3/win-arm64/libz3.dll"; fi
    ;;
  rm) ;;
  *) exit 93 ;;
esac
'@
    [IO.File]::WriteAllText($dockerStub, $stub.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
    & chmod 700 $dockerStub
    if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the synthetic Docker boundary.' }
    Test-InlinePreparation 'valid-pinned-context-cache-cleared' $true
    Test-InlinePreparation 'wrong-source' $false { $env:SHARPPROOF_SOURCE_COMMIT = '0' * 40 }
    Test-InlinePreparation 'wrong-orchestration' $false { $env:SHARPPROOF_WORKFLOW_COMMIT = '0' * 40; $env:GITHUB_WORKFLOW_SHA = '0' * 40 }
    Test-InlinePreparation 'wrong-workflow-context' $false { $env:GITHUB_WORKFLOW_SHA = '0' * 40 }
    Test-InlinePreparation 'foreign-repository' $false { $env:GITHUB_REPOSITORY = 'other/SharpProof-preview' }
    Test-InlinePreparation 'root-runner-temp' $false { $env:RUNNER_TEMP = '/' }
    Test-InlinePreparation 'missing-output' $false { Remove-Item Env:\GITHUB_OUTPUT }
    Test-InlinePreparation 'preexisting-native-path' $false { [void][IO.Directory]::CreateDirectory((Join-Path $env:RUNNER_TEMP 'sharpproof-private-windows-native')) }
    Test-InlinePreparation 'missing-native-payload' $false { $env:MOCK_MISSING_PAYLOAD = 'true' } -MayInvokeDocker
    Write-Host "Private checkout and inline preparation regressions passed: $controls controls against real disposable Git and actual workflow scripts. Docker mocked; no network, build, or publication."
}
finally {
    foreach ($name in $environmentNames) {
        if ($null -eq $previousEnvironment[$name]) { Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
    }
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-private-checkout-[a-f0-9]{32}\z') { throw 'Unsafe checkout fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
