$ErrorActionPreference = 'Stop'
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
