[CmdletBinding()]
param([switch]$Verify)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repositoryRoot 'docs/configuration-reference.md'
$temporaryPath = Join-Path ([System.IO.Path]::GetTempPath()) ("sharpproof-config-{0}.md" -f [Guid]::NewGuid())

try {
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1') run `
        --project (Join-Path $repositoryRoot 'Tools/SharpProof.SymbolicCli/SharpProof.SymbolicCli.csproj') `
        --configuration Release `
        --no-restore `
        -- `
        --generate-configuration-reference $temporaryPath
    if ($LASTEXITCODE -ne 0) {
        throw "Configuration reference generator failed with exit code $LASTEXITCODE."
    }

    Update-SharpProofGeneratedFile `
        -Path $outputPath `
        -Content (Get-Content -LiteralPath $temporaryPath -Raw) `
        -DisplayPath 'docs/configuration-reference.md' `
        -GeneratorCommand '.\scripts\Generate-ConfigurationReference.ps1' `
        -Verify:$Verify
}
finally {
    Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
}

if ($Verify) {
    Write-Host 'Generated configuration reference is up to date.'
    return
}

Write-Host 'Regenerated docs/configuration-reference.md.'
