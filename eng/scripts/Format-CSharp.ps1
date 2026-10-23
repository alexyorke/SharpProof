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
        [Collections.Immutable.ImmutableArray[string]]$Arguments
    )

    $effectiveArguments = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    [void]$effectiveArguments.AddRange($Arguments)
    if ($VerifyNoChanges) {
        [void]$effectiveArguments.Add('--verify-no-changes')
    }
    $nativeArguments = [string[]]@($effectiveArguments.ToImmutable())
    & $wrapperPath `
        -TimeoutSeconds 900 `
        @nativeArguments
    if ($LASTEXITCODE -ne 0) {
        throw (
            "dotnet format failed with exit code " +
            "$LASTEXITCODE.")
    }
}

Push-Location $repositoryRoot
try {
    $sourcePaths = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(git ls-files -- '*.cs'))
    if ($LASTEXITCODE -ne 0 -or $sourcePaths.Length -eq 0) {
        throw 'Formatting requires a Git checkout with tracked C# sources.'
    }

    & $wrapperPath -TimeoutSeconds 900 restore SharpProof.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw 'Solution restore failed before formatting.'
    }

    # Folder mode covers every tracked C# file, including samples, templates,
    # script helpers and golden fixtures outside the main solution. Restrict
    # generated formatting to tracked source so bin/obj output stays excluded.
    $whitespaceArgumentsBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    [void]$whitespaceArgumentsBuilder.AddRange([string[]]@(
            'format',
            'whitespace',
            $repositoryRoot,
            '--folder',
            '--include-generated',
            '--verbosity',
            'minimal',
            '--include'
        ))
    [void]$whitespaceArgumentsBuilder.AddRange([string[]]@($sourcePaths))
    $whitespaceArguments = $whitespaceArgumentsBuilder.ToImmutable()
    $solutionArgumentsBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    [void]$solutionArgumentsBuilder.AddRange([string[]]@(
            'format',
            'SharpProof.slnx',
            '--severity',
            'warn',
            '--no-restore',
            '--include-generated',
            '--verbosity',
            'minimal',
            '--include'
        ))
    [void]$solutionArgumentsBuilder.AddRange([string[]]@($sourcePaths))
    $solutionArguments = $solutionArgumentsBuilder.ToImmutable()
    Invoke-DotnetFormat -Arguments $whitespaceArguments
    Invoke-DotnetFormat -Arguments $solutionArguments
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
