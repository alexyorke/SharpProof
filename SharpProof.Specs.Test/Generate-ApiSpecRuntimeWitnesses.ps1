[CmdletBinding()]
param(
    [Parameter()]
    [string]$CatalogPath,

    [Parameter()]
    [string]$OutputPath,

    [Parameter()]
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repositoryRoot 'scripts\GeneratedFileHelpers.ps1')

if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path `
        $repositoryRoot `
        'SharpProof.Specs\DefaultApiSpecCatalog.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path `
        $PSScriptRoot `
        'ApiSpecRuntimeWitnesses.generated.cs'
}
$CatalogPath = [IO.Path]::GetFullPath($CatalogPath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not [IO.File]::Exists($CatalogPath)) {
    throw "API-spec catalog not found: $CatalogPath"
}

function ConvertTo-FactoryName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WitnessIdentifier
    )

    $segments = @(
        $WitnessIdentifier -split '[^A-Za-z0-9]+' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($segments.Count -eq 0) {
        throw "Witness identifier '$WitnessIdentifier' has no name segments."
    }
    $suffix = $segments | ForEach-Object {
        $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1)
    }
    return 'Create' + ($suffix -join '') + 'Witness'
}

$catalog = [IO.File]::ReadAllText($CatalogPath) |
    ConvertFrom-Json -Depth 100
if ($catalog.schema -ne 'SharpProof.ApiSpecCatalog' -or
    [int]$catalog.schemaVersion -ne 1) {
    throw 'The API-spec catalog schema must be SharpProof.ApiSpecCatalog v1.'
}
$declarations = @(
    $catalog.declarations |
        Sort-Object {
            [string]$_.target.witnessIdentifier
        })
if ($declarations.Count -eq 0) {
    throw 'The API-spec catalog must contain at least one declaration.'
}
$descriptors = @($declarations | ForEach-Object {
    $identifier = [string]$_.target.witnessIdentifier
    if ([string]::IsNullOrWhiteSpace($identifier)) {
        throw 'An API-spec declaration has no witness identifier.'
    }
    [pscustomobject]@{
        Identifier = $identifier
        Factory = ConvertTo-FactoryName $identifier
    }
})
if (@($descriptors.Identifier | Sort-Object -Unique).Count -ne
    $descriptors.Count) {
    throw 'API-spec witness identifiers must be unique.'
}
if (@($descriptors.Factory | Sort-Object -Unique).Count -ne
    $descriptors.Count) {
    throw 'API-spec witness identifiers produce colliding factory names.'
}

$source = New-SharpProofGeneratedHeader `
    -Generator 'SharpProof.Specs.Test/Generate-ApiSpecRuntimeWitnesses.ps1' `
    -Source 'SharpProof.Specs/DefaultApiSpecCatalog.json.'
$source.Add('using System.Collections.Immutable;')
$source.Add('')
$source.Add('namespace SharpProof.Specs.Test;')
$source.Add('')
$source.Add('public sealed partial class ApiSpecRuntimeOracleTests {')
$source.Add(
    '    private static ImmutableArray<RuntimeWitnessDescriptor> ' +
    'GeneratedRuntimeWitnesses =>')
$source.Add('    [')
foreach ($descriptor in $descriptors) {
    $source.Add(
        '        new(' +
        (ConvertTo-CSharpString $descriptor.Identifier) +
        ', ' +
        $descriptor.Factory +
        '),')
}
$source.Add('    ];')
$source.Add('}')
$sourceText = $source -join "`n"

[IO.Directory]::CreateDirectory(
    [IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
$generatorCommand =
    '.\SharpProof.Specs.Test\Generate-ApiSpecRuntimeWitnesses.ps1'
Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content $sourceText `
    -DisplayPath $OutputPath `
    -GeneratorCommand $generatorCommand `
    -Verify:$Verify

$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb catalog-derived API-spec runtime-witness descriptors."
