[CmdletBinding()]
param(
    [Parameter()][string]$CatalogPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path $repositoryRoot 'SharpProof.DeclarativeModels.catalog.json'
}
$CatalogPath = [IO.Path]::GetFullPath($CatalogPath)
$catalogJson = Get-Content -LiteralPath $CatalogPath -Raw

function Assert-UniqueJsonProperties {
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Value.EnumerateArray()) {
            Assert-UniqueJsonProperties $item "$Context[$index]"
            $index++
        }
        return
    }
    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        return
    }
    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $Value.EnumerateObject()) {
        if (-not $names.Add($property.Name)) {
            throw "$Context contains duplicate property '$($property.Name)'."
        }
        Assert-UniqueJsonProperties $property.Value "$Context.$($property.Name)"
    }
}

function Assert-AllowedProperties {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string[]]$Allowed,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $allowedSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($name in $Allowed) { [void]$allowedSet.Add($name) }
    foreach ($name in @($Object.PSObject.Properties.Name)) {
        if (-not $allowedSet.Contains([string]$name)) {
            throw "$Context contains unsupported property '$name'."
        }
    }
}

 $catalogDocument = [System.Text.Json.JsonDocument]::Parse($catalogJson)
try {
    Assert-UniqueJsonProperties $catalogDocument.RootElement 'declarative-model catalog'
}
finally {
    $catalogDocument.Dispose()
}
$catalog = $catalogJson | ConvertFrom-Json -Depth 100

Assert-AllowedProperties $catalog @('schema', 'schemaVersion', 'outputs') `
    'Declarative-model catalog'

function Required([object]$Object, [string]$Name, [string]$Context) {
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "$Context must define '$Name'."
    }
    return $property.Value
}

function Identifier([string]$Value, [string]$Context) {
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "$Context is not a C# identifier: '$Value'."
    }
    return $Value
}

function TypeName([string]$Value, [string]$Context) {
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_?.<>, \[\]]*$') {
        throw "$Context is not an approved C# type: '$Value'."
    }
    return $Value
}

function NamespaceName([string]$Value, [string]$Context) {
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$') {
        throw "$Context is not a C# namespace: '$Value'."
    }
    return $Value
}

if ([string](Required $catalog 'schema' 'Declarative-model catalog') -ne
    'SharpProof.DeclarativeModels' -or
    [int](Required $catalog 'schemaVersion' 'Declarative-model catalog') -ne 1) {
    throw 'Declarative-model catalog schema is unsupported.'
}

function Emit-Record([Collections.Generic.List[string]]$Lines,
    [object]$Record, [string]$Indent, [string]$Context) {
    Assert-AllowedProperties $Record @('kind', 'accessibility', 'modifiers', 'name', 'parameters') $Context
    $kind = [string](Required $Record 'kind' $Context)
    if ($kind -notin @('record', 'recordStruct')) {
        throw "$Context has unsupported record kind '$kind'."
    }
    $accessibility = Identifier ([string](Required $Record 'accessibility' $Context)) "$Context accessibility"
    $name = Identifier ([string](Required $Record 'name' $Context)) "$Context name"
    $modifiers = @($Record.modifiers | ForEach-Object {
        Identifier ([string]$_) "$Context modifier"
    })
    $parameters = @(Required $Record 'parameters' $Context)
    $modifierSource = if ($modifiers.Count -eq 0) { '' } else { ($modifiers -join ' ') + ' ' }
    $recordKeyword = if ($kind -eq 'recordStruct') { 'record struct' } else { 'record' }
    $Lines.Add("$Indent$accessibility $modifierSource`partial $recordKeyword $name(")
    for ($index = 0; $index -lt $parameters.Count; $index++) {
        $parameter = $parameters[$index]
        Assert-AllowedProperties $parameter @('type', 'name') "$Context parameter $index"
        $type = TypeName ([string](Required $parameter 'type' "$Context parameter $index") ) "$Context parameter $index type"
        $parameterName = Identifier ([string](Required $parameter 'name' "$Context parameter $index") ) "$Context parameter $index name"
        $comma = if ($index -lt $parameters.Count - 1) { ',' } else { '' }
        $Lines.Add("$Indent    $type $parameterName$comma")
    }
    $Lines.Add("$Indent);")
}

function Emit-Container([Collections.Generic.List[string]]$Lines,
    [object]$Container, [string]$Indent, [string]$Context) {
    Assert-AllowedProperties $Container @('types', 'records', 'children') $Context
    $types = @(Required $Container 'types' $Context)
    foreach ($type in $types) {
        Assert-AllowedProperties $type @('kind', 'accessibility', 'modifiers', 'name') "$Context container"
        $kind = [string](Required $type 'kind' "$Context container")
        if ($kind -ne 'class') { throw "$Context containers must be classes." }
        $accessibility = Identifier ([string](Required $type 'accessibility' "$Context container") ) "$Context container accessibility"
        $name = Identifier ([string](Required $type 'name' "$Context container") ) "$Context container name"
        $modifiers = @($type.modifiers | ForEach-Object {
            Identifier ([string]$_) "$Context container modifier"
        })
        $modifierSource = if ($modifiers.Count -eq 0) { '' } else { ($modifiers -join ' ') + ' ' }
        $Lines.Add("$Indent$accessibility $modifierSource`partial class $name")
        $Lines.Add("$Indent{")
        $Indent += '    '
    }
    foreach ($record in @(Required $Container 'records' $Context)) {
        Emit-Record $Lines $record $Indent "$Context record"
    }
    $childrenProperty = $Container.PSObject.Properties['children']
    $children = if ($null -eq $childrenProperty) { @() } else { @($childrenProperty.Value) }
    foreach ($child in $children) {
        Emit-Container $Lines $child $Indent "$Context child"
    }
    for ($index = $types.Count - 1; $index -ge 0; $index--) {
        $Indent = $Indent.Substring(0, $Indent.Length - 4)
        $Lines.Add("$Indent}")
    }
}

function Emit-Class([Collections.Generic.List[string]]$Lines,
    [object]$Class, [string]$Indent, [string]$Context) {
    Assert-AllowedProperties $Class @('kind', 'accessibility', 'name', 'modifiers', 'baseType', 'storageTag', 'constructor', 'properties') $Context
    if ([string](Required $Class 'kind' $Context) -ne 'class') {
        throw "$Context must have kind 'class'."
    }
    $accessibility = Identifier ([string](Required $Class 'accessibility' $Context)) "$Context accessibility"
    $name = Identifier ([string](Required $Class 'name' $Context)) "$Context name"
    $modifiers = @($Class.modifiers | ForEach-Object {
        Identifier ([string]$_) "$Context modifier"
    })
    $modifierSource = if ($modifiers.Count -eq 0) { '' } else { ($modifiers -join ' ') + ' ' }
    $baseTypeProperty = $Class.PSObject.Properties['baseType']
    $baseSource = if ($null -eq $baseTypeProperty) {
        ''
    }
    else {
        ' : ' + (TypeName ([string]$baseTypeProperty.Value) "$Context base type")
    }
    $constructor = Required $Class 'constructor' $Context
    Assert-AllowedProperties $constructor @('accessibility', 'parameters', 'assignments') "$Context constructor"
    $constructorAccess = Identifier ([string](Required $constructor 'accessibility' "$Context constructor")) "$Context constructor accessibility"
    $parameters = @(Required $constructor 'parameters' "$Context constructor")
    $assignments = @(Required $constructor 'assignments' "$Context constructor")
    $properties = @(Required $Class 'properties' $Context)
    $storageTagProperty = $Class.PSObject.Properties['storageTag']
    $storageTag = $null -ne $storageTagProperty -and [bool]$storageTagProperty.Value
    $propertyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $properties) {
        $propertyName = Identifier ([string](Required $property 'name' "$Context property")) "$Context property name"
        [void]$propertyNames.Add($propertyName)
        [void](TypeName ([string](Required $property 'type' "$Context property")) "$Context property type")
    }
    $Lines.Add("$Indent$accessibility $modifierSource`partial class $name$baseSource")
    $Lines.Add("$Indent{")
    if ($storageTag) {
        $Lines.Add("$Indent    internal readonly struct StorageTag")
        $Lines.Add("$Indent    {")
        $Lines.Add("$Indent    }")
    }
    $parameterSources = [Collections.Generic.List[string]]::new()
    foreach ($parameter in $parameters) {
        Assert-AllowedProperties $parameter @('type', 'name') "$Context parameter"
        $parameterName = Identifier ([string](Required $parameter 'name' "$Context parameter")) "$Context parameter name"
        $parameterType = TypeName ([string](Required $parameter 'type' "$Context parameter")) "$Context parameter type"
        $parameterSources.Add("$parameterType $parameterName")
    }
    if ($storageTag) {
        $parameterSources.Add('StorageTag storage')
    }
    $Lines.Add("$Indent    $constructorAccess $name(")
    for ($index = 0; $index -lt $parameterSources.Count; $index++) {
        $comma = if ($index -lt $parameterSources.Count - 1) { ',' } else { '' }
        $Lines.Add("$Indent        $($parameterSources[$index])$comma")
    }
    $Lines.Add("$Indent    )")
    $Lines.Add("$Indent    {")
    foreach ($assignment in $assignments) {
        Assert-AllowedProperties $assignment @('property', 'parameter') "$Context assignment"
        $propertyName = Identifier ([string](Required $assignment 'property' "$Context assignment")) "$Context assignment property"
        $parameterName = Identifier ([string](Required $assignment 'parameter' "$Context assignment")) "$Context assignment parameter"
        if (-not $propertyNames.Contains($propertyName)) {
            throw "$Context assignment references unknown property '$propertyName'."
        }
        if (@($parameters | Where-Object { [string]$_.name -eq $parameterName }).Count -ne 1) {
            throw "$Context assignment references unknown parameter '$parameterName'."
        }
        $Lines.Add("$Indent        $propertyName = $parameterName;")
    }
    $Lines.Add("$Indent    }")
    foreach ($property in $properties) {
        Assert-AllowedProperties $property @('accessibility', 'type', 'name') "$Context property"
        $propertyAccess = Identifier ([string](Required $property 'accessibility' "$Context property")) "$Context property accessibility"
        $propertyType = TypeName ([string](Required $property 'type' "$Context property")) "$Context property type"
        $propertyName = Identifier ([string](Required $property 'name' "$Context property")) "$Context property name"
        $Lines.Add('')
        $Lines.Add("$Indent    $propertyAccess $propertyType $propertyName")
        $Lines.Add("$Indent    {")
        $Lines.Add("$Indent        get;")
        $Lines.Add("$Indent    }")
    }
    $Lines.Add("$Indent}")
}

foreach ($output in @(Required $catalog 'outputs' 'Declarative-model catalog')) {
    Assert-AllowedProperties $output @('path', 'namespace', 'classes', 'records', 'containers') 'Declarative-model output'
    $relativePath = [string](Required $output 'path' 'Declarative-model output')
    if ($relativePath -notmatch '^[^:]+\.generated\.cs$') {
        throw "Declarative-model output path is not a generated C# file: '$relativePath'."
    }
    $path = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath))
    $namespace = NamespaceName ([string](Required $output 'namespace' "output '$relativePath'") ) "output '$relativePath' namespace"
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('// <auto-generated>')
    $lines.Add('// Generated by scripts/Generate-DeclarativeModels.ps1 from')
    $lines.Add('// SharpProof.DeclarativeModels.catalog.json.')
    $lines.Add('// Declarative record storage only; analysis remains handwritten.')
    $lines.Add('// Do not edit this file directly.')
    $lines.Add('// </auto-generated>')
    $lines.Add('#nullable enable')
    $lines.Add("namespace $namespace;")
    $lines.Add('')
    $classesProperty = $output.PSObject.Properties['classes']
    $classes = if ($null -eq $classesProperty) { @() } else { @($classesProperty.Value) }
    foreach ($class in $classes) {
        Emit-Class $lines $class '' "output '$relativePath' class"
        $lines.Add('')
    }
    foreach ($record in @(Required $output 'records' "output '$relativePath'")) {
        Emit-Record $lines $record '' "output '$relativePath' record"
        $lines.Add('')
    }
    foreach ($container in @(Required $output 'containers' "output '$relativePath'")) {
        Emit-Container $lines $container '' "output '$relativePath' container"
        $lines.Add('')
    }
    Update-SharpProofGeneratedFile -Path $path -Content ($lines -join "`n") `
        -DisplayPath $relativePath -GeneratorCommand '.\scripts\Generate-DeclarativeModels.ps1' `
        -Verify:$Verify
}

$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic declarative models."
