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
    Join-Path $repositoryRoot 'SharpProof.Effects\EffectContractMappings.catalog.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Effects\EffectContractMappings.generated.cs')
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
if ([int]$catalog.schemaVersion -ne 1) {
    throw 'Only effect-contract-mappings catalog schema version 1 is supported.'
}

$lines = New-SharpProofGeneratedHeader `
    -Generator 'Generate-EffectContractMappings.ps1' `
    -Source 'SharpProof.Effects/EffectContractMappings.catalog.json.' `
    -Nullable
$lines.Add('')
$lines.Add('namespace SharpProof.Effects;')
$lines.Add('')
foreach ($enum in @($catalog.enums)) {
    $enumName = [string]$enum.name
    Assert-Identifier $enumName 'Effect enum name'
    $access = [string]$enum.access
    if ($access -notin @('public', 'internal')) {
        throw "Effect enum '$enumName' has unsupported access '$access'."
    }
    $underlyingProperty = $enum.PSObject.Properties['underlyingType']
    $underlying = if ($null -eq $underlyingProperty) {
        ''
    }
    else {
        $type = [string]$underlyingProperty.Value
        if ($type -notin @('int', 'long')) {
            throw "Effect enum '$enumName' has unsupported underlying type '$type'."
        }
        " : $type"
    }
    if ([bool]$enum.flags) {
        $lines.Add('[Flags]')
    }
    $lines.Add("$access enum $enumName$underlying")
    $lines.Add('{')
    $members = @($enum.members)
    if ($members.Count -eq 0) {
        throw "Effect enum '$enumName' must define members."
    }
    for ($index = 0; $index -lt $members.Count; $index++) {
        $member = $members[$index]
        $memberName = [string]$member.name
        Assert-Identifier $memberName "Effect enum '$enumName' member"
        $value = [long]$member.value
        $suffix = if ($index -lt $members.Count - 1) { ',' } else { '' }
        $lines.Add(
            "    $memberName = $($value.ToString([Globalization.CultureInfo]::InvariantCulture))$suffix")
    }
    $lines.Add('}')
    $lines.Add('')
}
foreach ($record in @($catalog.records)) {
    $recordName = [string]$record.name
    Assert-Identifier $recordName 'Effect record name'
    $access = [string]$record.access
    if ($access -notin @('public', 'internal')) {
        throw "Effect record '$recordName' has unsupported access '$access'."
    }
    $lines.Add("$access readonly record struct $recordName(")
    $parameters = @($record.parameters)
    if ($parameters.Count -eq 0) {
        throw "Effect record '$recordName' must define parameters."
    }
    for ($index = 0; $index -lt $parameters.Count; $index++) {
        $parameter = $parameters[$index]
        $type = [string]$parameter.type
        $name = [string]$parameter.name
        Assert-TypeName $type "Effect record '$recordName' parameter type"
        Assert-Identifier $name "Effect record '$recordName' parameter name"
        $defaultProperty = $parameter.PSObject.Properties['default']
        $default = if ($null -eq $defaultProperty) {
            ''
        }
        else {
            $value = [string]$defaultProperty.Value
            if ($value -notmatch '^(null|""|[A-Za-z_][A-Za-z0-9_.]*)$') {
                throw "Effect record '$recordName' has an invalid default."
            }
            " = $value"
        }
        $comma = if ($index -lt $parameters.Count - 1) { ',' } else { '' }
        $lines.Add("    $type $name$default$comma")
    }
    $lines.Add(');')
    $lines.Add('')
}
$lines.Add('internal static class EffectContractMappingCatalog')
$lines.Add('{')
$lines.Add('    internal static readonly (EffectContractCapabilityKind Contract, EffectCapabilityKind Analysis, EffectContractKind Effect)[] Capabilities = [')
foreach ($entry in @($catalog.capabilities)) {
    foreach ($name in @('contract', 'analysis', 'effect')) {
        Assert-Identifier ([string]$entry.PSObject.Properties[$name].Value) "Capability $name"
    }
    $lines.Add(
        "        (EffectContractCapabilityKind.$($entry.contract), " +
        "EffectCapabilityKind.$($entry.analysis), EffectContractKind.$($entry.effect)),")
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static readonly (EffectRegionKind Region, EffectContractKind Read, EffectContractKind Write, EffectRegionId? AnalysisRegion, bool ExpandParameters)[] RegionContracts = [')
foreach ($entry in @($catalog.regions)) {
    foreach ($name in @('region', 'read', 'write')) {
        Assert-Identifier ([string]$entry.PSObject.Properties[$name].Value) "Region $name"
    }
    $analysis = if ($null -eq $entry.analysisRegion) {
        'null'
    }
    else {
        Assert-Identifier ([string]$entry.analysisRegion) 'Region analysisRegion'
        if ([string]$entry.analysisRegion -in @('Receiver', 'Ambient', 'Unknown')) {
            "EffectRegionId.$($entry.analysisRegion)"
        }
        else {
            "EffectRegionId.$($entry.analysisRegion)(0)"
        }
    }
    $expandParameters = if ([bool]$entry.expandParameters) { 'true' } else { 'false' }
    $lines.Add(
        "        (EffectRegionKind.$($entry.region), EffectContractKind.$($entry.read), " +
        "EffectContractKind.$($entry.write), $analysis, " +
        "$expandParameters),")
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static readonly (EffectDirectEventKind Event, string WireName)[] DirectEvents = [')
foreach ($entry in @($catalog.directEvents)) {
    Assert-Identifier ([string]$entry.event) 'Direct event'
    $lines.Add(
        "        (EffectDirectEventKind.$($entry.event), " +
        (ConvertTo-CSharpString ([string]$entry.wireName)) + '),')
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('    internal static readonly (string Marker, ApiSpecReferenceFamily Family)[] ReferenceFamilyMarkers = [')
foreach ($entry in @($catalog.referenceFamilyMarkers)) {
    Assert-Identifier ([string]$entry.family) 'Reference family'
    $lines.Add(
        '        (' + (ConvertTo-CSharpString ([string]$entry.marker)) + ', ' +
        "ApiSpecReferenceFamily.$($entry.family)),")
}
$lines.Add('    ];')
$lines.Add('')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal readonly struct EffectEvidenceRule(')
$lines.Add('    Type type, long mask, bool flags, long[] values)')
$lines.Add('{')
$lines.Add('    internal Type Type { get; } = type;')
$lines.Add('    internal long Mask { get; } = mask;')
$lines.Add('    internal bool Flags { get; } = flags;')
$lines.Add('    internal long[] Values { get; } = values;')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal static class EffectEvidenceCatalog')
$lines.Add('{')
$lines.Add('    internal static readonly EffectEvidenceRule[] Rules = [')
foreach ($entry in @($catalog.evidenceRules)) {
    $type = [string]$entry.type
    Assert-Identifier $type 'Evidence rule type'
    $mode = [string]$entry.mode
    if ($mode -notin @('flags', 'exact')) {
        throw "Evidence rule '$type' has unsupported mode '$mode'."
    }
    $mask = if ($mode -eq 'flags') {
        $member = [string]$entry.mask
        if ($member -notmatch '^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Evidence rule '$type' has an invalid mask."
        }
        "(long)$member"
    }
    else {
        '0'
    }
    $values = if ($mode -eq 'exact') {
        $items = foreach ($value in @($entry.values)) {
            Assert-Identifier ([string]$value) "Evidence rule '$type' value"
            "(long)$type.$value"
        }
        '[' + ($items -join ', ') + ']'
    }
    else {
        '[]'
    }
    $lines.Add(
        "        new(typeof($type), $mask, " +
        $(if ($mode -eq 'flags') { 'true' } else { 'false' }) +
        ", $values),")
}
$lines.Add('    ];')
$lines.Add('}')

Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content ($lines -join "`n") `
    -DisplayPath $OutputPath `
    -GeneratorCommand '.\scripts\Generate-EffectContractMappings.ps1' `
    -Verify:$Verify
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic effect-contract mappings."
