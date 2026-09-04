[CmdletBinding()]
param(
    [Parameter()]
    [string]$CatalogPath,

    [Parameter()]
    [string]$SourceOutputPath,

    [Parameter()]
    [string]$DocumentationOutputPath,

    [Parameter()]
    [string]$RuntimeWitnessOutputPath,

    [Parameter()]
    [Alias('Check')]
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$CatalogPath = Resolve-SharpProofPath $CatalogPath (
    Join-Path $repositoryRoot 'SharpProof.Specs\DefaultApiSpecCatalog.json')
$SourceOutputPath = Resolve-SharpProofPath $SourceOutputPath (
    Join-Path $repositoryRoot 'SharpProof.Specs\DefaultApiSpecCatalog.generated.cs')
$DocumentationOutputPath = Resolve-SharpProofPath $DocumentationOutputPath (
    Join-Path $repositoryRoot 'docs\api-spec-catalog.generated.md')
$RuntimeWitnessOutputPath = Resolve-SharpProofPath $RuntimeWitnessOutputPath (
    Join-Path $repositoryRoot 'SharpProof.Specs.Test\ApiSpecRuntimeWitnesses.generated.cs')
if (-not [IO.File]::Exists($CatalogPath)) {
    throw "API-spec catalog not found: $CatalogPath"
}

$allowedEnums = @{
    SpecEvidenceKind = @('Documented', 'Observed')
    SpecEffect = @(
        'None',
        'Unknown',
        'ReadsReceiverState',
        'ReadsArgumentState',
        'WritesReceiverState',
        'WritesArgumentState',
        'ReadsAmbientState',
        'WritesAmbientState',
        'InputOutput',
        'Synchronization',
        'NativeCode',
        'Reflection',
        'Nondeterminism')
    SpecAllocationBehavior = @('None', 'MayAllocate', 'Unknown')
    SpecThrowBehavior = @('DoesNotThrow', 'MayThrow', 'Unknown')
    SpecTerminationBehavior = @('Terminates', 'Unknown')
    SpecNullness = @(
        'NotApplicable',
        'NonNull',
        'MaybeNull',
        'Null',
        'Unknown')
    SpecCardinality = @(
        'NotApplicable',
        'Empty',
        'NonEmpty',
        'Exact',
        'Unknown')
    ApiSpecReferenceFamily = @(
        'MicrosoftNetCoreReferencePack',
        'NetStandardReferencePack',
        'NetFrameworkReferenceAssemblies',
        'MicrosoftNetCoreRuntime',
        'SharpProofPackage')
    SpecTargetMemberKind = @('Constructor', 'Method', 'PropertyGet')
    IrTypeKind = @(
        'Boolean',
        'Integer',
        'String',
        'Reference',
        'Sequence')
    SpecVariableRole = @('Receiver', 'Parameter', 'Result')
    IrUnaryOperator = @('Not', 'Negate')
    IrBinaryOperator = @(
        'Add',
        'Subtract',
        'Multiply',
        'Divide',
        'Remainder',
        'AndAlso',
        'OrElse',
        'Equal',
        'NotEqual',
        'LessThan',
        'LessThanOrEqual',
        'GreaterThan',
        'GreaterThanOrEqual',
        'StringConcat')
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Context is missing required property '$Name'."
    }
    return ,$property.Value
}

function Get-OptionalProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Get-RequiredArrayProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $value = Get-RequiredProperty `
        -Object $Object `
        -Name $Name `
        -Context $Context
    if ($value -isnot [Array]) {
        throw "$Context.$Name must be a JSON array."
    }
    return $value
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string[]]$Names,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $expected = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($name in $Names) {
        [void]$expected.Add($name)
    }
    $actual = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $Object.PSObject.Properties) {
        [void]$actual.Add($property.Name)
    }
    foreach ($name in $Names) {
        if (-not $actual.Contains($name)) {
            throw "$Context is missing required property '$name'."
        }
    }
    foreach ($name in $actual) {
        if (-not $expected.Contains($name)) {
            throw "$Context contains unexpected property '$name'."
        }
    }
}

function Assert-Text {
    param(
        [AllowNull()]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context,

        [switch]$AllowEmpty
    )

    if ($Value -isnot [string] -or
        (-not $AllowEmpty -and
            [string]::IsNullOrWhiteSpace([string]$Value))) {
        throw "$Context must be a string$(
            if ($AllowEmpty) { '.' } else { ' with content.' })"
    }
    return [string]$Value
}

function Assert-JsonInt64 {
    param(
        [AllowNull()]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value -isnot [long]) {
        throw "$Context must be a JSON integer."
    }
    return [long]$Value
}

function Assert-JsonInt32 {
    param(
        [AllowNull()]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $number = Assert-JsonInt64 -Value $Value -Context $Context
    if ($number -lt [int]::MinValue -or $number -gt [int]::MaxValue) {
        throw "$Context must be a 32-bit JSON integer."
    }
    return [int]$number
}

function Assert-EnumValue {
    param(
        [AllowNull()]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Type,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $text = Assert-Text -Value $Value -Context $Context
    if ($allowedEnums[$Type] -notcontains $text) {
        throw "$Context contains unsupported $Type value '$text'."
    }
    return $text
}

function ConvertTo-PascalIdentifier {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "$Context '$Value' is not a generator-safe identifier."
    }
    return $Value.Substring(0, 1).ToUpperInvariant() +
        $Value.Substring(1)
}

function Format-EnumValue {
    param(
        [AllowNull()]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Type,

        [Parameter(Mandatory = $true)]
        [string]$Context,

        [switch]$Nullable
    )

    if ($null -eq $Value) {
        if (-not $Nullable) {
            throw "$Context cannot be null."
        }
        return 'null'
    }
    $text = Assert-EnumValue `
        -Value $Value `
        -Type $Type `
        -Context $Context
    return "$Type.$text"
}

function Format-EnumArray {
    param(
        [AllowNull()]
        [object]$Values,

        [Parameter(Mandatory = $true)]
        [string]$Type,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $items = @($Values)
    return '[' + (($items | ForEach-Object {
        Format-EnumValue `
            -Value $_ `
            -Type $Type `
            -Context $Context
    }) -join ', ') + ']'
}

function Format-StringArray {
    param(
        [AllowNull()]
        [object]$Values,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $items = @($Values)
    return '[' + (($items | ForEach-Object {
        ConvertTo-CSharpString (
            Assert-Text -Value $_ -Context $Context)
    }) -join ', ') + ']'
}

function Get-EvidenceVariable {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Reference,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $id = Assert-Text -Value $Reference -Context $Context
    if (-not $evidenceVariables.ContainsKey($id)) {
        throw "$Context references unknown evidence '$id'."
    }
    return $evidenceVariables[$id]
}

function Format-Term {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Term,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $kind = Assert-Text `
        -Value (Get-RequiredProperty $Term 'kind' $Context) `
        -Context "$Context.kind"
    $type = Format-EnumValue `
        -Value (Get-RequiredProperty $Term 'type' $Context) `
        -Type 'IrTypeKind' `
        -Context "$Context.type"
    switch -CaseSensitive ($kind) {
        'variable' {
            $role = Format-EnumValue `
                -Value (Get-RequiredProperty $Term 'role' $Context) `
                -Type 'SpecVariableRole' `
                -Context "$Context.role"
            $ordinal = Assert-JsonInt32 `
                -Value (Get-RequiredProperty $Term 'ordinal' $Context) `
                -Context "$Context.ordinal"
            return "new SpecVariableDeclaration($role, $ordinal, $type)"
        }
        'boolean' {
            $value = Assert-Boolean `
                -Value (Get-RequiredProperty $Term 'value' $Context) `
                -Context "$Context.value" `
                -TypeDescription 'a JSON boolean'
            $literal = if ($value) { 'true' } else { 'false' }
            if ($type -ne 'IrTypeKind.Boolean') {
                throw "$Context boolean type must be Boolean."
            }
            return "new SpecBooleanDeclaration($literal)"
        }
        'integer' {
            $value = Assert-JsonInt64 `
                -Value (Get-RequiredProperty $Term 'value' $Context) `
                -Context "$Context.value"
            if ($type -ne 'IrTypeKind.Integer') {
                throw "$Context integer type must be Integer."
            }
            return (
                'new SpecIntegerDeclaration(' +
                $value.ToString([Globalization.CultureInfo]::InvariantCulture) +
                ')')
        }
        'string' {
            $value = Assert-Text `
                -Value (Get-RequiredProperty $Term 'value' $Context) `
                -Context "$Context.value" `
                -AllowEmpty
            if ($type -ne 'IrTypeKind.String') {
                throw "$Context string type must be String."
            }
            return 'new SpecStringDeclaration(' +
                (ConvertTo-CSharpString $value) + ')'
        }
        'null' {
            return "new SpecNullDeclaration($type)"
        }
        'unary' {
            $operator = Format-EnumValue `
                -Value (Get-RequiredProperty $Term 'operator' $Context) `
                -Type 'IrUnaryOperator' `
                -Context "$Context.operator"
            $operand = Format-Term `
                -Term (Get-RequiredProperty $Term 'operand' $Context) `
                -Context "$Context.operand"
            return "new SpecUnaryDeclaration($operator, $operand, $type)"
        }
        'binary' {
            $operator = Format-EnumValue `
                -Value (Get-RequiredProperty $Term 'operator' $Context) `
                -Type 'IrBinaryOperator' `
                -Context "$Context.operator"
            $left = Format-Term `
                -Term (Get-RequiredProperty $Term 'left' $Context) `
                -Context "$Context.left"
            $right = Format-Term `
                -Term (Get-RequiredProperty $Term 'right' $Context) `
                -Context "$Context.right"
            return (
                "new SpecBinaryDeclaration($operator, $left, " +
                "$right, $type)")
        }
        'conditional' {
            $condition = Format-Term `
                -Term (Get-RequiredProperty $Term 'condition' $Context) `
                -Context "$Context.condition"
            $whenTrue = Format-Term `
                -Term (Get-RequiredProperty $Term 'whenTrue' $Context) `
                -Context "$Context.whenTrue"
            $whenFalse = Format-Term `
                -Term (Get-RequiredProperty $Term 'whenFalse' $Context) `
                -Context "$Context.whenFalse"
            return (
                "new SpecConditionalDeclaration($condition, $whenTrue, " +
                "$whenFalse, $type)")
        }
        'length' {
            if ($type -ne 'IrTypeKind.Integer') {
                throw "$Context length type must be Integer."
            }
            $value = Format-Term `
                -Term (Get-RequiredProperty $Term 'value' $Context) `
                -Context "$Context.value"
            return "new SpecLengthDeclaration($value)"
        }
        default {
            throw "$Context contains unsupported term kind '$kind'."
        }
    }
}

function Format-MarkdownText {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    return $Value.Replace('|', '\|').Replace('`', '&#96;').
        Replace("`r", ' ').Replace("`n", ' ')
}

function Format-EvidenceDocumentation {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Reference
    )

    $id = [string]$Reference
    $evidence = $evidenceById[$id]
    return "$($evidence.kind):$($evidence.source)"
}

function Format-TermDocumentation {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Term
    )

    switch -CaseSensitive ([string]$Term.kind) {
        'variable' {
            return "$($Term.role)[$($Term.ordinal)]:$($Term.type)"
        }
        'boolean' {
            return "$($Term.value):Boolean"
        }
        'integer' {
            return "$($Term.value):Integer"
        }
        'string' {
            return "`"$($Term.value)`":String"
        }
        'null' {
            return "null:$($Term.type)"
        }
        'unary' {
            return (
                "$($Term.operator)(" +
                (Format-TermDocumentation $Term.operand) + ")")
        }
        'binary' {
            return (
                '(' + (Format-TermDocumentation $Term.left) + ' ' +
                $Term.operator + ' ' +
                (Format-TermDocumentation $Term.right) + ')')
        }
        'conditional' {
            return (
                'if ' + (Format-TermDocumentation $Term.condition) +
                ' then ' + (Format-TermDocumentation $Term.whenTrue) +
                ' else ' + (Format-TermDocumentation $Term.whenFalse))
        }
        'length' {
            return 'length(' +
                (Format-TermDocumentation $Term.value) + ')'
        }
        default {
            throw "Unsupported documentation term kind '$($Term.kind)'."
        }
    }
}

$catalogText = [IO.File]::ReadAllText($CatalogPath)
$catalog = $catalogText | ConvertFrom-Json -Depth 100
$schemaVersion = Assert-JsonInt32 `
    -Value (Get-RequiredProperty $catalog 'schemaVersion' 'catalog') `
    -Context 'schemaVersion'
if ($catalog.schema -ne 'SharpProof.ApiSpecCatalog' -or
    $schemaVersion -ne 1) {
    throw 'The API-spec catalog schema must be SharpProof.ApiSpecCatalog v1.'
}
$tableIdentity = Assert-Text -Value $catalog.tableIdentity -Context 'tableIdentity'
$tableVersion = Assert-Text -Value $catalog.tableVersion -Context 'tableVersion'

$assemblySets = @{}
$assemblyFieldNames = @{}
foreach ($set in @($catalog.assemblySets)) {
    $id = Assert-Text -Value $set.id -Context 'assemblySets[].id'
    if ($assemblySets.ContainsKey($id)) {
        throw "Duplicate assembly set '$id'."
    }
    $identities = [Collections.Generic.List[object]]::new()
    $explicitIdentities = Get-OptionalProperty $set 'identities'
    if ($null -eq $explicitIdentities) {
        throw "Assembly set '$id' requires explicit identities."
    }
    foreach ($identity in @($explicitIdentities)) {
        $name = Assert-Text `
            -Value $identity.name `
            -Context "assemblySets[$id].identities[].name"
        $token = Assert-Text `
            -Value $identity.publicKeyToken `
            -Context "assemblySets[$id].identities[].publicKeyToken" `
            -AllowEmpty
        $familyValues = @($identity.referenceFamilies)
        if ($familyValues.Count -eq 0) {
            throw "Assembly identity '$name' requires a reference family."
        }
        foreach ($familyValue in $familyValues) {
            $family = Assert-EnumValue `
                -Value $familyValue `
                -Type 'ApiSpecReferenceFamily' `
                -Context "assemblySets[$id].identities[].referenceFamilies[]"
            $identities.Add([pscustomobject]@{
                name = $name
                publicKeyToken = $token
                referenceFamily = $family
            })
        }
    }
    if ($identities.Count -eq 0) {
        throw "Assembly set '$id' cannot be empty."
    }
    $keys = @($identities | ForEach-Object {
        $_.name + '|' + $_.publicKeyToken.ToLowerInvariant() +
            '|' + $_.referenceFamily
    })
    if (@($keys | Sort-Object -Unique).Count -ne $keys.Count) {
        throw "Assembly set '$id' contains duplicate identities."
    }
    $assemblySets[$id] = @($identities)
    $assemblyFieldNames[$id] =
        'assemblySet' +
        (ConvertTo-PascalIdentifier $id "Assembly set id")
}

$evidenceById = @{}
$evidenceVariables = @{}
foreach ($evidence in @($catalog.evidence)) {
    $id = Assert-Text -Value $evidence.id -Context 'evidence[].id'
    if ($evidenceById.ContainsKey($id)) {
        throw "Duplicate evidence '$id'."
    }
    [void](Assert-EnumValue `
        -Value $evidence.kind `
        -Type 'SpecEvidenceKind' `
        -Context "evidence[$id].kind")
    [void](Assert-Text `
        -Value $evidence.source `
        -Context "evidence[$id].source")
    $evidenceById[$id] = $evidence
    $evidenceVariables[$id] =
        'evidence' +
        (ConvertTo-PascalIdentifier $id "Evidence id")
}

$declarations = @(
    $catalog.declarations |
        Sort-Object {
            [string]$_.target.witnessIdentifier
        })
$witnesses = @($declarations | ForEach-Object {
    Assert-Text `
        -Value $_.target.witnessIdentifier `
        -Context 'declarations[].target.witnessIdentifier'
})
if (@($witnesses | Sort-Object -Unique).Count -ne $witnesses.Count) {
    throw 'API-spec witness identifiers must be unique.'
}
foreach ($declaration in $declarations) {
    $witness = [string]$declaration.target.witnessIdentifier
    Assert-ExactProperties `
        -Object $declaration `
        -Names @('target', 'facets', 'postconditions') `
        -Context "declarations[$witness]"
}

$source = New-SharpProofGeneratedHeader `
    -Generator 'Generate-ApiSpecCatalog.ps1' `
    -Source 'SharpProof.Specs/DefaultApiSpecCatalog.json.' `
    -Nullable
$source.Add('namespace SharpProof.Specs;')
$source.Add('')
$source.Add('public enum SpecEvidenceKind {')
$source.Add('    Documented,')
$source.Add('    Observed')
$source.Add('}')
$source.Add('')
$source.Add('[Flags]')
$source.Add('public enum SpecEffect {')
$source.Add('    None = 0,')
$source.Add('    Unknown = 1 << 0,')
$source.Add('    ReadsReceiverState = 1 << 1,')
$source.Add('    ReadsArgumentState = 1 << 2,')
$source.Add('    WritesReceiverState = 1 << 3,')
$source.Add('    WritesArgumentState = 1 << 4,')
$source.Add('    ReadsAmbientState = 1 << 5,')
$source.Add('    WritesAmbientState = 1 << 6,')
$source.Add('    InputOutput = 1 << 7,')
$source.Add('    Synchronization = 1 << 8,')
$source.Add('    NativeCode = 1 << 9,')
$source.Add('    Reflection = 1 << 10,')
$source.Add('    Nondeterminism = 1 << 11')
$source.Add('}')
$source.Add('')
$source.Add('public enum SpecAllocationBehavior {')
$source.Add('    None,')
$source.Add('    MayAllocate,')
$source.Add('    Unknown')
$source.Add('}')
$source.Add('')
$source.Add('public enum SpecThrowBehavior {')
$source.Add('    DoesNotThrow,')
$source.Add('    MayThrow,')
$source.Add('    Unknown')
$source.Add('}')
$source.Add('')
$source.Add('public enum SpecTerminationBehavior {')
$source.Add('    Terminates,')
$source.Add('    Unknown')
$source.Add('}')
$source.Add('')
$source.Add('public enum SpecNullness {')
$source.Add('    NotApplicable,')
$source.Add('    NonNull,')
$source.Add('    MaybeNull,')
$source.Add('    Null,')
$source.Add('    Unknown')
$source.Add('}')
$source.Add('')
$source.Add('public enum SpecCardinality {')
$source.Add('    NotApplicable,')
$source.Add('    Empty,')
$source.Add('    NonEmpty,')
$source.Add('    Exact,')
$source.Add('    Unknown')
$source.Add('}')
$source.Add('')
$source.Add('public enum SpecTargetMemberKind {')
$source.Add('    Constructor,')
$source.Add('    Method,')
$source.Add('    PropertyGet')
$source.Add('}')
$source.Add('')
$source.Add('public enum SpecVariableRole {')
$source.Add('    Receiver,')
$source.Add('    Parameter,')
$source.Add('    Result')
$source.Add('}')
$source.Add('')
$source.Add('public sealed record SpecEvidence(SpecEvidenceKind Kind, string Source);')
$source.Add('public sealed record SpecEffectFacet(SpecEffect Effects, SpecEvidence Evidence);')
$source.Add('public sealed record SpecAllocationFacet(SpecAllocationBehavior Behavior, SpecEvidence Evidence);')
$source.Add('public sealed record SpecThrowFacet(')
$source.Add('    SpecThrowBehavior Behavior, ImmutableArray<string> ExceptionMetadataNames, SpecEvidence Evidence);')
$source.Add('public sealed record SpecTerminationFacet(')
$source.Add('    SpecTerminationBehavior Behavior, SpecEvidence Evidence);')
$source.Add('public sealed record SpecNullnessFacet(SpecNullness Result, SpecEvidence Evidence);')
$source.Add('public sealed record SpecCardinalityFacet(')
$source.Add('    SpecCardinality Result, int? ExactCount, SpecEvidence Evidence);')
$source.Add('')
$source.Add('public enum ApiSpecReferenceFamily {')
$source.Add('    Unspecified,')
$source.Add('    MicrosoftNetCoreReferencePack,')
$source.Add('    NetStandardReferencePack,')
$source.Add('    NetFrameworkReferenceAssemblies,')
$source.Add('    MicrosoftNetCoreRuntime,')
$source.Add('    SharpProofPackage')
$source.Add('}')
$source.Add('')
$source.Add('public sealed record ApiSpecFacets(')
$source.Add('    SpecEffectFacet Effects, SpecAllocationFacet Allocation,')
$source.Add('    SpecThrowFacet Throws, SpecNullnessFacet Nullness,')
$source.Add('    SpecCardinalityFacet Cardinality,')
$source.Add('    SpecTerminationFacet? Termination = null);')
$source.Add('')
$source.Add('public sealed record ApiSpecAssemblyIdentity(')
$source.Add('    string Name,')
$source.Add('    string PublicKeyToken,')
$source.Add('    ApiSpecReferenceFamily ReferenceFamily =')
$source.Add('        ApiSpecReferenceFamily.Unspecified);')
$source.Add('')
$source.Add('public sealed record ApiSpecTarget(')
$source.Add('    string WitnessIdentifier, string DocumentationCommentId,')
$source.Add('    string ContainingTypeMetadataName, SpecTargetMemberKind MemberKind,')
$source.Add('    string MemberName, bool IsStatic, int GenericArity,')
$source.Add('    IrTypeKind? ReceiverType, ImmutableArray<IrTypeKind> ParameterTypes,')
$source.Add('    IrTypeKind? ResultType,')
$source.Add('    ImmutableArray<ApiSpecAssemblyIdentity> ApprovedAssemblies);')
$source.Add('')
$source.Add('public abstract record SpecTermDeclaration(IrTypeKind Type);')
$source.Add('')
$source.Add('public sealed record SpecVariableDeclaration(SpecVariableRole Role, int Ordinal, IrTypeKind Type)')
$source.Add('    : SpecTermDeclaration(Type);')
$source.Add('')
$source.Add('public sealed record SpecBooleanDeclaration(bool Value)')
$source.Add('    : SpecTermDeclaration(IrTypeKind.Boolean);')
$source.Add('')
$source.Add('public sealed record SpecIntegerDeclaration(long Value)')
$source.Add('    : SpecTermDeclaration(IrTypeKind.Integer);')
$source.Add('')
$source.Add('public sealed record SpecStringDeclaration(string Value)')
$source.Add('    : SpecTermDeclaration(IrTypeKind.String);')
$source.Add('')
$source.Add('public sealed record SpecNullDeclaration(IrTypeKind Type)')
$source.Add('    : SpecTermDeclaration(Type);')
$source.Add('')
$source.Add('public sealed record SpecUnaryDeclaration(')
$source.Add('    IrUnaryOperator Operator, SpecTermDeclaration Operand, IrTypeKind Type)')
$source.Add('    : SpecTermDeclaration(Type);')
$source.Add('')
$source.Add('public sealed record SpecBinaryDeclaration(')
$source.Add('    IrBinaryOperator Operator, SpecTermDeclaration Left,')
$source.Add('    SpecTermDeclaration Right, IrTypeKind Type)')
$source.Add('    : SpecTermDeclaration(Type);')
$source.Add('')
$source.Add('public sealed record SpecConditionalDeclaration(')
$source.Add('    SpecTermDeclaration Condition, SpecTermDeclaration WhenTrue,')
$source.Add('    SpecTermDeclaration WhenFalse, IrTypeKind Type)')
$source.Add('    : SpecTermDeclaration(Type);')
$source.Add('')
$source.Add('public sealed record SpecLengthDeclaration(SpecTermDeclaration Value)')
$source.Add('    : SpecTermDeclaration(IrTypeKind.Integer);')
$source.Add('')
$source.Add('public sealed record SpecPostconditionDeclaration(SpecTermDeclaration Condition, SpecEvidence Evidence);')
$source.Add('')
$source.Add('public sealed record ApiSpecDeclaration(')
$source.Add('    ApiSpecTarget Target, ApiSpecFacets Facets,')
$source.Add('    ImmutableArray<SpecPostconditionDeclaration> Postconditions);')
$source.Add('')
$source.Add('public sealed record SpecVariableInfo(')
$source.Add('    SpecVarId Id, SpecVariableRole Role, int Ordinal, IrTypeKind Type);')
$source.Add('')
$source.Add('public sealed record SpecPostcondition(')
$source.Add('    SpecTermDeclaration Condition, SpecEvidence Evidence);')
$source.Add('')
$source.Add('public sealed class ApiSpecTemplate')
$source.Add('{')
$source.Add('    internal ApiSpecTemplate(')
$source.Add('        SpecId id, ApiSpecTarget target, ApiSpecFacets facets,')
$source.Add('        ImmutableArray<SpecVariableInfo> variables, SpecVarId? receiver,')
$source.Add('        ImmutableArray<SpecVarId> parameters, SpecVarId? result,')
$source.Add('        ImmutableArray<SpecPostcondition> postconditions)')
$source.Add('    {')
$source.Add('        (Id, Target, Facets, Variables) = (id, target, facets, variables);')
$source.Add('        (Receiver, Parameters, Result, Postconditions) =')
$source.Add('            (receiver, parameters, result, postconditions);')
$source.Add('    }')
$source.Add('')
$source.Add('    public SpecId Id { get; }')
$source.Add('    public ApiSpecTarget Target { get; }')
$source.Add('    public ApiSpecFacets Facets { get; }')
$source.Add('    public ImmutableArray<SpecVariableInfo> Variables { get; }')
$source.Add('    public SpecVarId? Receiver { get; }')
$source.Add('    public ImmutableArray<SpecVarId> Parameters { get; }')
$source.Add('    public SpecVarId? Result { get; }')
$source.Add('    public ImmutableArray<SpecPostcondition> Postconditions { get; }')
$source.Add('}')
$source.Add('')
$source.Add('public sealed partial class ApiSpecTable {')
$source.Add(
    '    public const string DefaultTableIdentity = ' +
    (ConvertTo-CSharpString $tableIdentity) + ';')
$source.Add(
    '    public const string DefaultTableVersion = ' +
    (ConvertTo-CSharpString $tableVersion) + ';')
$source.Add('')
$source.Add(
    '    private static ImmutableArray<ApiSpecDeclaration> ' +
    'CreateDefaultDeclarations() {')
foreach ($id in @($assemblySets.Keys | Sort-Object)) {
    $source.Add(
        '        ImmutableArray<ApiSpecAssemblyIdentity> ' +
        "$($assemblyFieldNames[$id]) = [")
    foreach ($identity in @($assemblySets[$id])) {
        $source.Add(
            '            new(' +
            (ConvertTo-CSharpString $identity.name) + ', ' +
            (ConvertTo-CSharpString $identity.publicKeyToken) + ', ' +
            'ApiSpecReferenceFamily.' + $identity.referenceFamily + '),')
    }
    $source.Add('        ];')
}
foreach ($id in @($evidenceById.Keys | Sort-Object)) {
    $evidence = $evidenceById[$id]
    $kind = Format-EnumValue `
        -Value $evidence.kind `
        -Type 'SpecEvidenceKind' `
        -Context "evidence[$id].kind"
    $source.Add(
        "        var $($evidenceVariables[$id]) = " +
        "new SpecEvidence($kind, " +
        (ConvertTo-CSharpString $evidence.source) + ');')
}
$source.Add('        return [')
foreach ($declaration in $declarations) {
    $target = $declaration.target
    $witness = [string]$target.witnessIdentifier
    $context = "declarations[$witness]"
    $assemblySetId = Assert-Text `
        -Value $target.approvedAssemblySet `
        -Context "$context.target.approvedAssemblySet"
    if (-not $assemblyFieldNames.ContainsKey($assemblySetId)) {
        throw (
            "$context references unknown assembly set " +
            "'$assemblySetId'.")
    }
    $memberKind = Format-EnumValue `
        -Value $target.memberKind `
        -Type 'SpecTargetMemberKind' `
        -Context "$context.target.memberKind"
    $receiverType = Format-EnumValue `
        -Value $target.receiverType `
        -Type 'IrTypeKind' `
        -Context "$context.target.receiverType" `
        -Nullable
    $parameterTypes = Format-EnumArray `
        -Values $target.parameterTypes `
        -Type 'IrTypeKind' `
        -Context "$context.target.parameterTypes"
    $resultType = Format-EnumValue `
        -Value $target.resultType `
        -Type 'IrTypeKind' `
        -Context "$context.target.resultType" `
        -Nullable
    $isStaticValue = Assert-Boolean `
        -Value (Get-RequiredProperty $target 'isStatic' "$context.target") `
        -Context "$context.target.isStatic" `
        -TypeDescription 'a JSON boolean'
    $isStatic = if ($isStaticValue) {
        'true'
    }
    else {
        'false'
    }
    $genericArity = Assert-JsonInt32 `
        -Value (Get-RequiredProperty $target 'genericArity' "$context.target") `
        -Context "$context.target.genericArity"
    if ($genericArity -lt 0) {
        throw "$context.target.genericArity cannot be negative."
    }
    $facets = $declaration.facets
    $effectValues = @($facets.effects.values)
    if ($effectValues.Count -eq 0) {
        throw "$context.facets.effects.values cannot be empty."
    }
    $effectExpression = ($effectValues | ForEach-Object {
        Format-EnumValue `
            -Value $_ `
            -Type 'SpecEffect' `
            -Context "$context.facets.effects.values"
    }) -join ' | '
    $effectEvidence = Get-EvidenceVariable `
        -Reference $facets.effects.evidence `
        -Context "$context.facets.effects.evidence"
    $allocation = Format-EnumValue `
        -Value $facets.allocation.behavior `
        -Type 'SpecAllocationBehavior' `
        -Context "$context.facets.allocation.behavior"
    $allocationEvidence = Get-EvidenceVariable `
        -Reference $facets.allocation.evidence `
        -Context "$context.facets.allocation.evidence"
    $throws = Format-EnumValue `
        -Value $facets.throws.behavior `
        -Type 'SpecThrowBehavior' `
        -Context "$context.facets.throws.behavior"
    $exceptionNames = Format-StringArray `
        -Values $facets.throws.exceptionMetadataNames `
        -Context "$context.facets.throws.exceptionMetadataNames"
    $throwEvidence = Get-EvidenceVariable `
        -Reference $facets.throws.evidence `
        -Context "$context.facets.throws.evidence"
    $nullness = Format-EnumValue `
        -Value $facets.nullness.result `
        -Type 'SpecNullness' `
        -Context "$context.facets.nullness.result"
    $nullnessEvidence = Get-EvidenceVariable `
        -Reference $facets.nullness.evidence `
        -Context "$context.facets.nullness.evidence"
    $cardinality = Format-EnumValue `
        -Value $facets.cardinality.result `
        -Type 'SpecCardinality' `
        -Context "$context.facets.cardinality.result"
    $exactCountValue = $facets.cardinality.exactCount
    $exactCount = if ($null -eq $exactCountValue) {
        'null'
    }
    else {
        (Assert-JsonInt32 `
            -Value $exactCountValue `
            -Context "$context.facets.cardinality.exactCount").ToString(
            [Globalization.CultureInfo]::InvariantCulture)
    }
    $cardinalityEvidence = Get-EvidenceVariable `
        -Reference $facets.cardinality.evidence `
        -Context "$context.facets.cardinality.evidence"
    $terminationFacet = Get-OptionalProperty $facets 'termination'
    $termination = if ($null -eq $terminationFacet) {
        'null'
    }
    else {
        $terminationBehavior = Format-EnumValue `
            -Value $terminationFacet.behavior `
            -Type 'SpecTerminationBehavior' `
            -Context "$context.facets.termination.behavior"
        $terminationEvidence = Get-EvidenceVariable `
            -Reference $terminationFacet.evidence `
            -Context "$context.facets.termination.evidence"
        "new SpecTerminationFacet($terminationBehavior, " +
            "$terminationEvidence)"
    }

    $source.Add('            new ApiSpecDeclaration(')
    $source.Add('                new ApiSpecTarget(')
    $source.Add(
        '                    ' +
        (ConvertTo-CSharpString $target.witnessIdentifier) + ',')
    $source.Add(
        '                    ' +
        (ConvertTo-CSharpString $target.documentationCommentId) + ',')
    $source.Add(
        '                    ' +
        (ConvertTo-CSharpString $target.containingTypeMetadataName) + ',')
    $source.Add(
        "                    $memberKind, " +
        (ConvertTo-CSharpString $target.memberName) + ',')
    $source.Add(
        "                    $isStatic, $genericArity, " +
        "$receiverType, $parameterTypes, $resultType,")
    $source.Add(
        "                    $($assemblyFieldNames[$assemblySetId])),")
    $source.Add('                new ApiSpecFacets(')
    $source.Add(
        '                    new SpecEffectFacet(' +
        "$effectExpression, $effectEvidence),")
    $source.Add(
        '                    new SpecAllocationFacet(' +
        "$allocation, $allocationEvidence),")
    $source.Add(
        '                    new SpecThrowFacet(' +
        "$throws, $exceptionNames, $throwEvidence),")
    $source.Add(
        '                    new SpecNullnessFacet(' +
        "$nullness, $nullnessEvidence),")
    $source.Add(
        '                    new SpecCardinalityFacet(' +
        "$cardinality, $exactCount, $cardinalityEvidence),")
    $source.Add("                    $termination),")

    $postconditions = @(Get-RequiredArrayProperty `
        -Object $declaration `
        -Name 'postconditions' `
        -Context $context)
    if ($postconditions.Count -eq 0) {
        $source.Add('                []),')
    }
    else {
        $source.Add('                [')
        for ($index = 0; $index -lt $postconditions.Count; $index++) {
            $postcondition = $postconditions[$index]
            $term = Format-Term `
                -Term $postcondition.condition `
                -Context "$context.postconditions[$index].condition"
            $postEvidence = Get-EvidenceVariable `
                -Reference $postcondition.evidence `
                -Context "$context.postconditions[$index].evidence"
            $source.Add(
                '                    new SpecPostconditionDeclaration(')
            $source.Add("                        $term,")
            $source.Add("                        $postEvidence),")
        }
        $source.Add('                ]),')
    }
}
$source.Add('        ];')
$source.Add('    }')
$source.Add('}')
$sourceText = $source -join "`n"

$documentation = [Collections.Generic.List[string]]::new()
$documentation.Add('<!-- <auto-generated> -->')
$documentation.Add(
    '<!-- Generated by scripts/Generate-ApiSpecCatalog.ps1 from ' +
    'SharpProof.Specs/DefaultApiSpecCatalog.json. -->')
$documentation.Add('<!-- Do not edit this file directly. -->')
$documentation.Add('<!-- </auto-generated> -->')
$documentation.Add('')
$documentation.Add('# Default API specification catalog')
$documentation.Add('')
$documentation.Add(
    'This reference is generated from the checked-in declarative catalog. ' +
    'The JSON file is the review source; SharpProof does not parse it at runtime.')
$documentation.Add('')
$documentation.Add("- Table identity: ``$tableIdentity``")
$documentation.Add("- Table version: ``$tableVersion``")
$documentation.Add("- Witnesses: $($declarations.Count)")
$documentation.Add('')
$documentation.Add('## Approved assembly sets')
$documentation.Add('')
$documentation.Add('| Set | Approved identities |')
$documentation.Add('| --- | --- |')
foreach ($id in @($assemblySets.Keys | Sort-Object)) {
    $identities = @($assemblySets[$id] | ForEach-Object {
        $token = if ([string]::IsNullOrEmpty($_.publicKeyToken)) {
            'unsigned'
        }
        else {
            'PublicKeyToken=' + $_.publicKeyToken
        }
        (Format-MarkdownText $_.name) + ' (' + $token + '; ' +
            $_.referenceFamily + ')'
    })
    $documentation.Add(
        '| `' + (Format-MarkdownText $id) + '` | ' +
        ($identities -join '<br>') + ' |')
}
$documentation.Add('')
$documentation.Add('## Evidence witnesses')
$documentation.Add('')
$documentation.Add('| Id | Kind | Source |')
$documentation.Add('| --- | --- | --- |')
foreach ($id in @($evidenceById.Keys | Sort-Object)) {
    $evidence = $evidenceById[$id]
    $documentation.Add(
        '| `' + (Format-MarkdownText $id) + '` | ' +
        (Format-MarkdownText $evidence.kind) + ' | `' +
        (Format-MarkdownText $evidence.source) + '` |')
}
$documentation.Add('')
$documentation.Add('## Declarations')
$documentation.Add('')
$documentation.Add(
    '| Witness | Target | Shape | Facets and evidence | Postconditions |')
$documentation.Add('| --- | --- | --- | --- | --- |')
foreach ($declaration in $declarations) {
    $target = $declaration.target
    $facets = $declaration.facets
    $parameters = @($target.parameterTypes) -join ', '
    $receiver = if ($null -eq $target.receiverType) {
        '-'
    }
    else {
        [string]$target.receiverType
    }
    $result = if ($null -eq $target.resultType) {
        '-'
    }
    else {
        [string]$target.resultType
    }
    $shape = (
        "$($target.memberKind); static=$($target.isStatic); " +
        "generic=$($target.genericArity); receiver=$receiver; " +
        "parameters=[$parameters]; result=$result; " +
        "assemblies=$($target.approvedAssemblySet)")
    $effectValues = @($facets.effects.values) -join '+'
    $exceptions = @($facets.throws.exceptionMetadataNames) -join ','
    if ([string]::IsNullOrEmpty($exceptions)) {
        $exceptions = '-'
    }
    $exact = if ($null -eq $facets.cardinality.exactCount) {
        '-'
    }
    else {
        [string]$facets.cardinality.exactCount
    }
    $terminationFacet = Get-OptionalProperty $facets 'termination'
    $terminationText = if ($null -eq $terminationFacet) {
        ''
    }
    else {
        '; termination=' + $terminationFacet.behavior + ' [' +
            (Format-EvidenceDocumentation $terminationFacet.evidence) +
            ']'
    }
    $facetText = (
        "effects=$effectValues [" +
        (Format-EvidenceDocumentation $facets.effects.evidence) +
        "]; allocation=$($facets.allocation.behavior) [" +
        (Format-EvidenceDocumentation $facets.allocation.evidence) +
        "]; throws=$($facets.throws.behavior)($exceptions) [" +
        (Format-EvidenceDocumentation $facets.throws.evidence) +
        "]; nullness=$($facets.nullness.result) [" +
        (Format-EvidenceDocumentation $facets.nullness.evidence) +
        "]; cardinality=$($facets.cardinality.result)($exact) [" +
        (Format-EvidenceDocumentation $facets.cardinality.evidence) +
        ']' + $terminationText)
    $postconditions = @(Get-RequiredArrayProperty `
        -Object $declaration `
        -Name 'postconditions' `
        -Context "declarations[$($target.witnessIdentifier)]")
    $postconditionText = if ($postconditions.Count -eq 0) {
        '-'
    }
    else {
        ($postconditions | ForEach-Object {
            (Format-TermDocumentation $_.condition) + ' [' +
            (Format-EvidenceDocumentation $_.evidence) + ']'
        }) -join '<br>'
    }
    $documentation.Add(
        '| `' +
        (Format-MarkdownText $target.witnessIdentifier) +
        '` | `' +
        (Format-MarkdownText $target.documentationCommentId) +
        '` | ' +
        (Format-MarkdownText $shape) +
        ' | ' +
        (Format-MarkdownText $facetText) +
        ' | ' +
        (Format-MarkdownText $postconditionText) +
        ' |')
}
$documentationText = $documentation -join "`n"

[IO.Directory]::CreateDirectory(
    [IO.Path]::GetDirectoryName($SourceOutputPath)) | Out-Null
[IO.Directory]::CreateDirectory(
    [IO.Path]::GetDirectoryName($DocumentationOutputPath)) | Out-Null
$generatorCommand = '.\scripts\Generate-ApiSpecCatalog.ps1'
Update-SharpProofGeneratedFile `
    -Path $SourceOutputPath `
    -Content $sourceText `
    -DisplayPath $SourceOutputPath `
    -GeneratorCommand $generatorCommand `
    -Verify:$Verify
Update-SharpProofGeneratedFile `
    -Path $DocumentationOutputPath `
    -Content $documentationText `
    -DisplayPath $DocumentationOutputPath `
    -GeneratorCommand $generatorCommand `
    -Verify:$Verify

& (Join-Path $repositoryRoot 'SharpProof.Specs.Test\Generate-ApiSpecRuntimeWitnesses.ps1') `
    -CatalogPath $CatalogPath `
    -OutputPath $RuntimeWitnessOutputPath `
    -Verify:$Verify
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic API-spec catalog source and documentation."
