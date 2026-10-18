[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,
    [Parameter(Mandatory = $true)]
    [ValidateSet('linux', 'windows', 'macos')]
    [string]$OsFamily,
    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$actualOsFamily = if ($IsLinux) {
    'linux'
}
elseif ($IsWindows) {
    'windows'
}
elseif ($IsMacOS) {
    'macos'
}
else {
    throw 'Unable to determine the executing host OS family from PowerShell runtime facts.'
}
if ($OsFamily -cne $actualOsFamily) {
    throw (
        "Portable consumer OS-family mismatch: expected '$OsFamily', " +
        "but the executing host is '$actualOsFamily'.")
}
$actualHostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$actualProcessArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if ($Architecture -cne $actualHostArchitecture -or
    $Architecture -cne $actualProcessArchitecture) {
    throw (
        "Portable consumer architecture mismatch: expected '$Architecture', " +
        "but host is '$actualHostArchitecture' and process is '$actualProcessArchitecture'.")
}
& (Join-Path $PSScriptRoot 'Test-SharpProofPackageConsumers.ps1') `
    -PackageSource $PackageSource -FrameworkConsumersOnly
if ($LASTEXITCODE -ne 0) { throw 'Portable package consumer failed.' }
