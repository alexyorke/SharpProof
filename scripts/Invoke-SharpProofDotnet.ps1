[CmdletBinding()]
param(
    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 0,

    [Parameter()]
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 0,

    [Parameter(Mandatory = $true, Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$DotnetArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'JobObjectHelpers.ps1')

$effectiveDotnetArgs = [System.Collections.Generic.List[string]]::new()
foreach ($argument in $DotnetArgs)
{
    $effectiveDotnetArgs.Add($argument)
}

if ($effectiveDotnetArgs.Count -gt 0)
{
    $msbuildBackedCommands = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($commandName in @('build', 'clean', 'msbuild', 'pack', 'publish', 'restore', 'test'))
    {
        [void]$msbuildBackedCommands.Add($commandName)
    }

    if ($msbuildBackedCommands.Contains($effectiveDotnetArgs[0]))
    {
        if (-not $effectiveDotnetArgs.Contains('/nodeReuse:false'))
        {
            $effectiveDotnetArgs.Add('/nodeReuse:false')
        }

        if (-not $effectiveDotnetArgs.Contains('-p:UseSharedCompilation=false'))
        {
            $effectiveDotnetArgs.Add('-p:UseSharedCompilation=false')
        }
    }
}

$exitCode = Invoke-ProcessUnderJobObject `
    -FilePath 'dotnet' `
    -ArgumentList $effectiveDotnetArgs `
    -MemoryLimitMb $MemoryLimitMb `
    -TimeoutSeconds $TimeoutSeconds `
    -WorkingDirectory (Get-Location).Path

exit $exitCode
