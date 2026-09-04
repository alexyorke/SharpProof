[CmdletBinding()]
param(
    [Parameter()][string]$SchemaPath,
    [Parameter()][string]$ProtocolSchemaPath,
    [Parameter()][Alias('OutputPath')][string]$ModelOutputPath,
    [Parameter()][string]$PortableOutputPath,
    [Parameter()][string]$CompilationOutputPath,
    [Parameter()][string]$CollectorOutputPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$SchemaPath = Resolve-SharpProofPath $SchemaPath (
    Join-Path $repositoryRoot 'SharpProof.CompilerArtifact\CompilerArtifactModel.schema.json')
$ProtocolSchemaPath = Resolve-SharpProofPath $ProtocolSchemaPath (
    Join-Path $repositoryRoot 'SharpProof.Worker.Protocol\ProtocolModel.schema.json')
$ModelOutputPath = Resolve-SharpProofPath $ModelOutputPath (
    Join-Path $repositoryRoot 'SharpProof.CompilerArtifact\CompilerArtifactModel.generated.cs')
$PortableOutputPath = Resolve-SharpProofPath $PortableOutputPath (
    Join-Path $repositoryRoot 'SharpProof.CompilerArtifact\PortableIrModel.generated.cs')
$CompilationOutputPath = Resolve-SharpProofPath $CompilationOutputPath (
    Join-Path $repositoryRoot 'SharpProof.CompilerArtifact\CompilerCompilationModel.generated.cs')
$CollectorOutputPath = Resolve-SharpProofPath $CollectorOutputPath (
    Join-Path $repositoryRoot 'SharpProof.CompilerCollector\CompilerArtifact\CompilerWireMappings.generated.cs')
if (-not [IO.File]::Exists($SchemaPath)) {
    throw "Compiler-artifact schema not found: $SchemaPath"
}
if (-not [IO.File]::Exists($ProtocolSchemaPath)) {
    throw "Protocol schema not found: $ProtocolSchemaPath"
}

function Get-MetadataRowExpression {
    param([string]$Role, [string]$Member)

    switch ($Role) {
        'direct' { return "value.$Member" }
        'stringValue' { return "_factory.GetString(value.$Member)" }
        'optionalStringValue' {
            return (
                "value.$Member.HasValue ? " +
                "_factory.GetString(value.$Member.Value) : null")
        }
        'typeIndex' { return "TypeIndex(value.$Member)" }
        'optionalTypeIndex' {
            return (
                "value.$Member.HasValue ? " +
                "TypeIndex(value.$Member.Value) : -1")
        }
        'typeIndices' { return "[.. value.$Member.Select(TypeIndex)]" }
        'identityIndex' { return "_identities.Add(value.$Member)" }
        default { throw "Unsupported metadata-row projection role '$Role'." }
    }
}

function New-GeneratedOutput {
    param(
        [string]$OutputName,
        [string[]]$Imports = @()
    )

    $result = New-SharpProofGeneratedHeader `
        -Generator 'scripts/Generate-CompilerArtifactModel.ps1' `
        -Source 'SharpProof.CompilerArtifact/CompilerArtifactModel.schema.json.' `
        -Notes @("Output: $OutputName.") `
        -Nullable
    $result.Add('')
    foreach ($import in $Imports) {
        $result.Add("using $import;")
    }
    if ($Imports.Count -gt 0) {
        $result.Add('')
    }
    $result.Add('namespace SharpProof.CompilerArtifact;')
    return ,$result
}

function Get-ParameterSource {
    param([object[]]$Parameters, [string]$Context)

    $sources = [Collections.Generic.List[string]]::new($Parameters.Count)
    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($parameter in $Parameters) {
        $name = [string](Get-RequiredMember $parameter 'name' $Context)
        $type = [string](Get-RequiredMember $parameter 'type' $Context)
        Assert-Identifier $name "$Context parameter"
        Assert-TypeName $type "$Context parameter '$name'"
        if (-not $names.Add($name)) {
            throw "$Context repeats parameter '$name'."
        }
        $defaultMember = $parameter.PSObject.Properties['default']
        $default = if ($null -eq $defaultMember) {
            ''
        }
        else {
            $value = [string]$defaultMember.Value
            if ($value -notmatch '^(default|null|false|true|-?[0-9]+)$') {
                throw "$Context parameter '$name' has an invalid default."
            }
            " = $value"
        }
        $sources.Add("$type $name$default")
    }
    return $sources
}

function Add-ParameterList {
    param(
        [Collections.Generic.List[string]]$Lines,
        [string]$Prefix,
        [object[]]$Parameters,
        [string]$Suffix,
        [string]$Context
    )

    $sources = @(Get-ParameterSource $Parameters $Context)
    if ($sources.Count -eq 0) {
        $Lines.Add("$Prefix$Suffix")
        return
    }
    $Lines.Add("$Prefix(")
    for ($index = 0; $index -lt $sources.Count; $index++) {
        $comma = if ($index -lt $sources.Count - 1) { ',' } else { '' }
        $Lines.Add("    $($sources[$index])$comma")
    }
    $Lines.Add(")$Suffix")
}

function Get-InitializerSource {
    param(
        [object]$Default,
        [Collections.Generic.HashSet[string]]$ConstructorParameters,
        [string]$Context
    )

    $kind = [string](Get-RequiredMember $Default 'kind' $Context)
    switch ($kind) {
        'implicit' { return '' }
        'stringEmpty' { return ' = string.Empty' }
        'new' { return ' = new()' }
        'emptyArray' { return ' = []' }
        'literal' {
            $value = [string](Get-RequiredMember $Default 'value' $Context)
            if ($value -notmatch '^-?[0-9]+$') {
                throw "$Context has an invalid literal initializer."
            }
            return " = $value"
        }
        'member' {
            $value = [string](Get-RequiredMember $Default 'value' $Context)
            if ($value -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') {
                throw "$Context has an invalid member initializer."
            }
            return " = $value"
        }
        { $_ -in 'parameter', 'parameterOrStringEmpty', 'parameterOrEmptyArray' } {
            $name = [string](Get-RequiredMember $Default 'name' $Context)
            if (-not $ConstructorParameters.Contains($name)) {
                throw "$Context references unknown constructor parameter '$name'."
            }
            $expression = switch ($kind) {
                'parameter' { $name }
                'parameterOrStringEmpty' { "$name ?? string.Empty" }
                'parameterOrEmptyArray' { "$name ?? []" }
            }
            return " = $expression"
        }
        default { throw "$Context has unsupported default kind '$kind'." }
    }
}

function Add-Properties {
    param(
        [Collections.Generic.List[string]]$Lines,
        [object[]]$Properties,
        [Collections.Generic.HashSet[string]]$ConstructorParameters,
        [string]$TypeName,
        [string]$JsonNamingPolicy
    )

    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $jsonNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $Properties) {
        $name = [string](
            Get-RequiredMember $property 'name' "type '$TypeName' property")
        $type = [string](
            Get-RequiredMember $property 'type' "property '$TypeName.$name'")
        $accessibility = [string](
            Get-RequiredMember $property 'accessibility' "property '$TypeName.$name'")
        $setter = [string](
            Get-RequiredMember $property 'set' "property '$TypeName.$name'")
        Assert-Identifier $name "Type '$TypeName' property"
        Assert-TypeName $type "Property '$TypeName.$name'"
        if (-not $names.Add($name)) {
            throw "Type '$TypeName' repeats property '$name'."
        }
        if ($accessibility -notin 'public', 'internal') {
            throw "Property '$TypeName.$name' has invalid accessibility."
        }
        if ($setter -notin 'set', 'init', 'none') {
            throw "Property '$TypeName.$name' has invalid setter."
        }
        $jsonMember = $property.PSObject.Properties['jsonName']
        if ($accessibility -eq 'public') {
            if ($null -eq $jsonMember -or [string]::IsNullOrWhiteSpace(
                    [string]$jsonMember.Value)) {
                throw "Public property '$TypeName.$name' must define its JSON name."
            }
            $jsonName = [string]$jsonMember.Value
            $expected = $name.Substring(0, 1).ToLowerInvariant() +
                $name.Substring(1)
            if ($JsonNamingPolicy -ne 'camelCase' -or
                $jsonName -ne $expected -or
                -not $jsonNames.Add($jsonName)) {
                throw "Property '$TypeName.$name' has an invalid JSON name."
            }
        }
        elseif ($null -ne $jsonMember) {
            throw "Internal property '$TypeName.$name' cannot define a JSON name."
        }
        $default = Get-RequiredMember $property 'default' "property '$TypeName.$name'"
        $initializer = Get-InitializerSource `
            $default $ConstructorParameters "property '$TypeName.$name'"
        $accessors = switch ($setter) {
            'set' { '{ get; set; }' }
            'init' { '{ get; init; }' }
            'none' { '{ get; }' }
        }
        $terminator = if ($initializer.Length -eq 0) { '' } else { ';' }
        $Lines.Add(
            "    $accessibility $type $name $accessors$initializer$terminator")
    }
}

function Add-RecordMembers {
    param(
        [Collections.Generic.List[string]]$Lines,
        [object[]]$Members,
        [string]$TypeName
    )

    foreach ($member in $Members) {
        $kind = [string](
            Get-RequiredMember $member 'kind' "record '$TypeName' member")
        $name = [string](
            Get-RequiredMember $member 'name' "record '$TypeName' member")
        $type = [string](
            Get-RequiredMember $member 'type' "record '$TypeName.$name'")
        $accessibility = [string](
            Get-RequiredMember $member 'accessibility' "record '$TypeName.$name'")
        Assert-Identifier $name "Record '$TypeName' member"
        Assert-TypeName $type "Record '$TypeName.$name'"
        if ($accessibility -notin 'public', 'internal') {
            throw "Record '$TypeName.$name' has invalid accessibility."
        }
        if ($kind -eq 'computedProperty') {
            $operation = [string](
                Get-RequiredMember $member 'operation' "record '$TypeName.$name'")
            $source = [string](
                Get-RequiredMember $member 'source' "record '$TypeName.$name'")
            $value = [string](
                Get-RequiredMember $member 'value' "record '$TypeName.$name'")
            if ($operation -ne 'equals' -or
                $source -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or
                $value -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') {
                throw "Record '$TypeName.$name' has invalid computed semantics."
            }
            $Lines.Add("    $accessibility $type $name => $source == $value;")
        }
        elseif ($kind -eq 'property') {
            $setter = [string](
                Get-RequiredMember $member 'set' "record '$TypeName.$name'")
            if ($setter -notin 'set', 'init') {
                throw "Record '$TypeName.$name' has invalid setter."
            }
            $default = Get-RequiredMember `
                $member 'default' "record '$TypeName.$name'"
            $empty = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            $initializer = Get-InitializerSource `
                $default $empty "record '$TypeName.$name'"
            $Lines.Add(
                "    $accessibility $type $name { get; $setter; }$initializer;")
        }
        else {
            throw "Record '$TypeName.$name' has unsupported kind '$kind'."
        }
    }
}

$schema = Read-SharpProofSchema `
    -Path $SchemaPath `
    -Context 'compiler-artifact model' `
    -ExpectedNamespace 'SharpProof.CompilerArtifact'
$namespace = [string]$schema.namespace
$jsonNamingPolicy = [string]$schema.jsonNamingPolicy
$declarations = @(Get-RequiredMember $schema 'declarations' 'schema')
$declarationNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$modelLines = New-GeneratedOutput `
    'CompilerArtifactModel.generated.cs' `
    @('System.Collections.Immutable', 'SharpProof.Ir', 'SharpProof.Worker.Protocol')
$portableLines = New-GeneratedOutput `
    'PortableIrModel.generated.cs' `
    @('SharpProof.Ir')
$compilationLines = New-GeneratedOutput 'CompilerCompilationModel.generated.cs'
$collectorLines = New-GeneratedOutput 'CompilerWireMappings.generated.cs'

foreach ($declaration in $declarations) {
    $kind = [string](Get-RequiredMember $declaration 'kind' 'declaration')
    $name = [string](Get-RequiredMember $declaration 'name' 'declaration')
    Assert-Identifier $name 'Declaration name'
    if (-not $declarationNames.Add($name)) {
        throw "Duplicate compiler-artifact declaration '$name'."
    }
    $lines = if ($name -match '^PortableIr' -or
        $name -in 'DecodedPortableIrGraph', 'EncodedPortableIrGraph') {
        ,$portableLines
    }
    elseif ($name -in @(
            'CompilerCompilationSnapshot',
            'CompilerCompilationOptionsSnapshot',
            'CompilerReportDiagnostic',
            'CompilerDiagnosticOptionSnapshot',
            'CompilerSyntaxTreeSnapshot',
            'CompilerReferenceSnapshot',
            'CompilerReferenceLimits',
            'CompilerReferenceModuleSnapshot',
            'CompilerAdditionalFileSnapshot',
            'CompilerFeatureSnapshot',
            'CompilerSourceLineMapEntry')) {
        ,$compilationLines
    }
    else {
        ,$modelLines
    }
    $lines.Add('')
    if ($kind -eq 'staticClass') {
        $lines.Add("internal static class $name {")
        foreach ($constant in @(Get-RequiredMember `
                $declaration 'constants' "static class '$name'")) {
            $constantName = [string](
                Get-RequiredMember $constant 'name' "static class '$name'")
            $type = [string](
                Get-RequiredMember $constant 'type' "constant '$name.$constantName'")
            $accessibility = [string](
                Get-RequiredMember $constant 'accessibility' "constant '$name.$constantName'")
            Assert-Identifier $constantName "Constant '$name'"
            if ($accessibility -ne 'internal' -or $type -notin 'string', 'int') {
                throw "Constant '$name.$constantName' has unsupported shape."
            }
            $value = Get-RequiredMember `
                $constant 'value' "constant '$name.$constantName'"
            $source = if ($type -eq 'string') {
                ConvertTo-CSharpString ([string]$value)
            }
            else {
                ([Convert]::ToInt32(
                    $value,
                    [Globalization.CultureInfo]::InvariantCulture)
                ).ToString([Globalization.CultureInfo]::InvariantCulture)
            }
            $lines.Add("    internal const $type $constantName = $source;")
        }
        $lines.Add('}')
        continue
    }
    if ($kind -eq 'enum') {
        $members = @(Get-RequiredMember $declaration 'members' "enum '$name'")
        $memberNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $lines.Add("internal enum $name {")
        for ($index = 0; $index -lt $members.Count; $index++) {
            $member = $members[$index]
            $memberName = [string](
                Get-RequiredMember $member 'name' "enum '$name' member")
            Assert-Identifier $memberName "Enum '$name' member"
            if (-not $memberNames.Add($memberName)) {
                throw "Enum '$name' repeats member '$memberName'."
            }
            $value = [int](
                Get-RequiredMember $member 'value' "enum '$name.$memberName'")
            if ($value -ne $index) {
                throw "Enum '$name.$memberName' must use its declared wire ordinal."
            }
            $comma = if ($index -lt $members.Count - 1) { ',' } else { '' }
            $lines.Add("    $memberName = $value$comma")
        }
        $lines.Add('}')
        continue
    }
    if ($kind -in 'record', 'recordStruct', 'preparedBodyRecord') {
        $parameters = @(
            Get-RequiredMember $declaration 'parameters' "record '$name'")
        $prefix = if ($kind -eq 'recordStruct') {
            $readonly = [bool](
                Get-RequiredMember $declaration 'readonly' "record '$name'")
            if (-not $readonly) {
                throw "Record struct '$name' must be readonly."
            }
            "internal readonly record struct $name"
        }
        else {
            "internal sealed record $name"
        }
        $members = @(Get-MemberArray $declaration 'members')
        $hasBody = $members.Count -ne 0 -or $kind -eq 'preparedBodyRecord'
        Add-ParameterList `
            $lines $prefix $parameters $(if ($hasBody) { ' {' } else { ';' }) `
            "record '$name'"
        if (-not $hasBody) {
            continue
        }
        if ($kind -eq 'preparedBodyRecord') {
            $maximumInstructions = [int](
                Get-RequiredMember `
                    $declaration 'maximumInstructions' "record '$name'")
            if ($maximumInstructions -ne 4096) {
                throw "Record '$name' must preserve its replay instruction bound."
            }
            $lines.Add(
                "    internal const int MaximumInstructions = $maximumInstructions;")
            $lines.Add('')
            $lines.Add('    internal static CompilerPreparedBody Trivial() =>')
            $lines.Add('        new(')
            $lines.Add('            CompilerPreparedBodyKind.Trivial,')
            $lines.Add('            null,')
            $lines.Add('            ImmutableDictionary<IrVarId, IrVarId>.Empty,')
            $lines.Add(
                '            ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,')
            $lines.Add(
                '            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty);')
            $lines.Add('')
            $lines.Add('    internal static CompilerPreparedBody ProgramBody(')
            $lines.Add('        IrProgram program,')
            $lines.Add(
                '        ImmutableDictionary<IrVarId, IrVarId> parameterBindings,')
            $lines.Add(
                '        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> specCalls,')
            $lines.Add(
                '        ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall> summaryCalls) =>')
            $lines.Add('        new(')
            $lines.Add('            CompilerPreparedBodyKind.Program,')
            $lines.Add(
                '            program ?? throw new ArgumentNullException(nameof(program)),')
            $lines.Add('            parameterBindings,')
            $lines.Add('            specCalls,')
            $lines.Add('            summaryCalls);')
        }
        else {
            Add-RecordMembers $lines $members $name
        }
        $lines.Add('}')
        continue
    }
    if ($kind -ne 'class') {
        throw "Unsupported compiler-artifact declaration kind '$kind'."
    }
    $parameters = @(Get-MemberArray $declaration 'constructor')
    $parameterNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($parameter in $parameters) {
        [void]$parameterNames.Add([string](
            Get-RequiredMember $parameter 'name' "class '$name' constructor"))
    }
    if ($parameters.Count -eq 0) {
        $lines.Add("internal sealed class $name {")
    }
    else {
        Add-ParameterList `
            $lines "internal sealed class $name" $parameters ' {' "class '$name'"
    }
    Add-Properties `
        $lines @(Get-RequiredMember $declaration 'properties' "class '$name'") `
        $parameterNames $name $jsonNamingPolicy
    $lines.Add('}')
}

$envelope = Get-RequiredMember $schema 'artifactEnvelope' 'schema'
if ([string](Get-RequiredMember $envelope 'schema' 'artifact envelope') -ne
        'SharpProof.CompilerManifest' -or
    [int](Get-RequiredMember $envelope 'version' 'artifact envelope') -ne 18) {
    throw 'The compiler-artifact envelope must remain schema version 18.'
}

$catalogs = @(Get-RequiredMember $schema 'wireEnumCatalogs' 'schema')
$catalogFields = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$portableLines.Add('')
$portableLines.Add('internal static class PortableIrWireCatalog {')
foreach ($catalog in $catalogs) {
    $field = [string](Get-RequiredMember $catalog 'field' 'wire enum catalog')
    $type = [string](Get-RequiredMember $catalog 'type' "wire catalog '$field'")
    Assert-Identifier $field 'Wire catalog field'
    Assert-TypeName $type "Wire catalog '$field'"
    if (-not $catalogFields.Add($field)) {
        throw "Duplicate wire catalog '$field'."
    }
    $members = @(Get-RequiredMember $catalog 'members' "wire catalog '$field'")
    if ($members.Count -eq 0) {
        throw "Wire catalog '$field' cannot be empty."
    }
    $portableLines.Add("    internal static readonly $type[] $field = [")
    for ($index = 0; $index -lt $members.Count; $index++) {
        $member = [string]$members[$index]
        Assert-Identifier $member "Wire catalog '$field' member"
        $comma = if ($index -lt $members.Count - 1) { ',' } else { '' }
        $portableLines.Add("        $type.$member$comma")
    }
    $portableLines.Add('    ];')
}
$portableLines.Add('}')

$slotMappings = Get-RequiredMember $schema `
    'portableIrSlotMappings' 'schema'
$portableLines.Add('')
$portableLines.Add('internal readonly struct PortableIrSlotMapping(')
$portableLines.Add('    string kind,')
$portableLines.Add('    string[] slots)')
$portableLines.Add('{')
$portableLines.Add('    internal string Kind { get; } = kind;')
$portableLines.Add('    internal string[] Slots { get; } = slots;')
$portableLines.Add('}')
$slotDomains = @(
    Get-RequiredMember $schema 'portableIrSlotDomains' 'schema' |
        ForEach-Object {
            Assert-Properties -Object $_ -Allowed @('key', 'name', 'enum', 'kinds', 'slots') `
                -Context 'portable IR slot domain'
            [pscustomobject]@{
                Key = [string](Get-RequiredMember $_ 'key' 'portable IR slot domain')
                Name = [string](Get-RequiredMember $_ 'name' 'portable IR slot domain')
                Enum = [string](Get-RequiredMember $_ 'enum' 'portable IR slot domain')
                Kinds = @((Get-RequiredMember $_ 'kinds' 'portable IR slot domain') |
                    ForEach-Object { [string]$_ })
                Slots = @((Get-RequiredMember $_ 'slots' 'portable IR slot domain') |
                    ForEach-Object { [string]$_ })
            }
        })
$allowedSlotRoles = @(
    Get-RequiredMember $schema 'portableIrSlotRoles' 'schema' |
        ForEach-Object { [string]$_ })
$actualSlotDomains = @($slotMappings.PSObject.Properties.Name)
$declaredSlotDomains = @($slotDomains | ForEach-Object { $_.Key })
$actualSlotDomainSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($domainName in $actualSlotDomains) {
    [void]$actualSlotDomainSet.Add([string]$domainName)
}
$declaredSlotDomainSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($domainName in $declaredSlotDomains) {
    [void]$declaredSlotDomainSet.Add([string]$domainName)
}
foreach ($domainName in $actualSlotDomains) {
    if (-not $declaredSlotDomainSet.Contains([string]$domainName)) {
        throw "Portable IR slot mappings contain unsupported domain '$domainName'."
    }
}
foreach ($domainName in $declaredSlotDomains) {
    if (-not $actualSlotDomainSet.Contains([string]$domainName)) {
        throw "Portable IR slot mappings are missing domain '$domainName'."
    }
}
$slotDomainNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$portableLines.Add('internal static class PortableIrSlotCatalog {')
foreach ($domain in $slotDomains) {
    if (-not $slotDomainNames.Add($domain.Key)) {
        throw "Duplicate portable IR slot domain '$($domain.Key)'."
    }
    $rows = @(Get-RequiredMember $slotMappings $domain.Key `
        "portable IR slot domain '$($domain.Key)'")
    Assert-Identifier $domain.Name "Portable IR slot domain '$($domain.Key)' name"
    Assert-Identifier $domain.Enum "Portable IR slot domain '$($domain.Key)' enum"
    if ($domain.Kinds.Count -eq 0 -or $domain.Slots.Count -eq 0) {
        throw "Portable IR slot domain '$($domain.Key)' must declare kinds and slots."
    }
    if ($rows.Count -ne $domain.Kinds.Count) {
        throw (
            "Portable IR slot domain '$($domain.Key)' must contain " +
            "$($domain.Kinds.Count) rows.")
    }
    $portableLines.Add('')
    $portableLines.Add(
        "    internal static readonly PortableIrSlotMapping[] $($domain.Name) = [")
    for ($index = 0; $index -lt $rows.Count; $index++) {
        $row = $rows[$index]
        Assert-Properties `
            -Object $row `
            -Allowed @('kind', 'slots') `
            -Context "portable IR slot mapping '$($domain.Key)'"
        $kind = [string](Get-RequiredMember $row 'kind' `
            "portable IR slot mapping '$($domain.Key)'")
        if ($kind -ne $domain.Kinds[$index]) {
            throw (
                "Portable IR slot domain '$($domain.Key)' row $index " +
                "must describe '$($domain.Kinds[$index])'.")
        }
        $slots = @($row.slots | ForEach-Object { [string]$_ })
        if ($slots.Count -ne $domain.Slots.Count) {
            throw (
                "Portable IR slot mapping '$($domain.Key).$kind' must " +
                "define $($domain.Slots.Count) slots.")
        }
        for ($slotIndex = 0; $slotIndex -lt $slots.Count; $slotIndex++) {
            $role = $slots[$slotIndex]
            if ($role -notin $allowedSlotRoles) {
                throw (
                    "Portable IR slot mapping '$($domain.Key).$kind' " +
                    "has unsupported role '$role'.")
            }
            if ($role.StartsWith('wire:', [StringComparison]::Ordinal) -and
                -not $catalogFields.Contains($role.Substring(5))) {
                throw (
                    "Portable IR slot mapping '$($domain.Key).$kind' " +
                    "references missing wire catalog '$($role.Substring(5))'.")
            }
        }
        $slotLiterals = $slots | ForEach-Object {
            ConvertTo-CSharpString ([string]$_)
        }
        $portableLines.Add(
            '    new(' +
            (ConvertTo-CSharpString $kind) +
            ', [' +
            ($slotLiterals -join ', ') +
            ']),')
    }
    $portableLines.Add('    ];')
}
$portableLines.Add('}')

$metadataRowMappings = @(
    Get-RequiredMember $schema 'portableIrMetadataRowMappings' 'schema')
if ($metadataRowMappings.Count -eq 0) {
    throw 'Portable IR metadata-row mappings must contain at least one row.'
}
$metadataMethods = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$portableLines.Add('')
$portableLines.Add('internal static partial class PortableIrGraphCodec')
$portableLines.Add('{')
$portableLines.Add('    private sealed partial class Encoder')
$portableLines.Add('    {')
for ($mappingIndex = 0;
    $mappingIndex -lt $metadataRowMappings.Count;
    $mappingIndex++) {
    $mapping = $metadataRowMappings[$mappingIndex]
    Assert-Properties `
        -Object $mapping `
        -Allowed @('method', 'sourceType', 'rowType', 'infoMethod', 'arguments') `
        -Context 'portable IR metadata-row mapping'
    $method = [string]$mapping.method
    $sourceType = [string]$mapping.sourceType
    $rowType = [string]$mapping.rowType
    $infoMethod = [string]$mapping.infoMethod
    foreach ($value in @($method, $sourceType, $rowType, $infoMethod)) {
        Assert-Identifier $value 'Portable IR metadata-row identifier'
    }
    if (-not $metadataMethods.Add($method)) {
        throw "Duplicate portable IR metadata-row method '$method'."
    }
    $arguments = @($mapping.arguments)
    if ($arguments.Count -eq 0) {
        throw (
            "Portable IR metadata-row method '$method' must define " +
            'at least one argument.')
    }
    $expressions = [Collections.Generic.List[string]]::new()
    foreach ($argument in $arguments) {
        Assert-Properties `
            -Object $argument `
            -Allowed @('role', 'member') `
            -Context "portable IR metadata-row method '$method' argument"
        $role = [string]$argument.role
        $member = [string]$argument.member
        Assert-Identifier $member "Portable IR metadata-row method '$method' member"
        $expressions.Add((Get-MetadataRowExpression $role $member))
    }

    $portableLines.Add('')
    $portableLines.Add("        private $rowType $method($sourceType id)")
    $portableLines.Add('        {')
    $portableLines.Add("            var value = _factory.$infoMethod(id);")
    $portableLines.Add('            return new(')
    for ($argumentIndex = 0;
        $argumentIndex -lt $expressions.Count;
        $argumentIndex++) {
        $suffix = if ($argumentIndex -lt $expressions.Count - 1) { ',' } else { ');' }
        $portableLines.Add("                $($expressions[$argumentIndex])$suffix")
    }
    $portableLines.Add('        }')
}
$portableLines.Add('    }')
$portableLines.Add('}')

$portableProjectionSource = @'

internal static class PortableIrGraphCodecProjections
{
    internal static PortableIrTerm EncodeTerm(
        IrTerm term,
        Func<IrTypeId, int> typeIndex,
        Func<IrStringId, string> stringValue,
        Func<IrVarId, int> variableIndex,
        Func<IrTerm, int> termIndex,
        Func<IrMemberId, int> memberIndex,
        Func<OperationId, int> operationIndex,
        Func<IrTerm?, int> optionalTermIndex,
        Func<IEnumerable<IrTerm>, int[]> termIndices,
        Func<IrOpaquePurity, int> opaquePurity,
        Func<IrUnaryOperator, int> unaryOperator,
        Func<IrBinaryOperator, int> binaryOperator,
        Func<IrTerm, int, int, int, int, long, string?, int[]?, PortableIrTerm> row,
        Func<Exception> invalid)
    {
        return term switch
        {
            IrBooleanTerm value => row(term, value.Value ? 1 : 0, -1, -1, -1, 0, null, null),
            IrIntegerTerm value => row(term, -1, -1, -1, -1, value.Value, null, null),
            IrStringTerm value => row(term, -1, -1, -1, -1, 0, stringValue(value.Value), null),
            IrNullTerm => row(term, -1, -1, -1, -1, 0, null, null),
            IrVariableTerm value => row(term, variableIndex(value.Variable), -1, -1, -1, 0, null, null),
            IrOpaqueTerm value => row(
                term,
                memberIndex(value.Member),
                optionalTermIndex(value.Receiver),
                opaquePurity(value.Purity),
                value.Purity == IrOpaquePurity.Pure ? -1 : operationIndex(value.Operation),
                0,
                null,
                termIndices(value.Arguments)),
            IrUnaryTerm value => row(
                term,
                unaryOperator(value.Operator),
                termIndex(value.Operand),
                -1,
                -1,
                0,
                null,
                null),
            IrBinaryTerm value => row(
                term,
                binaryOperator(value.Operator),
                termIndex(value.Left),
                termIndex(value.Right),
                -1,
                0,
                null,
                null),
            IrConditionalTerm value => row(
                term,
                termIndex(value.Condition),
                termIndex(value.WhenTrue),
                termIndex(value.WhenFalse),
                -1,
                0,
                null,
                null),
            IrCastTerm value => row(term, termIndex(value.Operand), -1, -1, -1, 0, null, null),
            IrLengthTerm value => row(term, termIndex(value.Value), -1, -1, -1, 0, null, null),
            IrSequenceAccessTerm value => row(
                term,
                termIndex(value.Sequence),
                termIndex(value.Index),
                -1,
                -1,
                0,
                null,
                null),
            _ => throw invalid()
        };
    }

    internal static PortableIrLocation EncodeLocation(
        IrLocation location,
        Func<IrTypeId, int> typeIndex,
        Func<IrMemberId, int> memberIndex,
        Func<IrTerm?, int> optionalTermIndex,
        Func<IrTerm, int> termIndex,
        Func<IEnumerable<IrTerm>, int[]> termIndices,
        Func<IrLocation, int, int, int[]?, PortableIrLocation> row,
        Func<Exception> invalid)
    {
        return location switch
        {
            IrMemberLocation value => row(
                location,
                memberIndex(value.Member),
                optionalTermIndex(value.Receiver),
                termIndices(value.Arguments)),
            IrSequenceLocation value => row(
                location,
                termIndex(value.Sequence),
                termIndex(value.Index),
                null),
            _ => throw invalid()
        };
    }

    internal static PortableIrInstruction EncodeInstruction(
        IrInstruction instruction,
        Func<OperationId, int> operationIndex,
        Func<IrVarId, int> variableIndex,
        Func<IrTerm, int> termIndex,
        Func<IrVarId?, int> optionalVariableIndex,
        Func<IrMemberId, int> memberIndex,
        Func<IrTerm?, int> optionalTermIndex,
        Func<IrHavocKind, int> havocKind,
        Func<IrBlockId, int> blockIndex,
        Func<IEnumerable<IrTerm>, int[]> termIndices,
        Func<IEnumerable<IrVarId>, int[]> variableIndices,
        Func<IrLocation, PortableIrLocation> location,
        Func<IrInstruction, int, int, int, int, int[]?, PortableIrLocation?, PortableIrInstruction> row,
        Func<Exception> invalid)
    {
        return instruction switch
        {
            IrAssignInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                variableIndex(value.Target),
                termIndex(value.Value),
                -1,
                null,
                null),
            IrLoadInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                variableIndex(value.Target),
                -1,
                -1,
                null,
                location(value.Location)),
            IrStoreInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                termIndex(value.Value),
                -1,
                -1,
                null,
                location(value.Location)),
            IrCallInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                optionalVariableIndex(value.Target),
                memberIndex(value.Member),
                optionalTermIndex(value.Receiver),
                termIndices(value.Arguments),
                null),
            IrAssumeInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                termIndex(value.Condition),
                -1,
                -1,
                null,
                null),
            IrAssertInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                termIndex(value.Condition),
                -1,
                -1,
                null,
                null),
            IrHavocInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                havocKind(value.HavocKind),
                -1,
                -1,
                variableIndices(value.Variables),
                null),
            IrBranchInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                termIndex(value.Condition),
                blockIndex(value.WhenTrue),
                blockIndex(value.WhenFalse),
                null,
                null),
            IrGotoInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                blockIndex(value.Target),
                -1,
                -1,
                null,
                null),
            IrReturnInstruction value => row(
                instruction,
                operationIndex(instruction.Operation),
                optionalTermIndex(value.Value),
                -1,
                -1,
                null,
                null),
            _ => throw invalid()
        };
    }

    internal static IrTerm DecodeTerm(
        PortableIrTerm row,
        IrFactory factory,
        int depth,
        Func<int, IrTypeId> type,
        Func<int, int, IrTerm> term,
        Func<int, int, IrTerm?> optionalTerm,
        Func<int, IrVarId> variable,
        Func<int, IrMemberId> member,
        Func<int, OperationId> operation,
        Func<int[], int, IrTerm[]> terms,
        Func<int, IrOpaquePurity> opaquePurity,
        Func<int, IrUnaryOperator> unaryOperator,
        Func<int, IrBinaryOperator> binaryOperator,
        Func<Exception> invalid)
    {
        return row.Kind switch
        {
            IrTermKind.Boolean when row.A is 0 or 1 => factory.Boolean(row.A == 1),
            IrTermKind.Integer => factory.Integer(row.Number),
            IrTermKind.String when row.Text != null => factory.String(row.Text),
            IrTermKind.Null => factory.Null(type(row.Type)),
            IrTermKind.Variable => factory.Variable(variable(row.A)),
            IrTermKind.Opaque => DecodeOpaque(
                row,
                factory,
                depth,
                optionalTerm,
                member,
                operation,
                terms,
                opaquePurity,
                invalid),
            IrTermKind.Unary => factory.Unary(
                unaryOperator(row.A),
                term(row.B, depth + 1)),
            IrTermKind.Binary => factory.Binary(
                binaryOperator(row.A),
                term(row.B, depth + 1),
                term(row.C, depth + 1)),
            IrTermKind.Conditional => factory.Conditional(
                term(row.A, depth + 1),
                term(row.B, depth + 1),
                term(row.C, depth + 1)),
            IrTermKind.Cast => factory.Cast(type(row.Type), term(row.A, depth + 1)),
            IrTermKind.Length => factory.Length(term(row.A, depth + 1)),
            IrTermKind.SequenceAccess => factory.SequenceAccess(
                term(row.A, depth + 1),
                term(row.B, depth + 1)),
            _ => throw invalid()
        };
    }

    private static IrTerm DecodeOpaque(
        PortableIrTerm row,
        IrFactory factory,
        int depth,
        Func<int, int, IrTerm?> optionalTerm,
        Func<int, IrMemberId> member,
        Func<int, OperationId> operation,
        Func<int[], int, IrTerm[]> terms,
        Func<int, IrOpaquePurity> opaquePurity,
        Func<Exception> invalid)
    {
        var purity = opaquePurity(row.C);
        var receiver = optionalTerm(row.B, depth);
        var arguments = terms(row.Items, depth);
        return purity switch
        {
            IrOpaquePurity.Pure when row.D == -1 =>
                factory.PureOpaque(member(row.A), receiver, arguments),
            IrOpaquePurity.Impure =>
                factory.ImpureOpaque(operation(row.D), member(row.A), receiver, arguments),
            _ => throw invalid()
        };
    }

    internal static IrLocation DecodeLocation(
        IrProgramBuilder builder,
        PortableIrLocation row,
        Func<int, IrMemberId> member,
        Func<int, IrTerm?> optionalTerm,
        Func<int, IrTerm> term,
        Func<int[], IrTerm[]> terms,
        Func<Exception> invalid)
    {
        return row.Kind switch
        {
            IrLocationKind.Member => builder.MemberLocation(
                member(row.A),
                optionalTerm(row.B),
                terms(row.Items)),
            IrLocationKind.Sequence => builder.SequenceLocation(
                term(row.A),
                term(row.B)),
            _ => throw invalid()
        };
    }

    internal static IrInstruction DecodeInstruction(
        IrProgramBuilder builder,
        IrBlockId block,
        PortableIrInstruction row,
        Func<int, OperationId> operation,
        Func<int, IrVarId> variable,
        Func<int, IrMemberId> member,
        Func<int, IrTerm?> optionalTerm,
        Func<int, IrTerm> term,
        Func<int, IrVarId?> optionalVariable,
        Func<int, IrHavocKind> havocKind,
        Func<int, IrBlockId> blockAt,
        Func<PortableIrLocation?, IrLocation> location,
        Func<int[], IrTerm[]> terms,
        Func<int[], IrVarId[]> variables,
        Func<Exception> invalid)
    {
        return row.Kind switch
        {
            IrInstructionKind.Assign => builder.Assign(
                block,
                operation(row.Operation),
                variable(row.A),
                term(row.B)),
            IrInstructionKind.Load => builder.Load(
                block,
                operation(row.Operation),
                variable(row.A),
                location(row.Location)),
            IrInstructionKind.Store => builder.Store(
                block,
                operation(row.Operation),
                location(row.Location),
                term(row.A)),
            IrInstructionKind.Call => builder.Call(
                block,
                operation(row.Operation),
                optionalVariable(row.A),
                member(row.B),
                optionalTerm(row.C),
                terms(row.Items)),
            IrInstructionKind.Assume => builder.Assume(
                block,
                operation(row.Operation),
                term(row.A)),
            IrInstructionKind.Assert => builder.Assert(
                block,
                operation(row.Operation),
                term(row.A)),
            IrInstructionKind.Havoc => builder.Havoc(
                block,
                operation(row.Operation),
                havocKind(row.A),
                variables(row.Items)),
            IrInstructionKind.Branch => builder.Branch(
                block,
                operation(row.Operation),
                term(row.A),
                blockAt(row.B),
                blockAt(row.C)),
            IrInstructionKind.Goto => builder.Goto(
                block,
                operation(row.Operation),
                blockAt(row.A)),
            IrInstructionKind.Return => builder.Return(
                block,
                operation(row.Operation),
                optionalTerm(row.A)),
            _ => throw invalid()
        };
    }
}
'@
foreach ($line in ($portableProjectionSource -split "`r?`n")) {
    $portableLines.Add($line)
}

$collectorMappings = @(
    Get-RequiredMember $schema 'collectorWireMappings' 'schema')
if ($collectorMappings.Count -eq 0) {
    throw 'Collector wire catalog cannot be empty.'
}
$collectorMappingNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$collectorMappingOverloads = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($mapping in $collectorMappings) {
    $name = [string](
        Get-RequiredMember $mapping 'name' 'collector wire mapping')
    $owner = [string](
        Get-RequiredMember $mapping 'owner' "collector mapping '$name'")
    $method = [string](
        Get-RequiredMember $mapping 'method' "collector mapping '$name'")
    $kind = [string](
        Get-RequiredMember $mapping 'kind' "collector mapping '$name'")
    $sourceType = [string](
        Get-RequiredMember $mapping 'sourceType' "collector mapping '$name'")
    $targetType = [string](
        Get-RequiredMember $mapping 'targetType' "collector mapping '$name'")
    $unknownException = [string](
        Get-RequiredMember $mapping 'unknownException' "collector mapping '$name'")
    foreach ($identifier in @(
            $name,
            $owner,
            $method,
            $sourceType,
            $targetType,
            $unknownException)) {
        Assert-Identifier $identifier "Collector mapping '$name'"
    }
    if (-not $collectorMappingNames.Add($name)) {
        throw "Duplicate collector wire mapping '$name'."
    }
    if (-not $collectorMappingOverloads.Add(
            "$owner.$method($sourceType)")) {
        throw (
            "Duplicate collector wire overload '$owner.$method($sourceType)'.")
    }
    $validShape = switch ($owner) {
        'CompilerOptionWireMappings' {
            $method -eq 'Map' -and
            $kind -in 'enum', 'referenceIdentity' -and
            $unknownException -eq 'InvalidOperationException'
        }
        'CompilerEffectEvaluationWireMappings' {
            $method -eq 'ToWorker' -and
            $kind -eq 'enum' -and
            $unknownException -eq 'ArgumentOutOfRangeException'
        }
        'CompilerLoweringWireMappings' {
            $method -in 'ToCompiler', 'ToWorkerEvidence', 'ToWorkerFailure' -and
            $kind -eq 'enum' -and
            $unknownException -eq 'ArgumentOutOfRangeException'
        }
        'ClaimManifestBuilder' {
            $method -in 'ToWorkerEffects', 'ToWorkerCapabilities' -and
            $kind -eq 'flags' -and
            $unknownException -eq 'ArgumentOutOfRangeException'
        }
        default { $false }
    }
    if (-not $validShape) {
        throw "Collector mapping '$name' has an unsupported owner or shape."
    }
    $rows = @(Get-RequiredMember $mapping 'rows' "collector mapping '$name'")
    if ($rows.Count -eq 0) {
        throw "Collector mapping '$name' cannot be empty."
    }
    $sources = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $targets = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $allowTargetAliases = $false
    if ($mapping.PSObject.Properties['allowTargetAliases']) {
        $allowTargetAliases = [bool]$mapping.allowTargetAliases
        if (-not $allowTargetAliases -or $kind -ne 'enum') {
            throw "Collector mapping '$name' has an invalid target-alias setting."
        }
    }
    foreach ($row in $rows) {
        $source = [string](
            Get-RequiredMember $row 'source' "collector mapping '$name' row")
        $target = [string](
            Get-RequiredMember $row 'target' "collector mapping '$name' row")
        if ($kind -eq 'referenceIdentity') {
            if ($source -notmatch
                '^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$') {
                throw (
                    "Collector mapping '$name' has invalid source '$source'.")
            }
        }
        else {
            Assert-Identifier $source "Collector mapping '$name' source"
        }
        Assert-Identifier $target "Collector mapping '$name' target"
        if (-not $sources.Add($source)) {
            throw "Collector mapping '$name' repeats source '$source'."
        }
        if (-not $allowTargetAliases -and -not $targets.Add($target)) {
            throw "Collector mapping '$name' repeats target '$target'."
        }
    }
    if ($kind -eq 'flags') {
        $sourceMask = [string](
            Get-RequiredMember $mapping 'sourceMask' "collector mapping '$name'")
        if ($sourceMask -notmatch
            '^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Collector mapping '$name' has an invalid source mask."
        }
        if ([string]$rows[0].source -ne 'None' -or
            [string]$rows[0].target -ne 'None') {
            throw "Collector flags mapping '$name' must begin with None -> None."
        }
    }
    elseif ($mapping.PSObject.Properties['sourceMask']) {
        throw "Non-flags collector mapping '$name' cannot define sourceMask."
    }
}

$collectorMappingsByOwner = [ordered]@{}
foreach ($mapping in $collectorMappings) {
    $owner = [string]$mapping.owner
    if (-not $collectorMappingsByOwner.Contains($owner)) {
        $collectorMappingsByOwner[$owner] =
            [Collections.Generic.List[object]]::new()
    }
    $collectorMappingsByOwner[$owner].Add($mapping)
}
foreach ($owner in $collectorMappingsByOwner.Keys) {
    $ownerMappings = $collectorMappingsByOwner[$owner]
    $collectorLines.Add('')
    $declaration = if ($owner -eq 'ClaimManifestBuilder') {
        'internal sealed partial class'
    }
    else {
        'internal static partial class'
    }
    $collectorLines.Add("$declaration $owner {")
    foreach ($mapping in $ownerMappings) {
        $method = [string]$mapping.method
        $kind = [string]$mapping.kind
        $sourceType = [string]$mapping.sourceType
        $targetType = [string]$mapping.targetType
        $rows = @($mapping.rows)
        $parameterName = if ($kind -eq 'flags') { 'source' } else { 'value' }
        $collectorLines.Add(
            "    internal static $targetType $method($sourceType $parameterName) {")
        if ($kind -eq 'enum') {
            $collectorLines.Add("        return $parameterName switch {")
            foreach ($row in $rows) {
                $collectorLines.Add(
                    "            $sourceType.$($row.source) => " +
                    "$targetType.$($row.target),")
            }
            $throw = if ([string]$mapping.unknownException -eq
                'InvalidOperationException') {
                "throw Unsupported(nameof($sourceType), $parameterName)"
            }
            else {
                "throw new ArgumentOutOfRangeException(nameof($parameterName))"
            }
            $collectorLines.Add("            _ => $throw")
            $collectorLines.Add('        };')
        }
        elseif ($kind -eq 'referenceIdentity') {
            foreach ($row in $rows) {
                $collectorLines.Add(
                    "        if (ReferenceEquals($parameterName, $($row.source)))")
                $collectorLines.Add(
                    "            return $targetType.$($row.target);")
            }
            $collectorLines.Add('        throw new InvalidOperationException(')
            $collectorLines.Add(
                '            "A custom assembly identity comparer is unsupported.");')
        }
        elseif ($kind -eq 'flags') {
            $collectorLines.Add(
                "        if (($parameterName & ~$($mapping.sourceMask)) != 0)")
            $collectorLines.Add(
                "            throw new ArgumentOutOfRangeException(nameof($parameterName));")
            $collectorLines.Add(
                "        var result = $targetType.$($rows[0].target);")
            for ($rowIndex = 1; $rowIndex -lt $rows.Count; $rowIndex++) {
                $row = $rows[$rowIndex]
                $collectorLines.Add(
                    "        if (($parameterName & $sourceType.$($row.source)) != 0)")
                $collectorLines.Add(
                    "            result |= $targetType.$($row.target);")
            }
            $collectorLines.Add('        return result;')
        }
        else {
            throw "Collector mapping '$($mapping.name)' has unsupported kind '$kind'."
        }
        $collectorLines.Add('    }')
        $collectorLines.Add('')
    }
    if ($owner -eq 'CompilerOptionWireMappings') {
        $collectorLines.Add('    private static InvalidOperationException Unsupported<T>(')
        $collectorLines.Add('        string name,')
        $collectorLines.Add('        T value)')
        $collectorLines.Add('        where T : struct {')
        $collectorLines.Add(
            '        return new($"The compiler option ''{name}'' has unsupported value ''{value}''.");')
        $collectorLines.Add('    }')
    }
    elseif ($collectorLines[$collectorLines.Count - 1] -eq '') {
        $collectorLines.RemoveAt($collectorLines.Count - 1)
    }
    $collectorLines.Add('}')
}

$callableReasons = Get-RequiredMember `
    $schema 'compilerCallableReasons' 'schema'
$callableSuccessReason = [string](Get-RequiredMember `
    $callableReasons 'success' 'compilerCallableReasons')
$callableDiagnosticFailure = [string](Get-RequiredMember `
    $callableReasons 'diagnosticFailure' 'compilerCallableReasons')
$callableFailureReasons = @(Get-RequiredMember `
    $callableReasons 'failures' 'compilerCallableReasons')
foreach ($reason in @(
        $callableSuccessReason,
        $callableDiagnosticFailure) + $callableFailureReasons) {
    Assert-Identifier $reason 'Compiler callable reason'
}
if ($callableFailureReasons.Count -eq 0 -or
        $callableFailureReasons -contains $callableSuccessReason -or
        $callableFailureReasons -notcontains $callableDiagnosticFailure -or
        @($callableFailureReasons | Select-Object -Unique).Count -ne
            $callableFailureReasons.Count) {
    throw 'Compiler callable reason catalog is invalid.'
}

$evidence = Get-RequiredMember $schema 'effectEvidence' 'schema'
$domain = [string](Get-RequiredMember $evidence 'domain' 'effect evidence')
$evidenceVersion = [int](Get-RequiredMember $evidence 'version' 'effect evidence')
if ($domain -ne 'SharpProof.CompilerEffectClaimEvidence' -or
    $evidenceVersion -ne 9) {
    throw 'Compiler effect evidence must preserve domain version 9.'
}
$protocolSchema = Get-Content -LiteralPath $ProtocolSchemaPath -Raw |
    ConvertFrom-Json -Depth 100
$effectCertaintyTables = @(
    (Get-MemberArray $protocolSchema 'validationTables') |
        Where-Object { [string]$_.name -ceq 'EffectCertainty' })
if ($effectCertaintyTables.Count -ne 1) {
    throw 'Protocol schema must define exactly one EffectCertainty table.'
}
$effectCertaintyTable = $effectCertaintyTables[0]
$effectCertaintyParameters = @(
    Get-RequiredMember $effectCertaintyTable 'parameters' 'EffectCertainty table')
if (($effectCertaintyParameters | ForEach-Object { [string]$_.name }) -join ',' -cne
        'outcome,reason,certainty') {
    throw 'Protocol EffectCertainty table must use outcome, reason, certainty order.'
}
$effectTupleRows = [Collections.Generic.List[object]]::new()
$unknownReasons = [Collections.Generic.List[string]]::new()
$unknownReasonSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($row in @(Get-RequiredMember $effectCertaintyTable 'rows' 'EffectCertainty table')) {
    $values = @($row)
    if ($values.Count -ne 3) {
        throw 'Protocol EffectCertainty table contains a row with the wrong width.'
    }
    $outcome = [string]$values[0]
    $reason = [string]$values[1]
    $certainty = [string]$values[2]
    if ([string]::IsNullOrWhiteSpace($outcome) -or
        [string]::IsNullOrWhiteSpace($reason) -or
        [string]::IsNullOrWhiteSpace($certainty)) {
        throw 'Protocol EffectCertainty table contains a blank tuple member.'
    }
    if ($outcome -eq '*' -or $certainty -eq '*') {
        throw 'Protocol EffectCertainty table may wildcard only its reason member.'
    }
    Assert-Identifier $outcome 'Effect evidence tuple outcome'
    Assert-Identifier $certainty 'Effect evidence tuple certainty'
    if ($reason -eq '*') {
        continue
    }
    Assert-Identifier $reason 'Effect evidence tuple reason'
    $effectTupleRows.Add([PSCustomObject]@{
        Outcome = $outcome
        Reason = $reason
        Certainty = $certainty
    })
    if ($outcome -eq 'Unknown' -and $unknownReasonSet.Add($reason)) {
        $unknownReasons.Add($reason)
    }
}
if ($effectTupleRows.Count -eq 0 -or $unknownReasons.Count -eq 0) {
    throw 'Protocol EffectCertainty table must define explicit compiler effect tuples.'
}
$constraintKinds = Get-RequiredMember `
    $evidence 'constraintKinds' 'effect evidence'
$capabilitiesKind = [string](
    Get-RequiredMember $constraintKinds 'capabilities' 'effect evidence constraints')
$exceptionsKind = [string](
    Get-RequiredMember $constraintKinds 'exceptions' 'effect evidence constraints')
$combinedKind = [string](
    Get-RequiredMember $constraintKinds 'combined' 'effect evidence constraints')
foreach ($kind in $capabilitiesKind, $exceptionsKind, $combinedKind) {
    Assert-Identifier $kind 'Effect evidence constraint kind'
}
$replay = Get-RequiredMember $evidence 'replay' 'effect evidence'
$replayPathKind = [string](
    Get-RequiredMember $replay 'pathKind' 'effect replay')
Assert-Identifier $replayPathKind 'Effect replay path kind'
$maximumReplayEvents = [int](
    Get-RequiredMember $replay 'maximumEvents' 'effect replay')
if ($maximumReplayEvents -ne 256) {
    throw 'Compiler effect replay must preserve its 256-event bound.'
}
$supportedReplayEventKinds = @(
    Get-RequiredMember $replay 'supportedEventKinds' 'effect replay')
if ($supportedReplayEventKinds.Count -eq 0) {
    throw 'Compiler effect replay must define supported event kinds.'
}
foreach ($kind in $supportedReplayEventKinds) {
    Assert-Identifier ([string]$kind) 'Supported effect replay event kind'
}
if (-not [bool](Get-RequiredMember `
        $evidence 'sortAllowedExceptionTypes' 'effect evidence') -or
    -not [bool](Get-RequiredMember `
        $evidence 'sortWitnessExceptionHierarchy' 'effect evidence')) {
    throw 'Compiler effect evidence must use canonical exception ordering.'
}

$constraintRuleKinds = @(
    'EnforcePure',
    'ZeroAllocations',
    'AllowedCapabilities',
    'DoesNotThrow',
    'AllowedExceptions',
    'EffectContract')
$modelLines.Add('')
$modelLines.Add('internal readonly struct CompilerEffectConstraintRule(')
$modelLines.Add('    WorkerEffectContractKind kind,')
$modelLines.Add('    bool effectsMustBeEmpty,')
$modelLines.Add('    bool capabilitiesMustBeEmpty,')
$modelLines.Add('    bool exceptionsMustBeEmpty)')
$modelLines.Add('{')
$modelLines.Add('    internal WorkerEffectContractKind Kind { get; } = kind;')
$modelLines.Add('    internal bool EffectsMustBeEmpty { get; } = effectsMustBeEmpty;')
$modelLines.Add('    internal bool CapabilitiesMustBeEmpty { get; } = capabilitiesMustBeEmpty;')
$modelLines.Add('    internal bool ExceptionsMustBeEmpty { get; } = exceptionsMustBeEmpty;')
$modelLines.Add('}')
$modelLines.Add('')
$modelLines.Add('internal static class CompilerEffectEvidenceCatalog {')
$modelLines.Add('    internal const string ConstraintDomain = "SharpProof.CompilerEffectReplayConstraint";')
$modelLines.Add('    internal const int ConstraintVersion = 1;')
$modelLines.Add('    internal const string OperationDomain = "SharpProof.CompilerEffectReplayOperation";')
$modelLines.Add('    internal const int OperationVersion = 1;')
$modelLines.Add("    internal const string EvidenceDomain = $(ConvertTo-CSharpString $domain);")
$modelLines.Add("    internal const int EvidenceVersion = $evidenceVersion;")
$modelLines.Add('    internal const int MaximumReplayEvents = ' +
    $maximumReplayEvents + ';')
$modelLines.Add('    internal const CompilerEffectReplayPathKind ReplayPathKind =')
$modelLines.Add("        CompilerEffectReplayPathKind.$replayPathKind;")
$modelLines.Add('    internal static readonly WorkerClaimReason[] UnknownReasons = [')
foreach ($reason in $unknownReasons) {
    $modelLines.Add("        WorkerClaimReason.$reason,")
}
$modelLines.Add('    ];')
$modelLines.Add('    internal static readonly (WorkerClaimOutcome Outcome, WorkerClaimReason Reason,')
$modelLines.Add('        WorkerEffectEvidenceCertainty Certainty)[] SupportedEffectTuples = [')
foreach ($row in $effectTupleRows) {
    $modelLines.Add(
        "        (WorkerClaimOutcome.$($row.Outcome), " +
        "WorkerClaimReason.$($row.Reason), " +
        "WorkerEffectEvidenceCertainty.$($row.Certainty)),")
}
$modelLines.Add('    ];')
$modelLines.Add('    internal static bool HasValidEffectTuple(')
$modelLines.Add('        WorkerClaimOutcome outcome, WorkerClaimReason reason,')
$modelLines.Add('        WorkerEffectEvidenceCertainty certainty)')
$modelLines.Add('    {')
$modelLines.Add('        foreach (var tuple in SupportedEffectTuples)')
$modelLines.Add('        {')
$modelLines.Add('            if (tuple.Outcome == outcome && tuple.Reason == reason &&')
$modelLines.Add('                tuple.Certainty == certainty)')
$modelLines.Add('            {')
$modelLines.Add('                return true;')
$modelLines.Add('            }')
$modelLines.Add('        }')
$modelLines.Add('        return outcome == WorkerClaimOutcome.Unknown &&')
$modelLines.Add('            certainty == WorkerEffectEvidenceCertainty.Unavailable &&')
$modelLines.Add('            UnknownReasons.Contains(reason);')
$modelLines.Add('    }')
$modelLines.Add('    internal static readonly CompilerEffectConstraintRule[] ConstraintRules = [')
foreach ($kind in $constraintRuleKinds) {
    $effectsEmpty = if ($kind -eq $combinedKind) { 'false' } else { 'true' }
    $capabilitiesEmpty = if ($kind -in $capabilitiesKind, $combinedKind) { 'false' } else { 'true' }
    $exceptionsEmpty = if ($kind -in $exceptionsKind, $combinedKind) { 'false' } else { 'true' }
    $modelLines.Add(
        "        new(WorkerEffectContractKind.$kind, $effectsEmpty, " +
        "$capabilitiesEmpty, $exceptionsEmpty),")
}
$modelLines.Add('    ];')
$modelLines.Add('    internal static readonly CompilerEffectReplayEventKind[] SupportedReplayEventKinds = [')
foreach ($kind in $supportedReplayEventKinds) {
    $modelLines.Add("        CompilerEffectReplayEventKind.$kind,")
}
$modelLines.Add('    ];')
$modelLines.Add('}')
$modelLines.Add('')
$modelLines.Add('internal static class CompilerCallableArtifactReasonCatalog {')
$modelLines.Add(
    "    internal const WorkerClaimReason SuccessReason = WorkerClaimReason.$callableSuccessReason;")
$modelLines.Add(
    "    internal const WorkerClaimReason DiagnosticFailureReason = WorkerClaimReason.$callableDiagnosticFailure;")
$modelLines.Add('    internal static readonly WorkerClaimReason[] FailureReasons = [')
foreach ($reason in $callableFailureReasons) {
    $modelLines.Add("        WorkerClaimReason.$reason,")
}
$modelLines.Add('    ];')
$modelLines.Add('    internal static bool IsFailureReason(WorkerClaimReason reason) =>')
$modelLines.Add('        Array.IndexOf(FailureReasons, reason) >= 0;')
$modelLines.Add('}')

$outputs = [ordered]@{
    $ModelOutputPath = $modelLines
    $PortableOutputPath = $portableLines
    $CompilationOutputPath = $compilationLines
    $CollectorOutputPath = $collectorLines
}
foreach ($output in $outputs.GetEnumerator()) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output.Key)) |
        Out-Null
    Update-SharpProofGeneratedFile `
        -Path $output.Key `
        -Content ($output.Value -join "`n") `
        -DisplayPath $output.Key `
        -GeneratorCommand '.\scripts\Generate-CompilerArtifactModel.ps1' `
        -Verify:$Verify
}
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic compiler-artifact model."
