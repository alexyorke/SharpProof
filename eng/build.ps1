$ErrorActionPreference = 'Stop'
$legacyProfiles = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('quick', 'check', 'pr', 'nightly', 'security', 'coverage',
        'build', 'test', 'test-changed', 'pack', 'samples', 'corpus',
        'fuzz-nightly', 'dependency-audit'))
if ($args.Count -gt 0 -and
    ([string]$args[0] -in $legacyProfiles -or [string]$args[0] -in @('-Profile', '-BuildProfile'))) {
    & (Join-Path $PSScriptRoot 'Invoke-SharpProof.ps1') @args
    exit $LASTEXITCODE
}
if ($env:SHARPPROOF_CONTAINER -eq '1') {
    & bash (Join-Path $PSScriptRoot 'build.sh') @args
}
else {
    $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    Push-Location $repositoryRoot
    try {
        & docker compose build tooling
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        & (Join-Path $PSScriptRoot 'scripts/Invoke-SharpProof.ps1') arcade @args
    }
    finally { Pop-Location }
}
exit $LASTEXITCODE
