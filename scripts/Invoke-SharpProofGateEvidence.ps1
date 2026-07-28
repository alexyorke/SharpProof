[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('corpus', 'performance')]
    [string]$Gate,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter()]
    [ValidateRange(1, 65536)]
    [int]$MemoryLimitMb = 8192,

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedOutput = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $OutputPath))
if (-not $resolvedOutput.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the repository: $resolvedOutput"
}
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Force -Path $outputDirectory |
    Out-Null
$rawOutput = [IO.Path]::ChangeExtension(
    $resolvedOutput,
    '.gate.json')
$standardError = [IO.Path]::ChangeExtension(
    $resolvedOutput,
    '.stderr.txt')
$wrapper = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$project = Join-Path $repositoryRoot 'SharpProof.Gates\SharpProof.Gates.csproj'
$dotnetArguments = @(
    'run',
    '--project',
    $project,
    '-c',
    'Release',
    '--no-build',
    '--',
    $Gate
)
$quotedArguments = @(
    $dotnetArguments |
        ForEach-Object {
            "'" + ([string]$_).Replace("'", "''") + "'"
        }
) -join ','
$escapedWrapper = $wrapper.Replace("'", "''")
$command = (
    "& '$escapedWrapper' -MemoryLimitMb " +
    [string]$MemoryLimitMb +
    ' -TimeoutSeconds ' +
    [string]$TimeoutSeconds +
    " @($quotedArguments); exit " + '$LASTEXITCODE')
$encodedCommand = [Convert]::ToBase64String(
    [Text.Encoding]::Unicode.GetBytes($command))
$exitCode = -1
$failure = $null
$gateResult = $null
try {
    $process = Start-Process `
        -FilePath 'pwsh' `
        -ArgumentList @(
            '-NoLogo',
            '-NoProfile',
            '-EncodedCommand',
            $encodedCommand
        ) `
        -WorkingDirectory $repositoryRoot `
        -WindowStyle Hidden `
        -Wait `
        -PassThru `
        -RedirectStandardOutput $rawOutput `
        -RedirectStandardError $standardError
    $exitCode = $process.ExitCode
    if (-not (Test-Path -LiteralPath $rawOutput -PathType Leaf)) {
        $failure = 'The gate did not produce a JSON result.'
    }
    else {
        try {
            $gateResult = Get-Content -LiteralPath $rawOutput -Raw |
                ConvertFrom-Json -ErrorAction Stop
            if ($null -eq $gateResult) {
                throw 'The JSON result was null.'
            }
        }
        catch {
            $failure = 'The gate result was not valid JSON: ' +
                $_.Exception.Message
        }
    }
    if ($exitCode -ne 0 -and $null -eq $failure) {
        $failure = "The gate exited with code $exitCode."
    }
}
catch {
    $failure = 'The gate could not be started or observed: ' +
        $_.Exception.Message
}

$passed = $exitCode -eq 0 -and
    $null -ne $gateResult -and
    $null -eq $failure
$envelope = [pscustomobject][ordered]@{
    schemaVersion = 1
    gate = $Gate
    passed = $passed
    exitCode = $exitCode
    failure = $failure
    result = $gateResult
    rawOutput = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $rawOutput).Replace('\', '/')
    standardError = [IO.Path]::GetRelativePath(
        $repositoryRoot,
        $standardError).Replace('\', '/')
}
$json = ($envelope | ConvertTo-Json -Depth 20) -replace "`r`n", "`n"
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + "`n",
    [Text.UTF8Encoding]::new($false))
$json

if (-not $passed) {
    throw "SharpProof $Gate gate failed. Evidence: $resolvedOutput"
}
