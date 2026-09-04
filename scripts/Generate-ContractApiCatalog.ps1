[CmdletBinding()]
param(
    [Parameter()]
    [string]$CatalogPath,

    [Parameter()]
    [string]$OutputPath,

    [Parameter()]
    [Alias('Check')]
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$CatalogPath = Resolve-SharpProofPath $CatalogPath (
    Join-Path $repositoryRoot 'SharpProof.Frontend\ContractApi.catalog.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Frontend\ContractApiMetadata.generated.cs')
if (-not [IO.File]::Exists($CatalogPath)) {
    throw "Contract API catalog not found: $CatalogPath"
}

$catalogJson = Get-Content -LiteralPath $CatalogPath -Raw
$catalogDocument = [System.Text.Json.JsonDocument]::Parse($catalogJson)
try {
    Assert-UniqueJsonProperties `
        -Value $catalogDocument.RootElement `
        -Context 'contract API catalog'
}
finally {
    $catalogDocument.Dispose()
}
$catalog = $catalogJson | ConvertFrom-Json
Assert-Properties `
    -Value $catalog `
    -Allowed @(
        'schemaVersion',
        'namespace',
        'contractType',
        'conditionalSymbol',
        'methods',
        'attributes') `
    -Context 'contract API catalog'
if ($catalog.schemaVersion -ne 1) {
    throw 'Contract API catalog schemaVersion must be 1.'
}
if ($catalog.namespace -isnot [string] -or
    [string]$catalog.namespace -cnotmatch
        '\A[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)*\z') {
    throw 'Contract API namespace is invalid.'
}
$contractType = Assert-PascalCaseIdentifier `
    -Value $catalog.contractType `
    -Context 'contractType'
if ($catalog.conditionalSymbol -isnot [string] -or
    [string]$catalog.conditionalSymbol -cnotmatch '\A[A-Z][A-Z0-9_]*\z') {
    throw 'Contract API conditionalSymbol is invalid.'
}

$methodShapes = @('Clause', 'Old', 'Result')
$clauseRoles = @('None', 'Requires', 'Ensures', 'Assume')
$methodIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$methodNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$methods = [Collections.Generic.List[object]]::new()
foreach ($method in @($catalog.methods)) {
    Assert-Properties `
        -Value $method `
        -Allowed @('id', 'name', 'shape', 'clauseRole') `
        -Context 'contract API method'
    $id = Assert-PascalCaseIdentifier -Value $method.id -Context 'method id'
    $name = Assert-PascalCaseIdentifier -Value $method.name -Context "method '$id' name"
    $shape = Assert-EnumValue `
        -Value $method.shape `
        -Allowed $methodShapes `
        -Context "method '$id' shape"
    $role = Assert-EnumValue `
        -Value $method.clauseRole `
        -Allowed $clauseRoles `
        -Context "method '$id' clauseRole"
    if (-not $methodIds.Add($id) -or -not $methodNames.Add($name)) {
        throw "Contract API method '$id' or '$name' is duplicated."
    }
    if (($shape -eq 'Clause') -ne ($role -ne 'None')) {
        throw "Method '$id' must pair Clause with a non-None clauseRole."
    }
    if ($shape -eq 'Clause' -and $id -ne $role) {
        throw "Clause method '$id' must use the matching clauseRole."
    }
    $methods.Add([pscustomobject]@{
        Id = $id
        Name = $name
        Shape = $shape
        ClauseRole = $role
    })
}
if ($methods.Count -ne 5 -or
    @($methods | Where-Object Shape -eq 'Clause').Count -ne 3 -or
    @($methods | Where-Object Shape -eq 'Old').Count -ne 1 -or
    @($methods | Where-Object Shape -eq 'Result').Count -ne 1) {
    throw 'Contract API catalog must define three clauses, Old, and Result.'
}

$attributeCategories = @('Companion', 'Closed', 'Effect', 'Control')
$selectionValues = @('None', 'Contracts', 'Effects', 'All')
$attributeIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$attributeTypeNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$attributes = [Collections.Generic.List[object]]::new()
foreach ($attribute in @($catalog.attributes)) {
    Assert-Properties `
        -Value $attribute `
        -Allowed @('id', 'typeName', 'category', 'selection') `
        -Context 'contract API attribute'
    $id = Assert-PascalCaseIdentifier -Value $attribute.id -Context 'attribute id'
    $typeName = Assert-PascalCaseIdentifier `
        -Value $attribute.typeName `
        -Context "attribute '$id' typeName"
    $category = Assert-EnumValue `
        -Value $attribute.category `
        -Allowed $attributeCategories `
        -Context "attribute '$id' category"
    $selection = Assert-EnumValue `
        -Value $attribute.selection `
        -Allowed $selectionValues `
        -Context "attribute '$id' selection"
    if (-not $typeName.EndsWith('Attribute', [StringComparison]::Ordinal)) {
        throw "Contract API attribute '$id' typeName must end in Attribute."
    }
    if (-not $attributeIds.Add($id) -or
        -not $attributeTypeNames.Add($typeName)) {
        throw "Contract API attribute '$id' or '$typeName' is duplicated."
    }
    $expectedSelection = switch ($category) {
        'Companion' { 'None' }
        'Closed' { 'Contracts' }
        'Effect' { 'Effects' }
        'Control' { 'All' }
    }
    if ($selection -ne $expectedSelection) {
        throw "Attribute '$id' category '$category' requires '$expectedSelection' selection."
    }
    $attributes.Add([pscustomobject]@{
        Id = $id
        TypeName = $typeName
        Category = $category
        Selection = $selection
    })
}
if (@($attributes | Where-Object Id -eq 'ContractFor').Count -ne 1 -or
    @($attributes | Where-Object Category -eq 'Closed').Count -eq 0 -or
    @($attributes | Where-Object Category -eq 'Effect').Count -eq 0 -or
    @($attributes | Where-Object Category -eq 'Control').Count -eq 0) {
    throw 'Contract API catalog is missing a required attribute category.'
}

$lines = New-SharpProofGeneratedHeader `
    -Generator 'scripts/Generate-ContractApiCatalog.ps1' `
    -Source 'SharpProof.Frontend/ContractApi.catalog.json.'
$lines.Add('')
$lines.Add('namespace SharpProof.Frontend;')
$lines.Add('')
$lines.Add('internal enum ContractApiMethodShape')
$lines.Add('{')
foreach ($shape in $methodShapes) {
    $lines.Add("    $shape,")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('internal enum ContractApiClauseRole')
$lines.Add('{')
foreach ($role in $clauseRoles) {
    $lines.Add("    $role,")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('internal enum ContractApiAttributeCategory')
$lines.Add('{')
foreach ($category in $attributeCategories) {
    $lines.Add("    $category,")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('[Flags]')
$lines.Add('internal enum ContractApiSelectionFeature')
$lines.Add('{')
$lines.Add('    None = 0,')
$lines.Add('    Contracts = 1,')
$lines.Add('    Effects = 2,')
$lines.Add('    All = Contracts | Effects,')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal readonly struct ContractApiMethodDescriptor(')
$lines.Add('    string name,')
$lines.Add('    ContractApiMethodShape shape,')
$lines.Add('    ContractApiClauseRole clauseRole)')
$lines.Add('{')
$lines.Add('    internal string Name { get; } = name;')
$lines.Add('    internal ContractApiMethodShape Shape { get; } = shape;')
$lines.Add('    internal ContractApiClauseRole ClauseRole { get; } = clauseRole;')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal readonly struct ContractApiAttributeDescriptor(')
$lines.Add('    string metadataName,')
$lines.Add('    string typeName,')
$lines.Add('    ContractApiAttributeCategory category,')
$lines.Add('    ContractApiSelectionFeature selection)')
$lines.Add('{')
$lines.Add('    internal string MetadataName { get; } = metadataName;')
$lines.Add('    internal string TypeName { get; } = typeName;')
$lines.Add('    internal ContractApiAttributeCategory Category { get; } = category;')
$lines.Add('    internal ContractApiSelectionFeature Selection { get; } = selection;')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal static class ContractApiCatalog')
$lines.Add('{')
$lines.Add(
    '    internal const string AttributesNamespace = ' +
    (ConvertTo-CSharpString ([string]$catalog.namespace)) + ';')
$lines.Add(
    '    internal const string ConditionalSymbol = ' +
    (ConvertTo-CSharpString ([string]$catalog.conditionalSymbol)) + ';')
$lines.Add(
    '    internal const string Contract = AttributesNamespace + ".' +
    $contractType + '";')
foreach ($method in $methods) {
    $lines.Add(
        '    internal const string ' + $method.Id + 'MethodName = ' +
        (ConvertTo-CSharpString $method.Name) + ';')
}
foreach ($attribute in $attributes) {
    $lines.Add(
        '    internal const string ' + $attribute.Id + ' = ' +
        'AttributesNamespace + ".' + $attribute.TypeName + '";')
}
$lines.Add('')
$lines.Add('    internal static ImmutableArray<ContractApiMethodDescriptor> Methods { get; } =')
$lines.Add('        [')
foreach ($method in $methods) {
    $lines.Add('            new(')
    $lines.Add('                ' + $method.Id + 'MethodName,')
    $lines.Add('                ContractApiMethodShape.' + $method.Shape + ',')
    $lines.Add('                ContractApiClauseRole.' + $method.ClauseRole + '),')
}
$lines.Add('        ];')
$lines.Add('')
$lines.Add('    internal static ImmutableArray<ContractApiAttributeDescriptor> Attributes { get; } =')
$lines.Add('        [')
foreach ($attribute in $attributes) {
    $lines.Add('            new(')
    $lines.Add('                ' + $attribute.Id + ',')
    $lines.Add('                ' + (ConvertTo-CSharpString $attribute.TypeName) + ',')
    $lines.Add('                ContractApiAttributeCategory.' + $attribute.Category + ',')
    $lines.Add('                ContractApiSelectionFeature.' + $attribute.Selection + '),')
}
$lines.Add('        ];')
$lines.Add('')
$lines.Add('    internal static ImmutableArray<string> ContractMethodCandidateNames { get; } =')
$lines.Add('        Methods.Select(static method => method.Name).ToImmutableArray();')
$lines.Add('')
$lines.Add('    internal static ImmutableArray<string> AttributeMetadataNames { get; } =')
$lines.Add('        Attributes.Select(static attribute => attribute.MetadataName).ToImmutableArray();')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal static class ContractApiClauseProjection')
$lines.Add('{')
$lines.Add('    internal static ContractApiClauseRole GetClauseRole(string name)')
$lines.Add('    {')
$lines.Add('        return name switch')
$lines.Add('        {')
foreach ($method in @($methods | Where-Object Shape -eq 'Clause')) {
    $lines.Add(
        '            ContractApiCatalog.' + $method.Id +
        'MethodName => ContractApiClauseRole.' + $method.ClauseRole + ',')
}
$lines.Add('            _ => ContractApiClauseRole.None')
$lines.Add('        };')
$lines.Add('    }')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal static partial class ContractApiMetadata')
$lines.Add('{')
$lines.Add('    internal const string AttributesNamespace =')
$lines.Add('        ContractApiCatalog.AttributesNamespace;')
$lines.Add('    internal const string Attribute = "System.Attribute";')
$lines.Add('    internal const string ConditionalAttribute =')
$lines.Add('        "System.Diagnostics.ConditionalAttribute";')
$lines.Add('    internal const string ConditionalSymbol =')
$lines.Add('        ContractApiCatalog.ConditionalSymbol;')
$lines.Add('    internal const string AttributesPayloadSha256MetadataKey =')
$lines.Add('        AttributesNamespace + ".SHA256";')
$lines.Add('    internal const string Contract = ContractApiCatalog.Contract;')
foreach ($method in $methods) {
    $lines.Add(
        '    internal const string ' + $method.Id + 'MethodName =')
    $lines.Add(
        '        ContractApiCatalog.' + $method.Id + 'MethodName;')
}
foreach ($attribute in $attributes) {
    $lines.Add('    internal const string ' + $attribute.Id + ' =')
    $lines.Add('        ContractApiCatalog.' + $attribute.Id + ';')
}
$lines.Add('')
$lines.Add('    internal static ImmutableArray<ContractApiMethodDescriptor> Methods { get; } = ContractApiCatalog.Methods;')
$lines.Add('    internal static ImmutableArray<ContractApiAttributeDescriptor> Attributes { get; } = ContractApiCatalog.Attributes;')
$lines.Add('    internal static ImmutableArray<string> ContractMethodCandidateNames { get; } =')
$lines.Add('        ContractApiCatalog.ContractMethodCandidateNames;')
$lines.Add('    internal static ImmutableArray<string> AttributeMetadataNames { get; } =')
$lines.Add('        ContractApiCatalog.AttributeMetadataNames;')
$lines.Add('}')

$content = $lines -join "`n"
Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content $content `
    -DisplayPath 'SharpProof.Frontend/ContractApiMetadata.generated.cs' `
    -GeneratorCommand '.\scripts\Generate-ContractApiCatalog.ps1' `
    -Verify:$Verify
