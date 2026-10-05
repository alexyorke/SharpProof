[CmdletBinding()]
param(
    [Parameter()]
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 0,

    [Parameter()]
    [string]$OutputPath,

    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$DotnetArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1')
Assert-SharpProofContainer `
    'SharpProof .NET commands must run in the canonical Linux container. Use docker compose run --rm tooling <command>.'
$effectiveArguments = @(
    Add-SharpProofStaticGraphArgument -Arguments $DotnetArgs
)

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = 'dotnet'
$startInfo.WorkingDirectory = (Get-Location).Path
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
foreach ($argument in $effectiveArguments) {
    [void]$startInfo.ArgumentList.Add($argument)
}
$capture = -not [string]::IsNullOrWhiteSpace($OutputPath)
$startInfo.RedirectStandardOutput = $capture
$startInfo.RedirectStandardError = $capture

$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$started = $false
try {
    if (-not $process.Start()) {
        throw 'The dotnet process could not be started.'
    }
    $started = $true
    $standardOutput = if ($capture) {
        $process.StandardOutput.ReadToEndAsync()
    } else {
        $null
    }
    $standardError = if ($capture) {
        $process.StandardError.ReadToEndAsync()
    } else {
        $null
    }

    if ($TimeoutSeconds -gt 0 -and
        -not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        $process.WaitForExit()
        $global:LASTEXITCODE = 124
        exit 124
    }
    $process.WaitForExit()

    if ($capture) {
        $directory = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            [IO.Directory]::CreateDirectory($directory) | Out-Null
        }
        $output = $standardOutput.GetAwaiter().GetResult()
        $errorOutput = $standardError.GetAwaiter().GetResult()
        [IO.File]::WriteAllText(
            $OutputPath,
            $output + $errorOutput,
            [Text.UTF8Encoding]::new($false))
    }

    $global:LASTEXITCODE = $process.ExitCode
    exit $process.ExitCode
}
finally {
    if ($started -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }
    $process.Dispose()
}
