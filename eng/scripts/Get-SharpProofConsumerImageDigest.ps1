[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ManifestPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw 'Supply a registry manifest JSON file.' }
$manifestFile = Get-Item -LiteralPath $ManifestPath
if ($manifestFile.Length -gt 1MB) { throw 'Registry manifest JSON exceeds the 1 MiB limit.' }
$manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json -AsHashtable
if ($manifest -isnot [Collections.IDictionary] -or -not $manifest.ContainsKey('digest') -or
    $manifest.digest -isnot [string] -or $manifest.digest -cnotmatch '^sha256:[a-f0-9]{64}$') {
    throw 'Invalid registry manifest digest.'
}
Write-Output $manifest.digest
