[CmdletBinding()]
param(
    [Parameter()]
    [switch]$Verify
)

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
    if ($Verify) {
        $effectiveArguments.Add('--verify-no-changes')
    }
    & $wrapperPath `
        -TimeoutSeconds 900 `
        @effectiveArguments
    if ($LASTEXITCODE -ne 0) {
        throw (
            "dotnet $($effectiveArguments -join ' ') failed with exit code " +
            "$LASTEXITCODE.")
    }
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
    if (-not $Verify) {
        Invoke-DotnetFormat -Arguments $whitespaceArguments
    }

    $generatedPaths = @(git ls-files '*.generated.cs')
    if ($LASTEXITCODE -ne 0) {
        throw 'git ls-files failed while resolving generated C# sources.'
    }
    if ($generatedPaths.Count -ne 0) {
        $generatedWhitespaceArguments = @(
            $whitespaceArguments +
            '--include-generated' +
            '--include' +
            $generatedPaths
        )
        $generatedStyleArguments = @(
            $styleArguments +
            '--include-generated' +
            '--include' +
            $generatedPaths
        )
        Invoke-DotnetFormat -Arguments $generatedWhitespaceArguments
        Invoke-DotnetFormat -Arguments $generatedStyleArguments
        if (-not $Verify) {
            Invoke-DotnetFormat -Arguments $generatedWhitespaceArguments
        }
    }
}
finally {
    Pop-Location
}

$verb = if ($Verify) { 'Verified' } else { 'Applied' }
Write-Host "$verb standard dotnet C# formatting."
