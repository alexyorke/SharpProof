[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string[]]$AssemblyPath = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.EmbeddedSymbols.psm1') -Force
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$prefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
if ($AssemblyPath.Count -eq 0) {
    $solution = [xml][IO.File]::ReadAllText((Join-Path $root 'SharpProof.slnx'))
    $AssemblyPath = @(
        foreach ($project in $solution.SelectNodes('//Project[@Path]')) {
            $projectPath = [IO.Path]::GetFullPath((Join-Path $root ([string]$project.Path).Replace('\', '/')))
            if (-not $projectPath.StartsWith($prefix, $comparison)) { throw 'Foreign solution project in embedded-symbol export.' }
            $output = Join-Path (Split-Path $projectPath) "bin/$Configuration"
            if (Test-Path -LiteralPath $output -PathType Container) {
                Get-ChildItem -LiteralPath $output -Filter 'SharpProof*.dll' -Recurse -File | ForEach-Object FullName
            }
        }
    ) | Sort-Object -Unique
}
$exported = 0
foreach ($assembly in $AssemblyPath) {
    $full = (Resolve-Path -LiteralPath $assembly).Path
    if (-not $full.StartsWith($prefix, $comparison) -or [IO.Path]::GetExtension($full) -cne '.dll') {
        throw 'Embedded-symbol export must stay inside the repository and name an assembly.'
    }
    $bytes = Get-SharpProofEmbeddedPdb -AssemblyPath $full
    if ($null -eq $bytes) { continue }
    $pdbPath = [IO.Path]::ChangeExtension($full, '.pdb')
    if (-not [IO.File]::Exists($pdbPath) -or
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($pdbPath))) -cne
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))) {
        [IO.File]::WriteAllBytes($pdbPath, $bytes)
    }
    $exported++
}
Write-Host "Exported exact embedded PDBs for $exported assembly outputs ($Configuration)."