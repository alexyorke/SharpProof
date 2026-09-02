[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$wrapperPath = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'

function Invoke-DotnetFormat {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $effectiveArguments =
        [Collections.Generic.List[string]]::new($Arguments)
    & $wrapperPath `
        -TimeoutSeconds 900 `
        @effectiveArguments
    if ($LASTEXITCODE -ne 0) {
        throw (
            "dotnet $($effectiveArguments -join ' ') failed with exit code " +
            "$LASTEXITCODE.")
    }
}

function Invoke-DotnetFormatForGenerated {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string[]]$GeneratedPaths
    )

    Invoke-DotnetFormat -Arguments @(
        $Arguments +
        '--include-generated' +
        '--include' +
        $GeneratedPaths
    )
}

Push-Location $repositoryRoot
try {
    $whitespaceArguments = @(
        'format',
        'whitespace',
        'SharpProof.sln',
        '--no-restore',
        '--verbosity',
        'minimal'
    )
    $styleArguments = @(
        'format',
        'style',
        'SharpProof.sln',
        '--severity',
        'warn',
        '--no-restore',
        '--verbosity',
        'minimal'
    )
    Invoke-DotnetFormat -Arguments $whitespaceArguments
    Invoke-DotnetFormat -Arguments $styleArguments

    $generatedPaths = @(git ls-files '*.generated.cs')
    if ($LASTEXITCODE -ne 0) {
        throw 'git ls-files failed while resolving generated C# sources.'
    }
    if ($generatedPaths.Count -ne 0) {
        Invoke-DotnetFormatForGenerated `
            -Arguments $whitespaceArguments `
            -GeneratedPaths $generatedPaths
        Invoke-DotnetFormatForGenerated `
            -Arguments $styleArguments `
            -GeneratedPaths $generatedPaths
    }
}
finally {
    Pop-Location
}

Write-Host 'Applied standard dotnet C# formatting.'
