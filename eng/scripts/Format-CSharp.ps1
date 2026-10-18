[CmdletBinding()]
param(
    [switch]$VerifyNoChanges
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$wrapperPath = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'

function Invoke-DotnetFormat {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $effectiveArguments =
    [Collections.Generic.List[string]]::new($Arguments)
    if ($VerifyNoChanges) {
        $effectiveArguments.Add('--verify-no-changes')
    }
    & $wrapperPath `
        -TimeoutSeconds 900 `
        @effectiveArguments
    if ($LASTEXITCODE -ne 0) {
        throw (
            "dotnet format failed with exit code " +
            "$LASTEXITCODE.")
    }
}

Push-Location $repositoryRoot
try {
    $sourcePaths = @(git ls-files -- '*.cs')
    if ($LASTEXITCODE -ne 0 -or $sourcePaths.Count -eq 0) {
        throw 'Formatting requires a Git checkout with tracked C# sources.'
    }

    & $wrapperPath -TimeoutSeconds 900 restore SharpProof.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw 'Solution restore failed before formatting.'
    }

    # Folder mode covers every tracked C# file, including samples, templates,
    # script helpers and golden fixtures outside the main solution. Restrict
    # generated formatting to tracked source so bin/obj output stays excluded.
    $whitespaceArguments = @(
        'format',
        'whitespace',
        $repositoryRoot,
        '--folder',
        '--include-generated',
        '--verbosity',
        'minimal',
        '--include'
    )
    $solutionArguments = @(
        'format',
        'SharpProof.slnx',
        '--severity',
        'warn',
        '--no-restore',
        '--include-generated',
        '--verbosity',
        'minimal',
        '--include'
    )
    Invoke-DotnetFormat -Arguments ($whitespaceArguments + $sourcePaths)
    Invoke-DotnetFormat -Arguments ($solutionArguments + $sourcePaths)
}
finally {
    Pop-Location
}

if ($VerifyNoChanges) {
    Write-Host 'Repository C# formatting is correct.'
}
else {
    Write-Host 'Applied standard dotnet C# formatting across the repository.'
}
