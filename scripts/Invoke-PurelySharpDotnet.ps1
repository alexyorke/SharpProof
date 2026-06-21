[CmdletBinding()]
param(
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 0,

    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$DotnetArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'JobObjectHelpers.ps1')

$exitCode = Invoke-ProcessUnderJobObject `
    -FilePath 'dotnet' `
    -ArgumentList $DotnetArgs `
    -MemoryLimitMb $MemoryLimitMb `
    -WorkingDirectory (Get-Location).Path

exit $exitCode
