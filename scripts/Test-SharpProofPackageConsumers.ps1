[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateSet('Required', 'Graceful')]
    [string]$ExpectedSmt = 'Required'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testProject = Join-Path $repositoryRoot 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
$isWindowsHost = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

Push-Location $repositoryRoot
try {
    if ($isWindowsHost) {
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1') `
            -MemoryLimitMb 6144 `
            -TimeoutSeconds 900 `
            test $testProject `
            --configuration $Configuration `
            --logger 'console;verbosity=minimal'
    }
    else {
        & dotnet test $testProject `
            --configuration $Configuration `
            --logger 'console;verbosity=minimal'
    }
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

$workerScope = if ($isWindowsHost) {
    'analyzer and out-of-process worker'
}
else {
    'analyzer (packaged worker is not supported on this host)'
}
Write-Host "SharpProof packaged $workerScope consumer passed ($ExpectedSmt host policy)."
