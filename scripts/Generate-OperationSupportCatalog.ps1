[CmdletBinding()]
param(
    [Parameter()][string]$CatalogPath,
    [Parameter()][string]$OutputPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$CatalogPath = Resolve-SharpProofPath $CatalogPath (
    Join-Path $repositoryRoot 'SharpProof.Frontend\OperationSupport.catalog.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Frontend\OperationSupportCatalog.generated.cs')
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
if ([int]$catalog.schemaVersion -ne 1) {
    throw 'Only operation-support catalog schema version 1 is supported.'
}

$lines = New-SharpProofGeneratedHeader `
    -Generator 'Generate-OperationSupportCatalog.ps1' `
    -Source 'SharpProof.Frontend/OperationSupport.catalog.json.' `
    -Nullable
$lines.Add('')
$lines.Add('namespace SharpProof.Frontend;')
$lines.Add('')
$lines.Add('internal static class OperationSupportCatalogData')
$lines.Add('{')
foreach ($property in @('contractExpression', 'effectDiscovery')) {
    $name = if ($property -eq 'contractExpression') { 'ContractExpression' } else { 'EffectDiscovery' }
    $entries = @($catalog.PSObject.Properties[$property].Value)
    if ($entries.Count -eq 0) {
        throw "Operation-support catalog '$property' cannot be empty."
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $lines.Add("    internal static readonly OperationKind[] $name = [")
    foreach ($entry in $entries) {
        Assert-Identifier ([string]$entry) "Operation-support catalog '$property' entry"
        if (-not $seen.Add([string]$entry)) {
            throw "Operation-support catalog '$property' repeats '$entry'."
        }
        $lines.Add("        OperationKind.$entry,")
    }
    $lines.Add('    ];')
}
$lines.Add('}')

Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content ($lines -join "`n") `
    -DisplayPath $OutputPath `
    -GeneratorCommand '.\scripts\Generate-OperationSupportCatalog.ps1' `
    -Verify:$Verify
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic operation-support catalog."
