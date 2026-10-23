[CmdletBinding()]
param(
    [switch]$VerifyNoChanges,

    [ValidateSet(1, 2)][int]$ShardCount = 1,

    [ValidateRange(0, 1)][int]$ShardIndex = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$wrapperPath = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
if ($ShardIndex -ge $ShardCount -or ($ShardCount -gt 1 -and -not $VerifyNoChanges)) {
    throw 'Formatting shards require verification mode and an index within their count.'
}
$filterPath = $null
$formattingPlan = $null

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

    $workspace = 'SharpProof.slnx'
    if ($ShardCount -gt 1) {
        Import-Module (Join-Path $PSScriptRoot 'SharpProof.CSharpFormatting.psm1') -Force
        $formattingPlan = Get-SharpProofCSharpFormattingPlan -RepositoryRoot $repositoryRoot -ShardCount $ShardCount -ShardIndex $ShardIndex
        $sourcePaths = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($formattingPlan.sourceFiles | ForEach-Object { $_.path }))
        # Roslyn resolves this filter back to the original solution and opens
        # project references as source. Each selected root retains its complete
        # compilation, linked documents, analyzer references and additional files.
        $filterPath = Join-Path $repositoryRoot ('.sharpproof-format-' + [Guid]::NewGuid().ToString('N') + '.slnf')
        ([ordered]@{ solution = ([ordered]@{ path = 'SharpProof.slnx'; projects = $formattingPlan.selectedProjects }).AsReadOnly() }).AsReadOnly() |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $filterPath -Encoding utf8NoBOM
        $workspace = $filterPath
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
            $workspace,
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
    if ($null -ne $formattingPlan) {
        Write-SharpProofCSharpFormattingReceipt -RepositoryRoot $repositoryRoot -Plan $formattingPlan
    }
}
finally {
    if ($null -ne $filterPath -and (Test-Path -LiteralPath $filterPath)) {
        Remove-Item -LiteralPath $filterPath -Force
    }
    Pop-Location
}

if ($VerifyNoChanges) {
    Write-Host 'Repository C# formatting is correct.'
}
else {
    Write-Host 'Applied standard dotnet C# formatting across the repository.'
}
