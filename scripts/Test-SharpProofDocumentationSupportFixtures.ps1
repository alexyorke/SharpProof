[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'clean',
        'stale-win-x64',
        'package-version-drift',
        'support-drift',
        'stale-contract-api-silence')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$relativePath = if ($Mutation -eq 'stale-contract-api-silence') {
    'docs\diagnostic-examples.md'
}
else {
    'README.md'
}
$readmePath = Join-Path $repositoryRoot $relativePath
$originalBytes = [IO.File]::ReadAllBytes($readmePath)
try {
    $text = [Text.Encoding]::UTF8.GetString($originalBytes)
    switch ($Mutation) {
        'stale-win-x64' {
            $text += "`nSharpProof.Verifier.Win-x64 is supported.`n"
        }
        'package-version-drift' {
            [xml]$release = Get-Content -LiteralPath (
                Join-Path $repositoryRoot 'SharpProof.Release.props') -Raw
            $prefix = [string]$release.Project.PropertyGroup.SharpProofVersionPrefix
            $version = ([string]$release.Project.PropertyGroup.SharpProofPackageVersion).
                Replace('$(SharpProofVersionPrefix)', $prefix)
            $text = $text.Replace(
                $version,
                '99.99.99-stale',
                [StringComparison]::Ordinal)
        }
        'support-drift' {
            $text += "`nThe verifier is supported only on Windows x64.`n"
        }
        'stale-contract-api-silence' {
            $text += (
                "`nA readable wrong-payload SharpProof.Attributes assembly " +
                "disables contract analysis without a diagnostic.`n")
        }
    }
    [IO.File]::WriteAllText(
        $readmePath,
        $text,
        [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'Generate-Readme.ps1') -Verify
}
finally {
    [IO.File]::WriteAllBytes($readmePath, $originalBytes)
}
