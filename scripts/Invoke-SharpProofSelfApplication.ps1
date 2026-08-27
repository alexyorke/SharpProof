[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateRange(60, 86400)]
    [int]$TimeoutSeconds = 1800,

    [switch]$IncludeMetaAnalyzers
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'SharpProof self-application requires the canonical Linux container.'
}

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$parallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$dotnetWrapper = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
$temporaryDirectory = Join-Path (
    [IO.Path]::GetTempPath()) (
        'sharpproof-self-application-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
$payloadDirectory = Join-Path $temporaryDirectory 'payload'
$baselineLog = Join-Path $temporaryDirectory 'baseline.log'
$selfApplicationLog = Join-Path $temporaryDirectory 'self-application.log'

function Invoke-RequiredDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$LogPath
    )

    & $dotnetWrapper `
        -TimeoutSeconds $TimeoutSeconds `
        -OutputPath $LogPath `
        @Arguments
    if ($LASTEXITCODE -ne 0) {
        if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
            $diagnosticLines = @(
                Select-String `
                    -LiteralPath $LogPath `
                    -Pattern '\b(?:SP(?:CF|META)?\d{3,4}|CS(?:8032|0006))\b' |
                    ForEach-Object { $_.Line.Trim() } |
                    Select-Object -Unique)
            if ($diagnosticLines.Count -gt 0) {
                $diagnosticLines | ForEach-Object { Write-Error $_ }
            }
            else {
                Get-Content -LiteralPath $LogPath -Tail 80 |
                    ForEach-Object { Write-Error $_ }
            }
        }
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$buildArguments = @(
    'build', 'SharpProof.sln',
    '--configuration', $Configuration,
    '--no-restore',
    "/m:$parallelism",
    '/p:EnableNETAnalyzers=false',
    '/p:UseSharedCompilation=false',
    '/p:TreatWarningsAsErrors=false',
    '--nologo'
)
$includeMetaValue = if ($IncludeMetaAnalyzers) { 'true' } else { 'false' }
try {
    Write-Host (
        "Self-application baseline: configuration=$Configuration, " +
        "container-visible build lanes=$parallelism")
    Invoke-RequiredDotnet `
        -Arguments @('restore', 'SharpProof.sln', '--locked-mode', '--nologo') `
        -LogPath $baselineLog

    $baselineArguments = @(
        $buildArguments + '/p:SharpProofSelfApply=false')
    Invoke-RequiredDotnet `
        -Arguments $baselineArguments `
        -LogPath $baselineLog

    [IO.Directory]::CreateDirectory($payloadDirectory) | Out-Null
    $payloadSources = @(
        [pscustomobject]@{
            Source = Join-Path $repositoryRoot (
                "SharpProof.Analyzer/bin/$Configuration/netstandard2.0/SharpProof.Analyzer.dll")
            Name = 'SharpProof.Analyzer.dll'
        },
        [pscustomobject]@{
            Source = Join-Path $repositoryRoot (
                "SharpProof.ContractForGenerator/bin/$Configuration/netstandard2.0/SharpProof.ContractForGenerator.dll")
            Name = 'SharpProof.ContractForGenerator.dll'
        }
    )
    $corePayloadNames = @(
        'SharpProof.Analyzer.Core.dll',
        'SharpProof.Contracts.dll',
        'SharpProof.Dataflow.dll',
        'SharpProof.Effects.dll',
        'SharpProof.Frontend.dll',
        'SharpProof.Ir.dll',
        'SharpProof.Specs.dll',
        'System.Buffers.dll',
        'System.Collections.Immutable.dll',
        'System.Memory.dll',
        'System.Numerics.Vectors.dll',
        'System.Reflection.Metadata.dll',
        'System.Runtime.CompilerServices.Unsafe.dll',
        'System.Text.Encoding.CodePages.dll',
        'System.Threading.Tasks.Extensions.dll'
    )
    foreach ($name in $corePayloadNames) {
        $payloadSources += [pscustomobject]@{
            Source = Join-Path $repositoryRoot (
                "SharpProof.Analyzer.Core/bin/$Configuration/netstandard2.0/$name")
            Name = $name
        }
    }
    if ($IncludeMetaAnalyzers) {
        $payloadSources += [pscustomobject]@{
            Source = Join-Path $repositoryRoot (
                "SharpProof.Meta.Analyzers/bin/$Configuration/netstandard2.0/SharpProof.Meta.Analyzers.dll")
            Name = 'SharpProof.Meta.Analyzers.dll'
        }
    }
    foreach ($payload in $payloadSources) {
        if (-not (Test-Path -LiteralPath $payload.Source -PathType Leaf)) {
            throw "Baseline build did not produce the self-application payload: $($payload.Source)"
        }
        Copy-Item -LiteralPath $payload.Source -Destination (
            Join-Path $payloadDirectory $payload.Name)
    }

    Write-Host 'Self-application analyzer/generator build: loading baseline payloads.'
    $selfApplicationArguments = @(
        $buildArguments +
        '/p:SharpProofSelfApply=true' +
        "/p:SharpProofSelfApplyIncludeMetaAnalyzers=$includeMetaValue" +
        "/p:SharpProofSelfApplyPayloadDirectory=$payloadDirectory")
    Invoke-RequiredDotnet `
        -Arguments $selfApplicationArguments `
        -LogPath $selfApplicationLog

    $sharpProofDiagnostics = @(
        Select-String `
            -LiteralPath $selfApplicationLog `
            -Pattern '\bSP(?:CF|META)?\d{3,4}\b' |
            ForEach-Object { $_.Line.Trim() } |
            Select-Object -Unique)
    $loadDiagnostics = @(
        Select-String `
            -LiteralPath $selfApplicationLog `
            -Pattern '\bCS(?:8032|0006)\b' |
            ForEach-Object { $_.Line.Trim() } |
            Select-Object -Unique)
    if ($sharpProofDiagnostics.Count -gt 0) {
        Write-Error (
            'Self-application produced SharpProof diagnostics. ' +
            'Intentional invalid fixtures must be narrowly documented; ' +
            'do not suppress production findings.')
        $sharpProofDiagnostics | ForEach-Object { Write-Error $_ }
        throw 'SharpProof self-application did not produce a clean analyzer pass.'
    }
    if ($loadDiagnostics.Count -gt 0) {
        $loadDiagnostics | ForEach-Object { Write-Error $_ }
        throw 'SharpProof self-application encountered an analyzer load or dependency diagnostic.'
    }

    Write-Host 'SharpProof self-application passed with no SP, SPCF, SPMETA, CS8032, or CS0006 diagnostics.'
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory -PathType Container) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
