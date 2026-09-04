[CmdletBinding()]
param(
    [Parameter()][string]$CatalogPath,
    [Parameter()][string]$OutputPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

function Assert-UniqueCatalogKey {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()]
        [Collections.Generic.HashSet[string]]$Seen,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$DuplicateMessage
    )

    if (-not $Seen.Add($Key)) {
        throw $DuplicateMessage
    }
}

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$CatalogPath = Resolve-SharpProofPath $CatalogPath (
    Join-Path $repositoryRoot 'SharpProof.Analyzer.Core\AnalyzerDiagnostic.catalog.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Analyzer.Core\AnalyzerDiagnosticCatalog.generated.cs')
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
if ([int]$catalog.schemaVersion -ne 1) {
    throw 'Only analyzer-diagnostic catalog schema version 1 is supported.'
}

$lines = New-SharpProofGeneratedHeader `
    -Generator 'Generate-AnalyzerDiagnosticCatalog.ps1' `
    -Source 'SharpProof.Analyzer.Core/AnalyzerDiagnostic.catalog.json.' `
    -Notes @('Declarative diagnostic projections only; analysis remains handwritten.') `
    -Nullable
$lines.Add('')
$lines.Add('namespace SharpProof.Analyzer;')
$lines.Add('')
$lines.Add('internal static class AnalyzerDiagnosticCatalog')
$lines.Add('{')
$lines.Add('    internal static (string Argument, string Reason) DescribeIntrinsicViolation(')
$lines.Add('        ContractBindingFailure failure, bool isOld)')
$lines.Add('    {')
$lines.Add('        return (failure, isOld) switch')
$lines.Add('        {')
$seenIntrinsic = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in @($catalog.intrinsicDescriptions)) {
    Assert-Identifier ([string]$entry.failure) 'Analyzer diagnostic intrinsic failure'
    $key = ([string]$entry.failure) + '|' + ([bool]$entry.isOld).ToString()
    Assert-UniqueCatalogKey `
        -Seen $seenIntrinsic `
        -Key $key `
        -DuplicateMessage "Analyzer diagnostic intrinsic mapping repeats '$key'."
    $suffix = if ([bool]$entry.isOld) { 'true' } else { 'false' }
    $lines.Add("            (ContractBindingFailure.$($entry.failure), $suffix) => (")
    $lines.Add("                $(ConvertTo-CSharpString ([string]$entry.argument)),")
    $lines.Add("                $(ConvertTo-CSharpString ([string]$entry.reason))),")
}
$intrinsicFallback = $catalog.intrinsicFallback
$lines.Add("            _ => ($(ConvertTo-CSharpString ([string]$intrinsicFallback.argument)),")
$lines.Add("                $(ConvertTo-CSharpString ([string]$intrinsicFallback.reason)))")
$lines.Add('        };')
$lines.Add('    }')
$lines.Add('')
$lines.Add('    internal static string DescribePlacement(ContractClausePlacement placement)')
$lines.Add('    {')
$lines.Add('        return placement switch')
$lines.Add('        {')
$seenPlacement = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in @($catalog.placementDescriptions)) {
    Assert-Identifier ([string]$entry.placement) 'Analyzer diagnostic placement'
    $placement = [string]$entry.placement
    Assert-UniqueCatalogKey `
        -Seen $seenPlacement `
        -Key $placement `
        -DuplicateMessage "Analyzer diagnostic placement mapping repeats '$placement'."
    $lines.Add("            ContractClausePlacement.$($entry.placement) =>")
    $lines.Add("                $(ConvertTo-CSharpString ([string]$entry.description)),")
}
$lines.Add("            _ => $(ConvertTo-CSharpString ([string]$catalog.placementFallback))")
$lines.Add('        };')
$lines.Add('    }')
$lines.Add('}')

Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content ($lines -join "`n") `
    -DisplayPath $OutputPath `
    -GeneratorCommand '.\scripts\Generate-AnalyzerDiagnosticCatalog.ps1' `
    -Verify:$Verify
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic analyzer-diagnostic catalog."
