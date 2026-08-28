[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-package-consumer-evidence-' + [Guid]::NewGuid().ToString('N'))
$scriptRoot = Join-Path $fixture 'scripts'
$contractRoot = Join-Path $fixture 'eng/acceptance'
$reportDirectory = Join-Path $fixture 'artifacts/release-qualification'
$receiptDirectory = Join-Path $reportDirectory 'qualification-receipts'
New-Item -ItemType Directory -Path `
    $scriptRoot, $contractRoot, $receiptDirectory -Force | Out-Null

try {
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Invoke-SharpProofContainer.ps1') `
        -Destination (Join-Path $scriptRoot 'Invoke-SharpProofContainer.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.ContainerExecution.psm1') `
        -Destination (Join-Path $scriptRoot 'SharpProof.ContainerExecution.psm1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Assert-SharpProofUniqueJsonProperties.ps1') `
        -Destination (Join-Path $scriptRoot 'Assert-SharpProofUniqueJsonProperties.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'eng/acceptance/contract.json') `
        -Destination (Join-Path $contractRoot 'contract.json')

    $reportPath = Join-Path $reportDirectory 'package-consumers.json'
    $receiptPath = Join-Path $receiptDirectory 'package-consumers.json'
    [IO.File]::WriteAllText(
        $reportPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        $receiptPath,
        '{"status":"passed","commit":"stale"}',
        [Text.UTF8Encoding]::new($false))

    $oldContainer = $env:SHARPPROOF_CONTAINER
    try {
        $env:SHARPPROOF_CONTAINER = '1'
        $output = & pwsh -NoLogo -NoProfile -File (
            Join-Path $scriptRoot 'Invoke-SharpProofContainer.ps1') `
            -Command package-consumers 2>&1
        if ($LASTEXITCODE -eq 0) {
            throw 'Package-consumer evidence fixture unexpectedly succeeded.'
        }
        if ((Test-Path -LiteralPath $reportPath -PathType Leaf) -or
            (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
            throw "Package-consumer evidence fixture preserved stale evidence: $output"
        }
    }
    finally {
        $env:SHARPPROOF_CONTAINER = $oldContainer
    }

    Write-Host 'Package-consumer evidence invalidation fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
