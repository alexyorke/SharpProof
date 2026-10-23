[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'Private checkout regressions require the canonical container.'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-private-checkout-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $scratch 'repository'
[void][IO.Directory]::CreateDirectory($fixture)
$environmentNames = @('SHARPPROOF_SOURCE_COMMIT', 'GITHUB_OUTPUT', 'NEXT_ACTION_SENTINEL', 'GIT_DIR', 'GIT_COMMON_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY', 'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_NAMESPACE')
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue
}
$controls = 0
function Invoke-FixtureGit([string[]]$Arguments) {
    $observed = @(& git -C $fixture -c core.hooksPath=/dev/null @Arguments 2>&1)
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
try {
    $workflowPath = Join-Path $PSScriptRoot '../../.github/workflows/private-preview.yml'
    $guard = [Collections.Generic.List[string]]::new()
    $insideStep = $false
    $insideRun = $false
    foreach ($line in [IO.File]::ReadAllLines($workflowPath)) {
        if ($line -ceq '      - name: Require checkout to equal the approved immutable source SHA') { $insideStep = $true; continue }
        if (-not $insideStep) { continue }
        if (-not $insideRun) {
            if ($line -ceq '        run: |') { $insideRun = $true }
            elseif ($line.StartsWith('      - ', [StringComparison]::Ordinal)) { break }
            continue
        }
        if ($line.StartsWith('          ', [StringComparison]::Ordinal)) { $guard.Add($line.Substring(10)) }
        elseif ($line.Length -eq 0) { $guard.Add('') }
        else { break }
    }
    if ($guard.Count -lt 6) { throw 'Cannot locate the actual private qualification checkout guard.' }
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
    Write-Host "Private checkout guard regressions passed: $controls controls against real disposable Git and the actual workflow script. No network, build, or publication."
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
