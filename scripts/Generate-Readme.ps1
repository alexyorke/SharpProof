[CmdletBinding()]
param(
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$documents = @(
    'README.md',
    'SEMANTICS.md',
    'docs\architecture-v2.md',
    'eng\acceptance\v2\README.md'
)
$requiredReadmeText = @(
    '0.2.0-preview.1',
    'sharpproof_mode',
    'SharpProofVerify=true',
    'SharpProof.Worker'
)
$retiredReadmeText = @(
    'SharpProof.Symbolic',
    'SharpProof.ProofCore',
    'SharpProof.SymbolicCli',
    'ExpectedComplexity'
)

foreach ($relativePath in $documents) {
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required maintained document is missing: $relativePath"
    }
    $content = Get-Content -LiteralPath $path -Raw
    if ($content.Contains("`r")) {
        throw "Maintained document must use LF line endings: $relativePath"
    }
}

$readme = Get-Content -LiteralPath (Join-Path $repositoryRoot 'README.md') -Raw
foreach ($required in $requiredReadmeText) {
    if (-not $readme.Contains($required, [StringComparison]::Ordinal)) {
        throw "README.md is missing required v2 text: $required"
    }
}
foreach ($retired in $retiredReadmeText) {
    if ($readme.Contains($retired, [StringComparison]::Ordinal)) {
        throw "README.md still advertises retired surface: $retired"
    }
}

if ($Verify) {
    Write-Host 'Maintained SharpProof v2 documentation is current.'
}
else {
    Write-Host 'Documentation is hand-maintained; validation passed.'
}
