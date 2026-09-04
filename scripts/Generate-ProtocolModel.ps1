[CmdletBinding()]
param(
    [Parameter()][string]$SchemaPath,
    [Parameter()][string]$OutputPath,
    [Parameter()][string]$AnalyzerOutputPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$SchemaPath = Resolve-SharpProofPath $SchemaPath (
    Join-Path $repositoryRoot 'SharpProof.Worker.Protocol\ProtocolModel.schema.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Worker.Protocol\ProtocolModel.generated.cs')
$AnalyzerOutputPath = Resolve-SharpProofPath $AnalyzerOutputPath (
    Join-Path $repositoryRoot 'SharpProof.Analyzer.Core\EffectEvaluationProducerTupleCatalog.generated.cs')
if (-not [IO.File]::Exists($SchemaPath)) {
    throw "Protocol schema not found: $SchemaPath"
}

function Add-WrappedAlternatives {
    param(
        [Collections.Generic.List[string]]$Lines,
        [string]$Prefix,
        [string]$Continuation,
        [string[]]$Alternatives,
        [string]$Suffix = '')
    if ($Alternatives.Count -eq 0) {
        throw 'A generated alternative list cannot be empty.'
    }
    $current = $Prefix + $Alternatives[0]
    foreach ($alternative in $Alternatives | Select-Object -Skip 1) {
        $addition = ' or ' + $alternative
        if (($current.Length + $addition.Length + $Suffix.Length) -le 140) {
            $current += $addition
        }
        else {
            $Lines.Add($current)
            $current = $Continuation + 'or ' + $alternative
        }
    }
    $Lines.Add($current + $Suffix)
}

function ConvertTo-ConstantSource {
    param([string]$Type, [object]$Value, [string]$Context)
    switch ($Type) {
        'string' { return ConvertTo-CSharpString ([string]$Value) }
        'int' {
            return ([Convert]::ToInt32(
                $Value, [Globalization.CultureInfo]::InvariantCulture)
            ).ToString([Globalization.CultureInfo]::InvariantCulture)
        }
        'uint' {
            return ([Convert]::ToUInt32(
                $Value, [Globalization.CultureInfo]::InvariantCulture)
            ).ToString([Globalization.CultureInfo]::InvariantCulture) + 'U'
        }
        'long' {
            return ([Convert]::ToInt64(
                $Value, [Globalization.CultureInfo]::InvariantCulture)
            ).ToString([Globalization.CultureInfo]::InvariantCulture) + 'L'
        }
        default { throw "$Context has unsupported constant type '$Type'." }
    }
}

function ConvertTo-InitializerSource {
    param([object]$Default, [string]$Context)
    $kind = [string](Get-RequiredMember $Default 'kind' $Context)
    switch ($kind) {
        'implicit' { return '' }
        'stringEmpty' { return ' = string.Empty' }
        'new' { return ' = new()' }
        'emptyArray' { return ' = []' }
        'true' { return ' = true' }
        'member' {
            $value = [string](Get-RequiredMember $Default 'value' $Context)
            if ($value -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') {
                throw "$Context has invalid member default '$value'."
            }
            return " = $value"
        }
        'constructorAssigned' { return '' }
        'computed' { return '' }
        default { throw "$Context has unsupported default kind '$kind'." }
    }
}

function Resolve-ValidationProperty {
    param(
        [string]$RootType,
        [string]$Path,
        [string]$ValueName = 'value')
    if ($Path -notmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$') {
        throw "Validation property path is invalid: '$Path'."
    }
    $currentType = $RootType
    $source = $ValueName
    foreach ($segment in $Path.Split('.')) {
        $declaration = $null
        if (-not $declarationByName.TryGetValue(
                $currentType.TrimEnd('?'), [ref]$declaration)) {
            throw "Validation path '$Path' cannot traverse '$currentType'."
        }
        $property = @(Get-MemberArray $declaration 'properties' |
            Where-Object {
                [string](Get-RequiredMember $_ 'name' `
                    "type '$currentType' property") -ceq $segment
            })
        if ($property.Count -ne 1) {
            throw "Validation path '$Path' does not resolve '$segment'."
        }
        $source += ".$segment"
        $currentType = [string](
            Get-RequiredMember $property[0] 'type' `
                "property '$currentType.$segment'")
    }
    return [PSCustomObject]@{
        Source = $source
        Type = $currentType
    }
}

function Assert-ValidationMember {
    param([string]$Member, [string]$Context)
    if ($Member -notmatch '^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$' -or
        (-not $constantNames.Contains($Member) -and
         -not $enumMemberNames.Contains($Member))) {
        throw "$Context references unknown member '$Member'."
    }
}

function ConvertTo-ValidationConditionListSource {
    param(
        [object]$Condition,
        [string]$RootType,
        [string]$Operation,
        [string]$JoinOperator)
    $parts = @(Get-RequiredMember `
        $Condition 'conditions' "validation '$Operation'" |
        ForEach-Object {
            ConvertTo-ValidationConditionSource $_ $RootType
        })
    if ($parts.Count -eq 0) {
        throw "Validation '$Operation' requires conditions."
    }
    return '(' + ($parts -join "`n$JoinOperator ") + ')'
}

function ConvertTo-ValidationConditionSource {
    param([object]$Condition, [string]$RootType)
    $operation = [string](
        Get-RequiredMember $Condition 'op' "validation for '$RootType'")
    $propertyMember = $Condition.PSObject.Properties['property']
    $property = if ($null -eq $propertyMember) {
        $null
    }
    else {
        Resolve-ValidationProperty `
            $RootType ([string]$propertyMember.Value)
    }
    switch ($operation) {
        'property' { return $property.Source }
        'all' {
            return ConvertTo-ValidationConditionListSource `
                -Condition $Condition -RootType $RootType `
                -Operation $operation -JoinOperator '&&'
        }
        'any' {
            return ConvertTo-ValidationConditionListSource `
                -Condition $Condition -RootType $RootType `
                -Operation $operation -JoinOperator '||'
        }
        'implies' {
            $antecedent = ConvertTo-ValidationConditionSource (
                Get-RequiredMember $Condition 'if' "validation '$operation'") `
                $RootType
            $consequent = ConvertTo-ValidationConditionSource (
                Get-RequiredMember $Condition 'then' "validation '$operation'") `
                $RootType
            return "(!($antecedent)`n|| ($consequent))"
        }
        'notNull' { return "$($property.Source) != null" }
        'nonBlank' {
            return "!string.IsNullOrWhiteSpace($($property.Source))"
        }
        'singleLine' {
            return "WorkerProtocolJson.IsSingleLineText($($property.Source))"
        }
        'sha256' {
            return "WorkerProtocolJson.IsSha256($($property.Source))"
        }
        'equalsMember' {
            $member = [string](
                Get-RequiredMember $Condition 'member' "validation '$operation'")
            Assert-ValidationMember $member "Validation '$operation'"
            return "$($property.Source) == $member"
        }
        'notEqualsMember' {
            $member = [string](
                Get-RequiredMember $Condition 'member' "validation '$operation'")
            Assert-ValidationMember $member "Validation '$operation'"
            return "$($property.Source) != $member"
        }
        'defined' {
            $unspecified = [string](
                Get-RequiredMember `
                    $Condition 'unspecified' "validation '$operation'")
            Assert-ValidationMember $unspecified "Validation '$operation'"
            if (-not $definedEnumNames.Contains($property.Type.TrimEnd('?'))) {
                throw "Validation '$operation' uses an enum outside definedEnums."
            }
            return "WorkerProtocolJson.IsDefined(" +
                "$($property.Source), $unspecified)"
        }
        'positive' { return "$($property.Source) > 0" }
        'nonNegative' { return "$($property.Source) >= 0" }
        'lessOrEqualProperty' {
            $other = Resolve-ValidationProperty $RootType (
                [string](Get-RequiredMember `
                    $Condition 'other' "validation '$operation'"))
            return "$($property.Source) <= $($other.Source)"
        }
        'between' {
            $minimum = [Convert]::ToInt64(
                (Get-RequiredMember `
                    $Condition 'minimum' "validation '$operation'"),
                [Globalization.CultureInfo]::InvariantCulture)
            $maximumMember =
                $Condition.PSObject.Properties['maximumMember']
            if ($null -ne $maximumMember) {
                $maximum = [string]$maximumMember.Value
                Assert-ValidationMember $maximum "Validation '$operation'"
            }
            else {
                $maximum = [Convert]::ToInt64(
                    (Get-RequiredMember `
                        $Condition 'maximum' "validation '$operation'"),
                    [Globalization.CultureInfo]::InvariantCulture
                ).ToString([Globalization.CultureInfo]::InvariantCulture)
            }
            return "$($property.Source) is >= $minimum and <= $maximum"
        }
        'uniqueEnums' {
            $unspecified = [string](
                Get-RequiredMember `
                    $Condition 'unspecified' "validation '$operation'")
            Assert-ValidationMember $unspecified "Validation '$operation'"
            $nonEmpty = [bool](
                Get-RequiredMember `
                    $Condition 'nonEmpty' "validation '$operation'")
            if (-not $definedEnumNames.Contains(
                    $property.Type.TrimEnd('?').TrimEnd('[]'))) {
                throw "Validation '$operation' uses an enum outside definedEnums."
            }
            return "WorkerProtocolJson.AreDefinedUnique(" +
                "$($property.Source), $unspecified, " +
                $nonEmpty.ToString().ToLowerInvariant() + ')'
        }
        'distinctNonblank' {
            return "WorkerProtocolJson.AreDistinctNonblank($($property.Source))"
        }
        'validAssumptions' {
            return "WorkerProtocolJson.AreValidAssumptions($($property.Source))"
        }
        'assumptionsUnused' {
            return "($($property.Source) ?? []).All(" +
                'static item => item != null && !item.Used)'
        }
        'knownFlags' {
            return "WorkerProtocolMetadata.HasOnlyKnownFlags(" +
                "$($property.Source))"
        }
        'flagContains' {
            $member = [string](
                Get-RequiredMember $Condition 'member' "validation '$operation'")
            Assert-ValidationMember $member "Validation '$operation'"
            return "($($property.Source) & $member) != 0"
        }
        'hasItems' { return "$($property.Source) is { Length: > 0 }" }
        'validModel' {
            return "WorkerProtocolJson.AreValidModel($($property.Source))"
        }
        'validPlan' {
            $plan = [string](
                Get-RequiredMember $Condition 'plan' "validation '$operation'")
            $planMetadata = $null
            if (-not $validationPlansByName.TryGetValue(
                    $plan,
                    [ref]$planMetadata) -or
                $planMetadata.Type -ne $property.Type.TrimEnd('?')) {
                throw "Validation references incompatible plan '$plan'."
            }
            if ($planMetadata.Mode -eq 'predicate') {
                return "WorkerProtocolMetadata.Is${plan}Valid($($property.Source))"
            }
            return "WorkerProtocolMetadata.${plan}Rules.All(" +
                "rule => rule.IsValid($($property.Source)))"
        }
        'stateTable' {
            $table = [string](
                Get-RequiredMember $Condition 'table' "validation '$operation'")
            if (-not $validationTableNames.Contains($table)) {
                throw "Validation references unknown state table '$table'."
            }
            $arguments = @(Get-RequiredMember `
                $Condition 'arguments' "validation '$operation'" |
                ForEach-Object {
                    ConvertTo-ValidationConditionSource $_ $RootType
                })
            return "WorkerProtocolMetadata.Matches$table(" +
                ($arguments -join ', ') + ')'
        }
        default {
            throw "Unsupported validation operation '$operation'."
        }
    }
}

$schema = Read-SharpProofSchema `
    -Path $SchemaPath `
    -Context 'protocol-model'
$namespace = [string]$schema.namespace
$jsonNamingPolicy = [string]$schema.jsonNamingPolicy
$declarations = @(Get-RequiredMember $schema 'declarations' 'schema')
$requiredJsonRoots = @(
    Get-RequiredMember $schema 'requiredJsonRoots' 'schema')
$manifestNameEnums = @(
    Get-RequiredMember $schema 'manifestNameEnums' 'schema')
$definedEnums = @(
    Get-RequiredMember $schema 'definedEnums' 'schema')
$validationTables = @(Get-MemberArray $schema 'validationTables')
$validationPlans = @(Get-MemberArray $schema 'validationPlans')
$manifestIdentity = Get-RequiredMember $schema 'manifestIdentity' 'schema'
$effectCertaintyTables = @($validationTables | Where-Object {
        [string]$_.name -ceq 'EffectCertainty'
    })
if ($effectCertaintyTables.Count -ne 1) {
    throw 'Protocol schema must define exactly one EffectCertainty table.'
}
$effectCertaintyRows = @(
    Get-RequiredMember $effectCertaintyTables[0] 'rows' 'EffectCertainty table')
$analyzerVocabulary = Get-RequiredMember `
    $schema `
    'analyzerProducerVocabulary' `
    'schema'
$analyzerOutcomes = [Collections.Generic.HashSet[string]]::new(
    [string[]](Get-RequiredMember $analyzerVocabulary 'outcomes' `
        'analyzer producer vocabulary'),
    [StringComparer]::Ordinal)
$analyzerReasons = [Collections.Generic.HashSet[string]]::new(
    [string[]](Get-RequiredMember $analyzerVocabulary 'reasons' `
        'analyzer producer vocabulary'),
    [StringComparer]::Ordinal)
$analyzerCertainties = [Collections.Generic.HashSet[string]]::new(
    [string[]](Get-RequiredMember $analyzerVocabulary 'certainties' `
        'analyzer producer vocabulary'),
    [StringComparer]::Ordinal)
$producerTupleRows = [Collections.Generic.List[object]]::new()
foreach ($row in $effectCertaintyRows) {
    $values = @($row)
    if ($values.Count -ne 3) {
        throw 'Protocol EffectCertainty table contains a row with the wrong width.'
    }
    $outcome = [string]$values[0]
    $reason = [string]$values[1]
    $certainty = [string]$values[2]
    if ($reason -eq '*' -or
        -not $analyzerOutcomes.Contains($outcome) -or
        -not $analyzerReasons.Contains($reason) -or
        -not $analyzerCertainties.Contains($certainty)) {
        continue
    }
    $producerTupleRows.Add([PSCustomObject]@{
        Outcome = $outcome
        Reason = $reason
        Certainty = $certainty
    })
}
if ($producerTupleRows.Count -eq 0) {
    throw 'Protocol EffectCertainty table has no analyzer producer tuples.'
}
$validationTableNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($table in $validationTables) {
    $tableName = [string](
        Get-RequiredMember $table 'name' 'validation table')
    Assert-Identifier $tableName 'Validation table name'
    if (-not $validationTableNames.Add($tableName)) {
        throw "Duplicate validation table '$tableName'."
    }
}
$validationPlansByName = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
foreach ($plan in $validationPlans) {
    $planName = [string](
        Get-RequiredMember $plan 'name' 'validation plan')
    $planType = [string](
        Get-RequiredMember $plan 'type' "validation plan '$planName'")
    Assert-Identifier $planName 'Validation plan name'
    if ($validationPlansByName.ContainsKey($planName)) {
        throw "Duplicate validation plan '$planName'."
    }
    $modeMember = $plan.PSObject.Properties['mode']
    $mode = if ($null -eq $modeMember) { 'rules' } else { [string]$modeMember.Value }
    if ($mode -notin @('rules', 'predicate')) {
        throw "Validation plan '$planName' has invalid mode '$mode'."
    }
    $validationPlansByName.Add($planName, [pscustomobject]@{
        Type = $planType
        Mode = $mode
    })
}
$declarationByName = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
$constantNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$enumMemberNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$lines = New-SharpProofGeneratedHeader `
    -Generator 'scripts/Generate-ProtocolModel.ps1' `
    -Source 'SharpProof.Worker.Protocol/ProtocolModel.schema.json.' `
    -Nullable
foreach ($using in @(Get-RequiredMember $schema 'usings' 'schema')) {
    $lines.Add("using $using;")
}
$lines.Add('')
$lines.Add("namespace $namespace;")

foreach ($declaration in $declarations) {
    $kind = [string](Get-RequiredMember $declaration 'kind' 'declaration')
    $name = [string](Get-RequiredMember $declaration 'name' 'declaration')
    Assert-Identifier $name 'Declaration name'
    if (-not $declarationByName.TryAdd($name, $declaration)) {
        throw "Duplicate protocol declaration '$name'."
    }
    $lines.Add('')
    $constants = @(Get-MemberArray $declaration 'constants')
    $constantMemberNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    if ($kind -eq 'staticClass') {
        $lines.Add("public static class $name {")
    }
    elseif ($kind -eq 'class') {
        $lines.Add("public sealed class $name {")
    }
    elseif ($kind -eq 'enum') {
        $underlyingType = [string](
            Get-RequiredMember $declaration 'underlyingType' "enum '$name'")
        $isFlags = [bool](Get-RequiredMember $declaration 'flags' "enum '$name'")
        if ($underlyingType -notin @('int', 'long')) {
            throw "Enum '$name' has unsupported underlying type '$underlyingType'."
        }
        if ($isFlags) {
            $lines.Add('[Flags]')
        }
        $suffix = if ($underlyingType -eq 'long') { ' : long' } else { '' }
        $members = @(Get-RequiredMember $declaration 'members' "enum '$name'")
        $memberNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $memberValues = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $memberSources = @($members | ForEach-Object {
            $member = $_
            $memberName = [string](
                Get-RequiredMember $member 'name' "enum '$name' member")
            Assert-Identifier $memberName "Enum '$name' member"
            if (-not $memberNames.Add($memberName)) {
                throw "Enum '$name' repeats member '$memberName'."
            }
            [void]$enumMemberNames.Add("$name.$memberName")
            $memberValue = Get-RequiredMember `
                $member 'value' "enum '$name' member '$memberName'"
            $memberSource = ConvertTo-ConstantSource `
                $underlyingType $memberValue "enum '$name' member '$memberName'"
            if (-not $memberValues.Add($memberSource)) {
                throw "Enum '$name' repeats value '$memberSource'."
            }
            "$memberName = $memberSource"
        })
        $lines.Add("public enum $name$suffix {")
        for ($memberIndex = 0;
             $memberIndex -lt $memberSources.Count;
             $memberIndex++) {
            $comma = if ($memberIndex -lt $memberSources.Count - 1) { ',' } else { '' }
            $lines.Add("    $($memberSources[$memberIndex])$comma")
        }
        $lines.Add('}')
        continue
    }
    else {
        throw "Unsupported protocol declaration kind '$kind'."
    }

    foreach ($constant in $constants) {
        $constantName = [string](
            Get-RequiredMember $constant 'name' "type '$name' constant")
        Assert-Identifier $constantName "Type '$name' constant"
        if (-not $constantMemberNames.Add($constantName)) {
            throw "Type '$name' repeats constant '$constantName'."
        }
        [void]$constantNames.Add("$name.$constantName")
        $constantType = [string](
            Get-RequiredMember $constant 'type' "constant '$name.$constantName'")
        $constantValue = Get-RequiredMember `
            $constant 'value' "constant '$name.$constantName'"
        $lines.Add(
            "    public const $constantType $constantName = " +
            (ConvertTo-ConstantSource `
                $constantType $constantValue "constant '$name.$constantName'") +
            ';')
    }

    $constructorMember = $declaration.PSObject.Properties['constructor']
    if ($null -ne $constructorMember -and $null -ne $constructorMember.Value) {
        $constructor = $constructorMember.Value
        $accessibility = [string](
            Get-RequiredMember $constructor 'accessibility' "constructor '$name'")
        $parameters = @(
            Get-RequiredMember $constructor 'parameters' "constructor '$name'")
        $parameterSource = @($parameters | ForEach-Object {
            "$([string](Get-RequiredMember $_ 'type' "constructor '$name' parameter")) " +
            "$([string](Get-RequiredMember $_ 'name' "constructor '$name' parameter"))"
        }) -join ', '
        $assignment = Get-RequiredMember `
            $constructor 'assignment' "constructor '$name'"
        if ([string](Get-RequiredMember `
                $assignment 'kind' "constructor '$name' assignment") -ne
            'collectionCopy') {
            throw "Constructor '$name' has an unsupported assignment."
        }
        $target = [string](
            Get-RequiredMember $assignment 'target' "constructor '$name' assignment")
        $source = [string](
            Get-RequiredMember $assignment 'source' "constructor '$name' assignment")
        $lines.Add("    $accessibility $name($parameterSource) =>")
        $lines.Add("        $target = [.. $source];")
    }

    $properties = @(Get-MemberArray $declaration 'properties')
    $propertyNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $jsonNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    for ($index = 0; $index -lt $properties.Count; $index++) {
        $property = $properties[$index]
        $propertyName = [string](
            Get-RequiredMember $property 'name' "type '$name' property")
        Assert-Identifier $propertyName "Type '$name' property"
        if (-not $propertyNames.Add($propertyName)) {
            throw "Type '$name' repeats property '$propertyName'."
        }
        $jsonName = [string](
            Get-RequiredMember $property 'jsonName' "property '$name.$propertyName'")
        $order = [int](
            Get-RequiredMember $property 'order' "property '$name.$propertyName'")
        if ($order -ne $index -or -not $jsonNames.Add($jsonName)) {
            throw "Property '$name.$propertyName' has invalid JSON order or name."
        }
        $expectedJsonName =
            $propertyName.Substring(0, 1).ToLowerInvariant() +
            $propertyName.Substring(1)
        if ($jsonName -ne $expectedJsonName) {
            throw "Property '$name.$propertyName' does not match camelCase."
        }
        $propertyType = [string](
            Get-RequiredMember $property 'type' "property '$name.$propertyName'")
        $hasSetter = [bool](
            Get-RequiredMember $property 'set' "property '$name.$propertyName'")
        $default = Get-RequiredMember `
            $property 'default' "property '$name.$propertyName'"
        $defaultKind = [string](
            Get-RequiredMember $default 'kind' "property '$name.$propertyName' default")
        if ($defaultKind -eq 'computed') {
            $operation = [string](
                Get-RequiredMember $default 'operation' `
                    "property '$name.$propertyName' default")
            $source = [string](
                Get-RequiredMember $default 'source' `
                    "property '$name.$propertyName' default")
            if ($operation -ne 'isDefaultOrEmpty' -or $hasSetter) {
                throw "Property '$name.$propertyName' has invalid computed semantics."
            }
            $lines.Add("    public $propertyType $propertyName => $source.IsDefaultOrEmpty;")
            continue
        }
        $accessors = if ($hasSetter) { '{ get; set; }' } else { '{ get; }' }
        $initializer = ConvertTo-InitializerSource `
            $default "property '$name.$propertyName' default"
        $terminator = if ($initializer.Length -eq 0) { '' } else { ';' }
        $lines.Add(
            "    public $propertyType $propertyName $accessors$initializer$terminator")
    }
    $lines.Add('}')
}

$lines.Add('')
$lines.Add('internal readonly struct WorkerProtocolRule<T>(string code, Func<T, bool> isValid) {')
$lines.Add('    internal readonly string Code = code;')
$lines.Add('    internal readonly Func<T, bool> IsValid = isValid;')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal static class WorkerProtocolMetadata {')
$requiredRootNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($nameValue in $requiredJsonRoots) {
    $name = [string]$nameValue
    if (-not $requiredRootNames.Add($name) -or
        -not $declarationByName.ContainsKey($name) -or
        [string](Get-RequiredMember $declarationByName[$name] 'kind' `
            "required JSON root '$name'") -ne 'class') {
        throw "Required JSON root '$name' is not a unique class."
    }
}
$lines.Add('')
$lines.Add('    internal static readonly IReadOnlyDictionary<string, WorkerProtocolJsonObjectShape> JsonObjectShapes =')
$lines.Add('        new Dictionary<string, WorkerProtocolJsonObjectShape>(StringComparer.Ordinal) {')
foreach ($declaration in $declarations) {
    if ([string](Get-RequiredMember $declaration 'kind' 'protocol declaration') -ne 'class') {
        continue
    }
    $name = [string](Get-RequiredMember $declaration 'name' 'class declaration')
    $lines.Add("            [$([char]34)$name$([char]34)] = new([")
    foreach ($property in @(Get-MemberArray $declaration 'properties')) {
        $jsonName = [string](Get-RequiredMember $property 'jsonName' "property '$name'")
        $propertyType = [string](Get-RequiredMember $property 'type' "property '$name.$jsonName'")
        $lines.Add("                new(" +
            (ConvertTo-CSharpString $jsonName) + ', ' +
            (ConvertTo-CSharpString $propertyType) + '),')
    }
    $lines.Add('            ]),')
}
$lines.Add('        };')
$lines.Add('')
$lines.Add('    private static readonly HashSet<Enum> s_knownValues = [')
$definedEnumNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($nameValue in $definedEnums) {
    $name = [string]$nameValue
    if (-not $definedEnumNames.Add($name) -or
        -not $declarationByName.ContainsKey($name) -or
        [string](Get-RequiredMember $declarationByName[$name] 'kind' `
            "defined enum '$name'") -ne 'enum' -or
        [bool](Get-RequiredMember $declarationByName[$name] 'flags' `
            "defined enum '$name'")) {
        throw "Defined enum '$name' is not a unique non-flags enum."
    }
    $members = @(
        Get-RequiredMember $declarationByName[$name] 'members' "enum '$name'" |
        ForEach-Object {
            "$name.$([string](Get-RequiredMember $_ 'name' "enum '$name' member"))"
        })
    $current = '        '
    foreach ($member in $members) {
        $addition = $member + ', '
        if ($current.Trim().Length -gt 0 -and
            ($current.Length + $addition.Length) -gt 140) {
            $lines.Add($current.TrimEnd())
            $current = '        '
        }
        $current += $addition
    }
    $lines.Add($current.TrimEnd())
}
$lines.Add('    ];')
$lines.Add('    internal static bool IsKnown<T>(T value) where T : struct, Enum =>')
$lines.Add('        s_knownValues.Contains((Enum)(object)value);')
$manifestEnumNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($nameValue in $manifestNameEnums) {
    $name = [string]$nameValue
    if (-not $manifestEnumNames.Add($name) -or
        -not $declarationByName.ContainsKey($name) -or
        [string](Get-RequiredMember $declarationByName[$name] 'kind' `
            "manifest-name enum '$name'") -ne 'enum') {
        throw "Manifest-name enum '$name' is not a unique enum."
    }
}
$lines.Add('    internal static string? GetManifestName(Enum value) => value switch {')
foreach ($name in $manifestNameEnums) {
    foreach ($member in @(
            Get-RequiredMember $declarationByName[$name] 'members' "enum '$name'")) {
        $memberName = [string](
            Get-RequiredMember $member 'name' "enum '$name' member")
        $lines.Add("        $name.$memberName => nameof($name.$memberName),")
    }
}
$lines.Add('        _ => null')
$lines.Add('    };')
$assumptionDeclaration = $declarationByName['WorkerAssumptionKind']
$lines.Add('    internal static int GetAssumptionOrder(WorkerAssumptionKind value) => value switch {')
$assumptionMembers = @(
    Get-RequiredMember $assumptionDeclaration 'members' "enum 'WorkerAssumptionKind'")
for ($index = 0; $index -lt $assumptionMembers.Count; $index++) {
    $memberName = [string](
        Get-RequiredMember $assumptionMembers[$index] 'name' `
            "enum 'WorkerAssumptionKind' member")
    $lines.Add("        WorkerAssumptionKind.$memberName => $index,")
}
$lines.Add(
    '        _ => throw new ArgumentOutOfRangeException(' +
    'nameof(value), value, "The manifest contains an unknown enum value.")')
$lines.Add('    };')
foreach ($declaration in $declarations) {
    if ([string](Get-RequiredMember $declaration 'kind' 'declaration') -ne
        'enum' -or
        -not [bool](Get-RequiredMember $declaration 'flags' 'enum')) {
        continue
    }
    $name = [string](Get-RequiredMember $declaration 'name' 'declaration')
    $members = @(
        Get-RequiredMember $declaration 'members' "enum '$name'")
    $allKnown = @($members | Where-Object {
        [string](Get-RequiredMember $_ 'name' "enum '$name' member") -eq
            'AllKnown'
    })
    if ($allKnown.Count -ne 1) {
        throw "Flags enum '$name' must define one AllKnown member."
    }
    $lines.Add(
        "    internal static bool HasOnlyKnownFlags($name value) => " +
        "(value & ~$name.AllKnown) == 0;")
}
$lines.Add('')
foreach ($table in $validationTables) {
    $tableName = [string](
        Get-RequiredMember $table 'name' 'validation table')
    $parameters = @(
        Get-RequiredMember $table 'parameters' "validation table '$tableName'")
    $parameterNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $parameterOrder = [Collections.Generic.List[string]]::new()
    $signature = @($parameters | ForEach-Object {
        $parameter = $_
        $parameterName = [string](
            Get-RequiredMember `
                $parameter 'name' "validation table '$tableName' parameter")
        $parameterType = [string](
            Get-RequiredMember `
                $parameter 'type' "validation table '$tableName' parameter")
        Assert-Identifier $parameterName "Validation table '$tableName' parameter"
        if (-not $parameterNames.Add($parameterName)) {
            throw "Validation table '$tableName' repeats parameter '$parameterName'."
        }
        $parameterOrder.Add($parameterName)
        if ($parameterType -ne 'bool' -and
            (-not $declarationByName.ContainsKey($parameterType) -or
             [string](Get-RequiredMember `
                $declarationByName[$parameterType] 'kind' `
                "validation table '$tableName' type") -ne 'enum')) {
            throw "Validation table '$tableName' has invalid type '$parameterType'."
        }
        "$parameterType $parameterName"
    }) -join ', '
    $patterns = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $rowRecords = [Collections.Generic.List[object]]::new()
    $containsWildcard = $false
    foreach ($row in @(
            Get-RequiredMember $table 'rows' "validation table '$tableName'")) {
        $values = @($row)
        if ($values.Count -ne $parameters.Count) {
            throw "Validation table '$tableName' has a row with the wrong width."
        }
        $parts = for ($index = 0; $index -lt $values.Count; $index++) {
            $parameterType = [string](
                Get-RequiredMember $parameters[$index] 'type' `
                    "validation table '$tableName' parameter")
            $value = $values[$index]
            if ($value -is [string] -and $value -ceq '*') {
                $containsWildcard = $true
                '_'
            }
            elseif ($parameterType -eq 'bool') {
                if ($value -isnot [bool]) {
                    throw "Validation table '$tableName' expects a Boolean."
                }
                $value.ToString().ToLowerInvariant()
            }
            else {
                $member = "$parameterType.$([string]$value)"
                Assert-ValidationMember `
                    $member "Validation table '$tableName'"
                $member
            }
        }
        $pattern = '(' + ($parts -join ', ') + ')'
        if (-not $patterns.Add($pattern)) {
            throw "Validation table '$tableName' repeats row '$pattern'."
        }
        $rowRecords.Add([PSCustomObject]@{
            Pattern = $pattern
            Parts = @($parts)
        })
    }
    $lines.Add("    internal static bool Matches$tableName($signature) =>")
    if ($parameters.Count -eq 2 -and -not $containsWildcard) {
        $firstGroups = [Collections.Generic.List[object]]::new()
        foreach ($row in $rowRecords) {
            $first = [string]$row.Parts[0]
            $group = @($firstGroups | Where-Object { $_.First -ceq $first })
            if ($group.Count -eq 0) {
                $group = [PSCustomObject]@{
                    First = $first
                    Seconds = [Collections.Generic.List[string]]::new()
                }
                $firstGroups.Add($group)
            }
            else {
                $group = $group[0]
            }
            $group.Seconds.Add([string]$row.Parts[1])
        }
        $groups = [Collections.Generic.List[object]]::new()
        foreach ($firstGroup in $firstGroups) {
            $key = $firstGroup.Seconds -join "`0"
            $group = @($groups | Where-Object { $_.Key -ceq $key })
            if ($group.Count -eq 0) {
                $group = [PSCustomObject]@{
                    Key = $key
                    Firsts = [Collections.Generic.List[string]]::new()
                    Seconds = $firstGroup.Seconds
                }
                $groups.Add($group)
            }
            else {
                $group = $group[0]
            }
            $group.Firsts.Add([string]$firstGroup.First)
        }
        $groupedLinesFit = $true
        foreach ($group in $groups) {
            $firstPattern = $group.Firsts -join ' or '
            $prefix = "            $firstPattern => $($parameterOrder[1]) is "
            if (($prefix.Length + $group.Seconds[0].Length + 1) -gt 140) {
                $groupedLinesFit = $false
            }
        }
        if ($groupedLinesFit) {
            $lines.Add("        $($parameterOrder[0]) switch {")
            foreach ($group in $groups) {
                $firstPattern = $group.Firsts -join ' or '
                Add-WrappedAlternatives $lines `
                    "            $firstPattern => $($parameterOrder[1]) is " `
                    '                ' $group.Seconds.ToArray() ','
            }
            $lines.Add('            _ => false')
            $lines.Add('        };')
            continue
        }
    }
    $lines.Add("        ($($parameterOrder -join ', ')) is")
    Add-WrappedAlternatives $lines '            ' '            ' `
        @($rowRecords | ForEach-Object { [string]$_.Pattern }) ';'
}
$lines.Add('')
foreach ($plan in $validationPlans) {
    $planName = [string](
        Get-RequiredMember $plan 'name' 'validation plan')
    $planType = [string](
        Get-RequiredMember $plan 'type' "validation plan '$planName'")
    Assert-Identifier $planName 'Validation plan name'
    if (-not $declarationByName.ContainsKey($planType) -or
        [string](Get-RequiredMember `
            $declarationByName[$planType] 'kind' `
            "validation plan '$planName' type") -ne 'class') {
        throw "Validation plan '$planName' has invalid type '$planType'."
    }
    $rules = @(
        Get-RequiredMember $plan 'rules' "validation plan '$planName'")
    if ($validationPlansByName[$planName].Mode -eq 'predicate') {
        if ($rules.Count -ne 1) {
            throw "Predicate validation plan '$planName' must have one rule."
        }
        $code = [string](
            Get-RequiredMember $rules[0] 'code' "validation plan '$planName' rule")
        if ([string]::IsNullOrWhiteSpace($code)) {
            throw "Validation plan '$planName' has an invalid rule code."
        }
        $condition = ConvertTo-ValidationConditionSource (
            Get-RequiredMember $rules[0] 'condition' `
                "validation plan '$planName' rule '$code'") $planType
        $conditionLines = @($condition -split "`n")
        $lines.Add("    internal static bool Is${planName}Valid($planType value) =>")
        for ($index = 0; $index -lt $conditionLines.Count; $index++) {
            $suffix = if ($index -eq $conditionLines.Count - 1) { ';' } else { '' }
            $lines.Add("        $($conditionLines[$index])$suffix")
        }
        continue
    }
    $lines.Add(
        "    internal static readonly WorkerProtocolRule<$planType>[] " +
        "${planName}Rules = [")
    $ruleCodes = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($rule in $rules) {
        $code = [string](
            Get-RequiredMember $rule 'code' "validation plan '$planName' rule")
        if ([string]::IsNullOrWhiteSpace($code) -or
            -not $ruleCodes.Add($code)) {
            throw "Validation plan '$planName' has an invalid rule code."
        }
        $condition = ConvertTo-ValidationConditionSource (
            Get-RequiredMember `
                $rule 'condition' "validation plan '$planName' rule '$code'") `
            $planType
        $conditionLines = @($condition -split "`n")
        $singleLine = '        new(' + (ConvertTo-CSharpString $code) +
            ', static value => ' + $condition + '),'
        if ($conditionLines.Count -eq 1 -and $singleLine.Length -le 140) {
            $lines.Add($singleLine)
            continue
        }
        $lines.Add(
            '        new(' + (ConvertTo-CSharpString $code) +
            ', static value =>')
        for ($index = 0; $index -lt $conditionLines.Count; $index++) {
            $suffix = if ($index -eq $conditionLines.Count - 1) { '),' } else { '' }
            $lines.Add("            $($conditionLines[$index])$suffix")
        }
    }
    $lines.Add('    ];')
}
$lines.Add('}')
$manifestDomain = [string](
    Get-RequiredMember $manifestIdentity 'domain' 'manifest identity')
$manifestVersionMember = [string](
    Get-RequiredMember `
        $manifestIdentity 'versionMember' 'manifest identity')
Assert-ValidationMember $manifestVersionMember 'Manifest identity'
$manifestFieldKinds = [ordered]@{
    string = 'String'
    int = 'Int'
    enum = 'Enum'
    location = 'Location'
    enumArray = 'EnumArray'
    ordinalStringArray = 'OrdinalStringArray'
    assumptionArray = 'AssumptionArray'
}
$lines.Add('')
$lines.Add('internal enum WorkerManifestIdentityFieldKind {')
foreach ($kind in $manifestFieldKinds.Values) {
    $lines.Add("    $kind,")
}
$lines.Add('}')
$lines.Add('')
$lines.Add('internal readonly struct WorkerManifestIdentityField')
$lines.Add('{')
$lines.Add('    internal WorkerManifestIdentityField(')
$lines.Add('        string label, string property,')
$lines.Add('        WorkerManifestIdentityFieldKind kind, string? defaultMember)')
$lines.Add('    {')
$lines.Add('        Label = label;')
$lines.Add('        Property = property;')
$lines.Add('        Kind = kind;')
$lines.Add('        DefaultMember = defaultMember;')
$lines.Add('    }')
$lines.Add('    internal string Label { get; }')
$lines.Add('    internal string Property { get; }')
$lines.Add('    internal WorkerManifestIdentityFieldKind Kind { get; }')
$lines.Add('    internal string? DefaultMember { get; }')
$lines.Add('}')
$lines.Add('internal readonly struct WorkerManifestIdentityOrder')
$lines.Add('{')
$lines.Add('    internal WorkerManifestIdentityOrder(string property, string kind)')
$lines.Add('    {')
$lines.Add('        Property = property;')
$lines.Add('        Kind = kind;')
$lines.Add('    }')
$lines.Add('    internal string Property { get; }')
$lines.Add('    internal string Kind { get; }')
$lines.Add('}')
$lines.Add('internal readonly struct WorkerManifestIdentityCollection')
$lines.Add('{')
$lines.Add('    internal WorkerManifestIdentityCollection(')
$lines.Add('        string property, string lengthLabel, string entryLabel,')
$lines.Add('        WorkerManifestIdentityOrder[] order,')
$lines.Add('        WorkerManifestIdentityField[] fields)')
$lines.Add('    {')
$lines.Add('        Property = property;')
$lines.Add('        LengthLabel = lengthLabel;')
$lines.Add('        EntryLabel = entryLabel;')
$lines.Add('        Order = order;')
$lines.Add('        Fields = fields;')
$lines.Add('    }')
$lines.Add('    internal string Property { get; }')
$lines.Add('    internal string LengthLabel { get; }')
$lines.Add('    internal string EntryLabel { get; }')
$lines.Add('    internal WorkerManifestIdentityOrder[] Order { get; }')
$lines.Add('    internal WorkerManifestIdentityField[] Fields { get; }')
$lines.Add('}')
$lines.Add('')
$lines.Add('internal static class WorkerManifestIdentityCatalog {')
$lines.Add("    internal const string Domain = $(ConvertTo-CSharpString $manifestDomain);")
$lines.Add('    internal static readonly WorkerManifestIdentityField[] RootFields = [')
foreach ($field in @(Get-RequiredMember `
        $manifestIdentity 'rootFields' 'manifest identity')) {
    $kind = [string](Get-RequiredMember $field 'kind' 'manifest root field')
    if (-not $manifestFieldKinds.Contains($kind)) {
        throw "Unsupported manifest field kind '$kind'."
    }
    $lines.Add(
        '        new(' +
        (ConvertTo-CSharpString ([string](Get-RequiredMember $field 'label' 'manifest root field'))) +
        ', ' +
        (ConvertTo-CSharpString ([string](Get-RequiredMember $field 'property' 'manifest root field'))) +
        ", WorkerManifestIdentityFieldKind.$($manifestFieldKinds[$kind]), null),")
}
$lines.Add('    ];')
$lines.Add('    internal static readonly WorkerManifestIdentityCollection[] Collections = [')
foreach ($collection in @(Get-RequiredMember `
        $manifestIdentity 'collections' 'manifest identity')) {
    $property = [string](Get-RequiredMember $collection 'property' 'manifest collection')
    $lines.Add('        new(' +
        (ConvertTo-CSharpString $property) + ', ' +
        (ConvertTo-CSharpString ([string](Get-RequiredMember $collection 'lengthLabel' 'manifest collection'))) + ', ' +
        (ConvertTo-CSharpString ([string](Get-RequiredMember $collection 'entryLabel' 'manifest collection'))) + ', [')
    foreach ($order in @(Get-RequiredMember $collection 'order' "manifest collection '$property'")) {
        $lines.Add('            new(' +
            (ConvertTo-CSharpString ([string](Get-RequiredMember $order 'property' 'manifest order'))) + ', ' +
            (ConvertTo-CSharpString ([string](Get-RequiredMember $order 'kind' 'manifest order'))) + '),')
    }
    $lines.Add('        ], [')
    foreach ($field in @(Get-RequiredMember $collection 'fields' "manifest collection '$property'")) {
        $kind = [string](Get-RequiredMember $field 'kind' 'manifest field')
        if (-not $manifestFieldKinds.Contains($kind)) {
            throw "Unsupported manifest field kind '$kind'."
        }
        $defaultMember = if ($field.PSObject.Properties.Name -contains 'defaultMember') {
            ConvertTo-CSharpString ([string]$field.defaultMember)
        }
        else {
            'null'
        }
        $lines.Add('            new(' +
            (ConvertTo-CSharpString ([string](Get-RequiredMember $field 'label' 'manifest field'))) + ', ' +
            (ConvertTo-CSharpString ([string](Get-RequiredMember $field 'property' 'manifest field'))) +
            ", WorkerManifestIdentityFieldKind.$($manifestFieldKinds[$kind]), $defaultMember),")
    }
    $lines.Add('        ]),')
}
$lines.Add('    ];')
$lines.Add('}')
$versionMembers = Get-RequiredMember $schema 'versionMembers' 'schema'
foreach ($role in $versionMembers.PSObject.Properties) {
    if (-not $constantNames.Contains([string]$role.Value)) {
        throw "Version role '$($role.Name)' references unknown constant '$($role.Value)'."
    }
}

$producerLines = New-SharpProofGeneratedHeader `
    -Generator 'scripts/Generate-ProtocolModel.ps1' `
    -Source 'SharpProof.Worker.Protocol/ProtocolModel.schema.json.' `
    -Nullable
$producerLines.Add('')
$producerLines.Add('namespace SharpProof.Analyzer;')
$producerLines.Add('')
$producerLines.Add('internal static class EffectEvaluationProducerTupleCatalog')
$producerLines.Add('{')
$producerLines.Add('    internal static bool IsDefined(')
$producerLines.Add('        EffectEvaluationOutcome outcome, EffectEvaluationReason reason,')
$producerLines.Add('        EffectEvaluationCertainty certainty) =>')
$producerLines.Add('        (outcome, reason, certainty) is')
$producerPatterns = @($producerTupleRows | ForEach-Object {
        "(EffectEvaluationOutcome.$($_.Outcome), " +
        "EffectEvaluationReason.$($_.Reason), " +
        "EffectEvaluationCertainty.$($_.Certainty))"
    })
Add-WrappedAlternatives $producerLines '        ' '        ' $producerPatterns ';'
$producerLines.Add('')
$producerLines.Add('    internal static (EffectEvaluationOutcome Outcome,')
$producerLines.Add('        EffectEvaluationReason Reason, EffectEvaluationCertainty Certainty) Require(')
$producerLines.Add('        EffectEvaluationOutcome outcome, EffectEvaluationReason reason,')
$producerLines.Add('        EffectEvaluationCertainty certainty)')
$producerLines.Add('    {')
$producerLines.Add('        if (!IsDefined(outcome, reason, certainty))')
$producerLines.Add('        {')
$producerLines.Add('            throw new InvalidOperationException(')
$producerLines.Add('                "Effect producer emitted an unsupported reason-certainty tuple.");')
$producerLines.Add('        }')
$producerLines.Add('        return (outcome, reason, certainty);')
$producerLines.Add('    }')
$producerLines.Add('}')
$producerContent = $producerLines -join "`n"
$content = $lines -join "`n"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) |
    Out-Null
Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content $content `
    -DisplayPath $OutputPath `
    -GeneratorCommand '.\scripts\Generate-ProtocolModel.ps1' `
    -Verify:$Verify
$producerDisplayPath = $AnalyzerOutputPath
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($AnalyzerOutputPath)) |
    Out-Null
Update-SharpProofGeneratedFile `
    -Path $AnalyzerOutputPath `
    -Content $producerContent `
    -DisplayPath $producerDisplayPath `
    -GeneratorCommand '.\scripts\Generate-ProtocolModel.ps1' `
    -Verify:$Verify
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic worker protocol model."
