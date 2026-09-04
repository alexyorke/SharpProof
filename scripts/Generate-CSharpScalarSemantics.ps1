[CmdletBinding()]
param(
    [Parameter()]
    [string]$CatalogPath,

    [Parameter()]
    [string]$OutputPath,

    [Parameter()]
    [string]$IrOutputPath,

    [Parameter()]
    [Alias('Check')]
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$CatalogPath = Resolve-SharpProofPath $CatalogPath (
    Join-Path $repositoryRoot 'SharpProof.Frontend\CSharpScalarSemantics.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Frontend\CSharpScalarSemantics.generated.cs')
$IrOutputPath = Resolve-SharpProofPath $IrOutputPath (
    Join-Path $repositoryRoot 'SharpProof.Ir\IrOperatorCatalog.generated.cs')
if (-not [IO.File]::Exists($CatalogPath)) {
    throw "C# scalar-semantics catalog not found: $CatalogPath"
}

function Assert-OptionalEnumName {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string[]]$Allowed,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($null -eq $Value) {
        return $null
    }
    return Assert-EnumName `
        -Value $Value `
        -Allowed $Allowed `
        -Context $Context
}

function ConvertTo-Long {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $result = 0L
    if ($Value -isnot [string] -or
        -not [long]::TryParse(
            [string]$Value,
            [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$result)) {
        throw "$Context must be an invariant Int64 string."
    }
    return $result
}

function ConvertTo-CSharpLongLiteral {
    param(
        [Parameter(Mandatory = $true)]
        [long]$Value
    )

    if ($Value -eq [long]::MinValue) {
        return 'long.MinValue'
    }
    if ($Value -eq [long]::MaxValue) {
        return 'long.MaxValue'
    }
    return $Value.ToString(
        [Globalization.CultureInfo]::InvariantCulture) + 'L'
}

function Add-Lines {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [Collections.Generic.List[string]]$Lines,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Values
    )

    foreach ($value in $Values) {
        $Lines.Add($value)
    }
}

$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
Assert-Properties `
    -Value $catalog `
    -Allowed @(
        'schemaVersion',
        'integerConversionPolicy',
        'integers',
        'binaryOperators',
        'unaryOperators',
        'specialBinaryOperators',
        'irTypeKinds',
        'irBuiltInTypes',
        'nullableIrTypeKinds',
        'builtInSpecialTypes',
        'irUnaryOperators',
        'irBinaryOperators',
        'irOpaquePurities',
        'builtInEquality') `
    -Context 'catalog'
if ([int]$catalog.schemaVersion -ne 2) {
    throw "Unsupported scalar-semantics schema '$($catalog.schemaVersion)'."
}
if ([string]$catalog.integerConversionPolicy -ne 'range-contained') {
    throw "integerConversionPolicy must be 'range-contained'."
}

$specialTypes = @(
    'System_SByte',
    'System_Byte',
    'System_Int16',
    'System_UInt16',
    'System_Char',
    'System_Int32',
    'System_UInt32',
    'System_Int64',
    'System_String',
    'System_Boolean')
$binaryKinds = @(
    'Add',
    'Subtract',
    'Multiply',
    'Divide',
    'Remainder',
    'ConditionalAnd',
    'ConditionalOr',
    'Equals',
    'NotEquals',
    'LessThan',
    'LessThanOrEqual',
    'GreaterThan',
    'GreaterThanOrEqual')
$unaryKinds = @('Not', 'Plus', 'Minus')
$irTypeKinds = @(
    $catalog.irTypeKinds |
        ForEach-Object {
            Assert-CSharpIdentifier `
                -Value $_ `
                -Context 'irTypeKinds'
        })
if ($irTypeKinds.Count -eq 0 -or
    @($irTypeKinds | Select-Object -Unique).Count -ne
        $irTypeKinds.Count) {
    throw 'irTypeKinds must be non-empty and unique.'
}
$irBuiltInTypeRows = foreach ($type in @($catalog.irBuiltInTypes)) {
    Assert-Properties `
        -Value $type `
        -Allowed @('kind', 'factoryProperty') `
        -Context 'IR built-in type'
    [pscustomobject]@{
        Kind = Assert-EnumName `
            -Value $type.kind `
            -Allowed $irTypeKinds `
            -Context 'IR built-in type kind'
        FactoryProperty = Assert-CSharpIdentifier `
            -Value $type.factoryProperty `
            -Context 'IR built-in type factory property'
    }
}
if ($irBuiltInTypeRows.Count -eq 0 -or
    @($irBuiltInTypeRows.Kind | Select-Object -Unique).Count -ne
        $irBuiltInTypeRows.Count) {
    throw 'irBuiltInTypes must contain unique kinds.'
}
$nullableIrTypeKinds = @(
    $catalog.nullableIrTypeKinds |
        ForEach-Object {
            Assert-EnumName `
                -Value $_ `
                -Allowed $irTypeKinds `
                -Context 'nullableIrTypeKinds'
        })
if (@($nullableIrTypeKinds | Select-Object -Unique).Count -ne
    $nullableIrTypeKinds.Count) {
    throw 'nullableIrTypeKinds must not contain duplicates.'
}
$builtInSpecialTypeRows = foreach ($type in @($catalog.builtInSpecialTypes)) {
    Assert-Properties `
        -Value $type `
        -Allowed @('specialType', 'factoryProperty') `
        -Context 'built-in special type'
    [pscustomobject]@{
        SpecialType = Assert-EnumName `
            -Value $type.specialType `
            -Allowed @($specialTypes + 'System_Object') `
            -Context 'built-in special type name'
        FactoryProperty = Assert-CSharpIdentifier `
            -Value $type.factoryProperty `
            -Context 'built-in special type factory property'
    }
}
if ($builtInSpecialTypeRows.Count -eq 0 -or
    @($builtInSpecialTypeRows.SpecialType | Select-Object -Unique).Count -ne
        $builtInSpecialTypeRows.Count) {
    throw 'builtInSpecialTypes must contain unique special types.'
}
$irOpaquePurityRows = foreach ($purity in @($catalog.irOpaquePurities)) {
    Assert-Properties `
        -Value $purity `
        -Allowed @('purity', 'key') `
        -Context 'IR opaque purity'
    $name = Assert-CSharpIdentifier `
        -Value $purity.purity `
        -Context 'IR opaque purity name'
    $key = [int]$purity.key
    if ($key -lt 0) {
        throw "IR opaque purity '$name'.key must be nonnegative."
    }
    [pscustomobject]@{
        Purity = $name
        Key = $key
    }
}
if ($irOpaquePurityRows.Count -eq 0 -or
    @($irOpaquePurityRows.Purity | Select-Object -Unique).Count -ne
        $irOpaquePurityRows.Count -or
    @($irOpaquePurityRows.Key | Select-Object -Unique).Count -ne
        $irOpaquePurityRows.Count) {
    throw 'irOpaquePurities must contain unique purities and keys.'
}
$orderedIrOpaquePurityKeys = @($irOpaquePurityRows.Key | Sort-Object)
for ($index = 0; $index -lt $irOpaquePurityRows.Count; $index++) {
    if ($orderedIrOpaquePurityKeys[$index] -ne $index) {
        throw 'irOpaquePurities keys must be contiguous from zero.'
    }
}
$irUnaryOperators = @(
    $catalog.irUnaryOperators |
        ForEach-Object {
            Assert-CSharpIdentifier `
                -Value $_.operator `
                -Context 'IR unary operator name'
        })
if ($irUnaryOperators.Count -eq 0 -or
    @($irUnaryOperators | Select-Object -Unique).Count -ne
        $irUnaryOperators.Count) {
    throw 'irUnaryOperators must be non-empty and unique.'
}
$irBinaryOperators = @(
    $catalog.irBinaryOperators |
        ForEach-Object {
            Assert-CSharpIdentifier `
                -Value $_.operator `
                -Context 'IR binary operator name'
        })
if ($irBinaryOperators.Count -eq 0 -or
    @($irBinaryOperators | Select-Object -Unique).Count -ne
        $irBinaryOperators.Count) {
    throw 'irBinaryOperators must be non-empty and unique.'
}
$operatorTokens = @(
    '!',
    '-',
    '+',
    '*',
    '/',
    '%',
    '&&',
    '||',
    '==',
    '!=',
    '<',
    '<=',
    '>',
    '>=',
    '++')

$integers = @($catalog.integers)
if ($integers.Count -eq 0) {
    throw 'integers must not be empty.'
}
$integerNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$integerRows = foreach ($integer in $integers) {
    Assert-Properties `
        -Value $integer `
        -Allowed @(
            'specialType',
            'signed',
            'bitWidth',
            'minimum',
            'maximum',
            'exactIrArithmetic') `
        -Context 'integer'
    $name = Assert-EnumName `
        -Value $integer.specialType `
        -Allowed $specialTypes `
        -Context 'integer.specialType'
    if (-not $integerNames.Add($name)) {
        throw "Duplicate integer specialType '$name'."
    }
    $bitWidth = [int]$integer.bitWidth
    if ($bitWidth -le 0 -or $bitWidth -gt 64) {
        throw "integer '$name' has invalid bitWidth '$bitWidth'."
    }
    $minimum = ConvertTo-Long `
        -Value $integer.minimum `
        -Context "integer '$name'.minimum"
    $maximum = ConvertTo-Long `
        -Value $integer.maximum `
        -Context "integer '$name'.maximum"
    if ($minimum -gt $maximum) {
        throw "integer '$name' minimum exceeds maximum."
    }
    [pscustomobject]@{
        Name = $name
        Signed = Assert-Boolean `
            -Value $integer.signed `
            -Context "integer '$name'.signed"
        BitWidth = $bitWidth
        Minimum = $minimum
        Maximum = $maximum
        Exact = Assert-Boolean `
            -Value $integer.exactIrArithmetic `
            -Context "integer '$name'.exactIrArithmetic"
    }
}

$binaryRows = foreach ($binary in @($catalog.binaryOperators)) {
    Assert-Properties `
        -Value $binary `
        -Allowed @(
            'kind',
            'irOperator',
            'integerArithmetic',
            'checkedArithmetic',
            'reverseKind',
            'negatedKind') `
        -Context 'binary operator'
    $kind = Assert-EnumName `
        -Value $binary.kind `
        -Allowed $binaryKinds `
        -Context 'binary operator kind'
    [pscustomobject]@{
        Kind = $kind
        Ir = Assert-EnumName `
            -Value $binary.irOperator `
            -Allowed $irBinaryOperators `
            -Context 'binary IR operator'
        Integer = Assert-Boolean `
            -Value $binary.integerArithmetic `
            -Context "binary '$($binary.kind)'.integerArithmetic"
        Checked = Assert-Boolean `
            -Value $binary.checkedArithmetic `
            -Context "binary '$($binary.kind)'.checkedArithmetic"
        Reverse = Assert-OptionalEnumName `
            -Value $binary.reverseKind `
            -Allowed $binaryKinds `
            -Context "binary '$kind'.reverseKind"
        Negated = Assert-OptionalEnumName `
            -Value $binary.negatedKind `
            -Allowed $binaryKinds `
            -Context "binary '$kind'.negatedKind"
    }
}
if (@($binaryRows.Kind | Select-Object -Unique).Count -ne $binaryRows.Count) {
    throw 'binaryOperators contains duplicate kinds.'
}
if (@($binaryRows.Ir | Select-Object -Unique).Count -ne $binaryRows.Count) {
    throw 'binaryOperators contains duplicate IR operators.'
}
foreach ($row in $binaryRows) {
    foreach ($relation in @('Reverse', 'Negated')) {
        $targetName = $row.$relation
        if ($null -eq $targetName) {
            continue
        }
        $target = @($binaryRows | Where-Object Kind -eq $targetName)
        if ($target.Count -ne 1) {
            throw "binary '$($row.Kind)'.$relation targets missing kind '$targetName'."
        }
        $returnName = $target[0].$relation
        if ($null -eq $returnName) {
            $returnName = $target[0].Kind
        }
        if ($returnName -ne $row.Kind) {
            throw "binary '$($row.Kind)'.$relation must be involutive."
        }
    }
}

$unaryRows = foreach ($unary in @($catalog.unaryOperators)) {
    Assert-Properties `
        -Value $unary `
        -Allowed @(
            'kind',
            'irOperator',
            'identity',
            'checkedArithmetic',
            'exactIntegerDomain') `
        -Context 'unary operator'
    $ir = $null
    if ($null -ne $unary.irOperator) {
        $ir = Assert-EnumName `
            -Value $unary.irOperator `
            -Allowed $irUnaryOperators `
            -Context 'unary IR operator'
    }
    [pscustomobject]@{
        Kind = Assert-EnumName `
            -Value $unary.kind `
            -Allowed $unaryKinds `
            -Context 'unary operator kind'
        Ir = $ir
        Identity = Assert-Boolean `
            -Value $unary.identity `
            -Context "unary '$($unary.kind)'.identity"
        Checked = Assert-Boolean `
            -Value $unary.checkedArithmetic `
            -Context "unary '$($unary.kind)'.checkedArithmetic"
        Exact = Assert-Boolean `
            -Value $unary.exactIntegerDomain `
            -Context "unary '$($unary.kind)'.exactIntegerDomain"
    }
}
if (@($unaryRows.Kind | Select-Object -Unique).Count -ne $unaryRows.Count) {
    throw 'unaryOperators contains duplicate kinds.'
}

function Assert-IrOperatorCatalogRows {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Rows,

        [Parameter(Mandatory = $true)]
        [string[]]$Operators,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $rowCount = @($Rows).Count
    if (@($Rows.Operator | Select-Object -Unique).Count -ne $rowCount) {
        throw "$Name contains duplicate operators."
    }
    if (@($Rows.Key | Select-Object -Unique).Count -ne $rowCount) {
        throw "$Name contains duplicate keys."
    }
    if (@(Compare-Object `
            -ReferenceObject $Operators `
            -DifferenceObject @($Rows.Operator)).Count -ne 0) {
        throw "$Name must cover every IR operator exactly once."
    }
    $orderedKeys = @($Rows.Key | Sort-Object)
    for ($index = 0; $index -lt $rowCount; $index++) {
        if ($orderedKeys[$index] -ne $index) {
            throw "$Name keys must be contiguous from zero."
        }
    }
}

$specialRows = foreach ($special in @($catalog.specialBinaryOperators)) {
    Assert-Properties `
        -Value $special `
        -Allowed @('kind', 'resultType', 'irOperator') `
        -Context 'special binary operator'
    [pscustomobject]@{
        Kind = Assert-EnumName `
            -Value $special.kind `
            -Allowed $binaryKinds `
            -Context 'special binary kind'
        Result = Assert-EnumName `
            -Value $special.resultType `
            -Allowed $specialTypes `
            -Context 'special binary result type'
        Ir = Assert-EnumName `
            -Value $special.irOperator `
            -Allowed $irBinaryOperators `
            -Context 'special binary IR operator'
    }
}

$irUnaryRows = foreach ($operator in @($catalog.irUnaryOperators)) {
    Assert-Properties `
        -Value $operator `
        -Allowed @('operator', 'key', 'operandType', 'token') `
        -Context 'IR unary operator'
    $name = Assert-EnumName `
        -Value $operator.operator `
        -Allowed $irUnaryOperators `
        -Context 'IR unary operator name'
    $key = [int]$operator.key
    if ($key -lt 0) {
        throw "IR unary '$name'.key must be nonnegative."
    }
    [pscustomobject]@{
        Operator = $name
        Key = $key
        Operand = Assert-EnumName `
            -Value $operator.operandType `
            -Allowed $irTypeKinds `
            -Context "IR unary '$name'.operandType"
        Token = Assert-EnumName `
            -Value $operator.token `
            -Allowed $operatorTokens `
            -Context "IR unary '$name'.token"
    }
}
Assert-IrOperatorCatalogRows `
    -Rows @($irUnaryRows) `
    -Operators $irUnaryOperators `
    -Name 'irUnaryOperators'

$irBinaryRows = foreach ($operator in @($catalog.irBinaryOperators)) {
    Assert-Properties `
        -Value $operator `
        -Allowed @(
            'operator',
            'key',
            'operandType',
            'resultType',
            'token') `
        -Context 'IR binary operator'
    $name = Assert-EnumName `
        -Value $operator.operator `
        -Allowed $irBinaryOperators `
        -Context 'IR binary operator name'
    $key = [int]$operator.key
    if ($key -lt 0) {
        throw "IR binary '$name'.key must be nonnegative."
    }
    [pscustomobject]@{
        Operator = $name
        Key = $key
        Operand = Assert-OptionalEnumName `
            -Value $operator.operandType `
            -Allowed $irTypeKinds `
            -Context "IR binary '$name'.operandType"
        Result = Assert-EnumName `
            -Value $operator.resultType `
            -Allowed $irTypeKinds `
            -Context "IR binary '$name'.resultType"
        Token = Assert-EnumName `
            -Value $operator.token `
            -Allowed $operatorTokens `
            -Context "IR binary '$name'.token"
    }
}
Assert-IrOperatorCatalogRows `
    -Rows @($irBinaryRows) `
    -Operators $irBinaryOperators `
    -Name 'irBinaryOperators'

$mappedUnaryOperators = @(
    $unaryRows |
        Where-Object { $null -ne $_.Ir } |
        ForEach-Object Ir)
if (@(Compare-Object `
        -ReferenceObject $mappedUnaryOperators `
        -DifferenceObject @($irUnaryRows.Operator)).Count -ne 0) {
    throw 'Every IR unary operator must have one Roslyn unary mapping.'
}
$mappedBinaryOperators = @(
    $binaryRows.Ir
    $specialRows.Ir)
if (@($mappedBinaryOperators | Select-Object -Unique).Count -ne
    $mappedBinaryOperators.Count -or
    @(Compare-Object `
        -ReferenceObject $mappedBinaryOperators `
        -DifferenceObject @($irBinaryRows.Operator)).Count -ne 0) {
    throw 'Every IR binary operator must have one Roslyn binary mapping.'
}

Assert-Properties `
    -Value $catalog.builtInEquality `
    -Allowed @(
        'allowReferenceTypes',
        'excludedReferenceTypeKinds',
        'excludedSpecialTypes',
        'excludeAbstractReferenceTypes',
        'specialTypes') `
    -Context 'builtInEquality'
$allowReferenceEquality = Assert-Boolean `
    -Value $catalog.builtInEquality.allowReferenceTypes `
    -Context 'builtInEquality.allowReferenceTypes'
$excludedReferenceTypeKinds = @(
    $catalog.builtInEquality.excludedReferenceTypeKinds |
        ForEach-Object {
            Assert-EnumName `
                -Value $_ `
                -Allowed @('Delegate') `
                -Context 'builtInEquality.excludedReferenceTypeKinds'
        })
if (@($excludedReferenceTypeKinds | Select-Object -Unique).Count -ne
    $excludedReferenceTypeKinds.Count) {
    throw 'builtInEquality.excludedReferenceTypeKinds contains duplicates.'
}
$excludedEqualitySpecialTypes = @(
    $catalog.builtInEquality.excludedSpecialTypes |
        ForEach-Object {
            Assert-EnumName `
                -Value $_ `
                -Allowed @('System_Delegate', 'System_MulticastDelegate') `
                -Context 'builtInEquality.excludedSpecialTypes'
        })
if (@($excludedEqualitySpecialTypes | Select-Object -Unique).Count -ne
    $excludedEqualitySpecialTypes.Count) {
    throw 'builtInEquality.excludedSpecialTypes contains duplicates.'
}
$excludeAbstractReferenceTypes = Assert-Boolean `
    -Value $catalog.builtInEquality.excludeAbstractReferenceTypes `
    -Context 'builtInEquality.excludeAbstractReferenceTypes'
$equalityTypes = @(
    $catalog.builtInEquality.specialTypes |
        ForEach-Object {
            Assert-EnumName `
                -Value $_ `
                -Allowed $specialTypes `
                -Context 'builtInEquality.specialTypes'
        })
if (@($equalityTypes | Select-Object -Unique).Count -ne $equalityTypes.Count) {
    throw 'builtInEquality.specialTypes contains duplicates.'
}

$lines = New-SharpProofGeneratedHeader `
    -Generator 'scripts/Generate-CSharpScalarSemantics.ps1' `
    -Source 'SharpProof.Frontend/CSharpScalarSemantics.json.' `
    -Nullable
$lines.AddRange([string[]]@(
    '',
    'namespace SharpProof.Frontend;',
    '',
    'internal readonly struct CSharpIntegerSemantics(',
    '    SpecialType specialType,',
    '    bool isSigned,',
    '    int bitWidth,',
    '    long minimum,',
    '    long maximum,',
    '    bool supportsExactIrArithmetic = false) {',
    '    internal SpecialType SpecialType { get; } = specialType;',
    '    internal bool IsSigned { get; } = isSigned;',
    '    internal int BitWidth { get; } = bitWidth;',
    '    internal long Minimum { get; } = minimum;',
    '    internal long Maximum { get; } = maximum;',
    '    internal bool SupportsExactIrArithmetic { get; } =',
    '        supportsExactIrArithmetic;',
    '}',
    '',
    'internal readonly struct CSharpIntegerConversionSemantics(',
    '    SpecialType source,',
    '    SpecialType target,',
    '    bool isValuePreserving) {',
    '    internal SpecialType Source { get; } = source;',
    '    internal SpecialType Target { get; } = target;',
    '    internal bool IsValuePreserving { get; } = isValuePreserving;',
    '}',
    '',
    'internal readonly struct CSharpBinarySemantics(',
    '    BinaryOperatorKind kind,',
    '    IrBinaryOperator irOperator,',
    '    bool isIntegerArithmetic = false,',
    '    bool requiresCheckedArithmetic = false,',
    '    BinaryOperatorKind? reverseKind = null,',
    '    BinaryOperatorKind? negatedKind = null) {',
    '    internal BinaryOperatorKind Kind { get; } = kind;',
    '    internal IrBinaryOperator IrOperator { get; } = irOperator;',
    '    internal bool IsIntegerArithmetic { get; } = isIntegerArithmetic;',
    '    internal bool RequiresCheckedArithmetic { get; } =',
    '        requiresCheckedArithmetic;',
    '    internal BinaryOperatorKind ReverseKind { get; } =',
    '        reverseKind ?? kind;',
    '    internal BinaryOperatorKind NegatedKind { get; } =',
    '        negatedKind ?? kind;',
    '}',
    '',
    'internal readonly struct CSharpUnarySemantics(',
    '    UnaryOperatorKind kind,',
    '    IrUnaryOperator? irOperator,',
    '    bool isIdentity = false,',
    '    bool requiresCheckedArithmetic = false,',
    '    bool requiresExactIntegerDomain = false) {',
    '    internal UnaryOperatorKind Kind { get; } = kind;',
    '    internal IrUnaryOperator? IrOperator { get; } = irOperator;',
    '    internal bool IsIdentity { get; } = isIdentity;',
    '    internal bool RequiresCheckedArithmetic { get; } =',
    '        requiresCheckedArithmetic;',
    '    internal bool RequiresExactIntegerDomain { get; } =',
    '        requiresExactIntegerDomain;',
    '}',
    '',
    'internal static class CSharpScalarSemantics {',
    '    private static readonly ImmutableArray<CSharpIntegerSemantics> Integers = ['))
for ($index = 0; $index -lt $integerRows.Count; $index++) {
    $row = $integerRows[$index]
    $suffix = if ($index -eq $integerRows.Count - 1) { '' } else { ',' }
    $arguments = @(
        "SpecialType.$($row.Name)",
        ([string]$row.Signed).ToLowerInvariant(),
        [string]$row.BitWidth,
        (ConvertTo-CSharpLongLiteral $row.Minimum),
        (ConvertTo-CSharpLongLiteral $row.Maximum))
    if ($row.Exact) {
        $arguments += 'supportsExactIrArithmetic: true'
    }
    $lines.Add("        new($($arguments -join ', '))$suffix")
}
Add-Lines -Lines $lines -Values @(
    '    ];',
    '',
    '    private static readonly ImmutableArray<CSharpIntegerConversionSemantics>',
    '        IntegerConversions = [',
    '            .. Integers.SelectMany(source =>',
    '                Integers.Select(target => new CSharpIntegerConversionSemantics(',
    '                    source.SpecialType,',
    '                    target.SpecialType,',
    '                    source.Minimum >= target.Minimum &&',
    '                    source.Maximum <= target.Maximum)))',
    '        ];',
    '',
    '    private static readonly ImmutableArray<CSharpBinarySemantics>',
    '        BinaryOperators = [')
for ($index = 0; $index -lt $binaryRows.Count; $index++) {
    $row = $binaryRows[$index]
    $suffix = if ($index -eq $binaryRows.Count - 1) { '' } else { ',' }
    $arguments = @(
        "BinaryOperatorKind.$($row.Kind)",
        "IrBinaryOperator.$($row.Ir)")
    if ($row.Integer -or $row.Checked) {
        $arguments += ([string]$row.Integer).ToLowerInvariant()
    }
    if ($row.Checked) {
        $arguments += 'true'
    }
    if ($null -ne $row.Reverse) {
        $arguments += "reverseKind: BinaryOperatorKind.$($row.Reverse)"
    }
    if ($null -ne $row.Negated) {
        $arguments += "negatedKind: BinaryOperatorKind.$($row.Negated)"
    }
    $lines.Add("        new($($arguments -join ', '))$suffix")
}
Add-Lines -Lines $lines -Values @(
    '    ];',
    '',
    '    private static readonly ImmutableArray<CSharpUnarySemantics>',
    '        UnaryOperators = [')
for ($index = 0; $index -lt $unaryRows.Count; $index++) {
    $row = $unaryRows[$index]
    $suffix = if ($index -eq $unaryRows.Count - 1) { '' } else { ',' }
    $ir = if ($null -eq $row.Ir) {
        'null'
    } else {
        "IrUnaryOperator.$($row.Ir)"
    }
    $arguments = @("UnaryOperatorKind.$($row.Kind)", $ir)
    if ($row.Identity) {
        $arguments += 'isIdentity: true'
    }
    if ($row.Checked) {
        $arguments += 'requiresCheckedArithmetic: true'
    }
    if ($row.Exact) {
        $arguments += 'requiresExactIntegerDomain: true'
    }
    $lines.Add("        new($($arguments -join ', '))$suffix")
}
Add-Lines -Lines $lines -Values @(
    '    ];',
    '',
    '    internal static ImmutableArray<CSharpIntegerSemantics> SupportedIntegers =>',
    '        Integers;',
    '',
    '    internal static ImmutableArray<CSharpBinarySemantics> SupportedBinaryOperators =>',
    '        BinaryOperators;',
    '',
    '    internal static ImmutableArray<CSharpUnarySemantics> SupportedUnaryOperators =>',
    '        UnaryOperators;',
    '',
    '    internal static ImmutableArray<CSharpIntegerConversionSemantics>',
    '        SupportedIntegerConversions =>',
    '        IntegerConversions;',
    '',
    '    internal static bool IsSupportedInteger(SpecialType type) =>',
    '        TryGetInteger(type, out _);',
    '',
    '    internal static IrTypeId? TryGetBuiltInType(',
    '        IrFactory factory, SpecialType type) =>',
    '        type switch {')
foreach ($row in @($builtInSpecialTypeRows)) {
    $lines.Add(
        "            SpecialType.$($row.SpecialType) => factory.$($row.FactoryProperty),")
}
Add-Lines -Lines $lines -Values @(
    '            _ => null',
    '        };',
    '',
    '    private static bool TryGet<TSemantics, TKey>(',
    '        ImmutableArray<TSemantics> candidates,',
    '        TKey key,',
    '        Func<TSemantics, TKey> keySelector,',
    '        out TSemantics semantics)',
    '        where TSemantics : struct',
    '        where TKey : struct {',
    '        foreach (var candidate in candidates)',
    '            if (EqualityComparer<TKey>.Default.Equals(',
    '                keySelector(candidate), key)) {',
    '                semantics = candidate;',
    '                return true;',
    '            }',
    '        semantics = default;',
    '        return false;',
    '    }',
    '',
    '    internal static bool TryGetInteger(',
    '        SpecialType type,',
    '        out CSharpIntegerSemantics semantics) =>',
    '        TryGet(Integers, type,',
    '            static candidate => candidate.SpecialType, out semantics);',
    '',
    '    internal static bool TryGetIrIntegerRange(',
    '        SpecialType type,',
    '        out long minimum,',
    '        out long maximum) {',
    '        if (type is (SpecialType.System_Int32 or SpecialType.System_Int64) &&',
    '            TryGetInteger(type, out var semantics)) {',
    '            minimum = semantics.Minimum;',
    '            maximum = semantics.Maximum;',
    '            return true;',
    '        }',
    '        minimum = 0;',
    '        maximum = 0;',
    '        return false;',
    '    }',
    '',
    '    internal static IrBinaryOperator? MapBinary(',
    '        BinaryOperatorKind kind,',
    '        SpecialType resultType) {')
foreach ($row in $specialRows) {
    Add-Lines $lines @(
        "        if (kind == BinaryOperatorKind.$($row.Kind) &&",
        "            resultType == SpecialType.$($row.Result))",
        "            return IrBinaryOperator.$($row.Ir);")
}
Add-Lines -Lines $lines -Values @(
    '        return TryGetBinary(kind, out var semantics)',
    '            ? semantics.IrOperator',
    '            : null;',
    '    }',
    '',
    '    internal static BinaryOperatorKind MapBinaryToRoslyn(',
    '        IrBinaryOperator @operator) {',
    '        foreach (var candidate in BinaryOperators)',
    '            if (candidate.IrOperator == @operator)',
    '                return candidate.Kind;',
    '        return BinaryOperatorKind.None;',
    '    }',
    '',
    '    internal static BinaryOperatorKind ReverseBinary(',
    '        BinaryOperatorKind kind) =>',
    '        TryGetBinary(kind, out var semantics)',
    '            ? semantics.ReverseKind',
    '            : kind;',
    '',
    '    internal static BinaryOperatorKind NegateBinary(',
    '        BinaryOperatorKind kind) =>',
    '        TryGetBinary(kind, out var semantics)',
    '            ? semantics.NegatedKind',
    '            : kind;',
    '',
    '    internal static bool IsIntegerArithmetic(BinaryOperatorKind kind) =>',
    '        TryGetBinary(kind, out var semantics) &&',
    '        semantics.IsIntegerArithmetic;',
    '',
    '    internal static bool RequiresCheckedArithmetic(BinaryOperatorKind kind) =>',
    '        TryGetBinary(kind, out var semantics) &&',
    '        semantics.RequiresCheckedArithmetic;',
    '',
    '    internal static bool IsValuePreservingIntegerConversion(',
    '        SpecialType source,',
    '        SpecialType target) {',
    '        foreach (var conversion in IntegerConversions)',
    '            if (conversion.Source == source &&',
    '                conversion.Target == target)',
    '                return conversion.IsValuePreserving;',
    '        return false;',
    '    }',
    '',
    '    internal static bool SupportsExactIntegerIrArithmetic(SpecialType type) =>',
    '        TryGetInteger(type, out var semantics) &&',
    '        semantics.SupportsExactIrArithmetic;',
    '',
    '    internal static bool TryGetUnary(',
    '        UnaryOperatorKind kind,',
    '        out CSharpUnarySemantics semantics) =>',
    '        TryGet(UnaryOperators, kind,',
    '            static candidate => candidate.Kind, out semantics);',
    '',
    '    internal static bool SupportsBuiltInOperands(',
    '        BinaryOperatorKind kind,',
    '        ITypeSymbol? left,',
    '        ITypeSymbol? right) =>',
    '        kind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals) ||',
    '        SupportsBuiltInEquality(left) && SupportsBuiltInEquality(right);',
    '',
    '    private static bool TryGetBinary(',
    '        BinaryOperatorKind kind,',
    '        out CSharpBinarySemantics semantics) =>',
    '        TryGet(BinaryOperators, kind,',
    '            static candidate => candidate.Kind, out semantics);',
    '',
    '    private static bool SupportsBuiltInEquality(ITypeSymbol? type) =>',
    '')
if ($allowReferenceEquality) {
    $excludedKinds = @(
        $excludedReferenceTypeKinds |
            ForEach-Object { "TypeKind.$_" })
    $excludedKindPattern = if ($excludedKinds.Count -eq 1) {
        $excludedKinds[0]
    } else {
        "($($excludedKinds -join ' or '))"
    }
    $referencePattern =
        "{ IsReferenceType: true, TypeKind: not $excludedKindPattern }"
    if ($excludedEqualitySpecialTypes.Count -gt 0) {
        $excludedSpecialTypePattern = @(
            $excludedEqualitySpecialTypes |
                ForEach-Object { "SpecialType.$_" }) -join ' or '
        $referencePattern = $referencePattern.TrimEnd(' }') +
            ", SpecialType: not ($excludedSpecialTypePattern) }"
    }
    if ($excludeAbstractReferenceTypes) {
        $referencePattern = "($referencePattern and not INamedTypeSymbol { IsAbstract: true })"
    }
    $lines.Add("        type is null or $referencePattern ||")
} else {
    $lines.Add('        type is null ||')
}
foreach ($type in $equalityTypes) {
    $lines.Add("        type.SpecialType == SpecialType.$type ||")
}
Add-Lines -Lines $lines -Values @(
    '        IsSupportedInteger(type.SpecialType);',
    '}')

$irLines = New-SharpProofGeneratedHeader `
    -Generator 'scripts/Generate-CSharpScalarSemantics.ps1' `
    -Source 'SharpProof.Frontend/CSharpScalarSemantics.json.' `
    -Nullable
Add-Lines -Lines $irLines -Values @(
    '',
    'namespace SharpProof.Ir;',
    '')
Add-Lines -Lines $irLines -Values @(
    'public enum IrTypeKind',
    '{')
for ($index = 0; $index -lt $irTypeKinds.Count; $index++) {
    $suffix = if ($index -eq $irTypeKinds.Count - 1) { '' } else { ',' }
    $irLines.Add("    $($irTypeKinds[$index]) = $index$suffix")
}
Add-Lines -Lines $irLines -Values @(
    '}',
    '',
    'public enum IrUnaryOperator',
    '{')
$orderedIrUnaryRows = @($irUnaryRows | Sort-Object Key)
for ($index = 0; $index -lt $orderedIrUnaryRows.Count; $index++) {
    $row = $orderedIrUnaryRows[$index]
    $suffix = if ($index -eq $orderedIrUnaryRows.Count - 1) { '' } else { ',' }
    $irLines.Add("    $($row.Operator) = $($row.Key)$suffix")
}
Add-Lines -Lines $irLines -Values @(
    '}',
    '',
    'public enum IrBinaryOperator',
    '{')
$orderedIrBinaryRows = @($irBinaryRows | Sort-Object Key)
for ($index = 0; $index -lt $orderedIrBinaryRows.Count; $index++) {
    $row = $orderedIrBinaryRows[$index]
    $suffix = if ($index -eq $orderedIrBinaryRows.Count - 1) { '' } else { ',' }
    $irLines.Add("    $($row.Operator) = $($row.Key)$suffix")
}
Add-Lines -Lines $irLines -Values @(
    '}',
    '',
    'internal static class IrOperatorCatalog',
    '{',
    '    internal static (int Key, IrTypeKind Operand, string Token) Get(',
    '        IrUnaryOperator @operator)',
    '    {',
    '        return @operator switch',
    '        {')
foreach ($row in $orderedIrUnaryRows) {
    $irLines.Add(
        "            IrUnaryOperator.$($row.Operator) => " +
        "($($row.Key), IrTypeKind.$($row.Operand), `"$($row.Token)`"),")
}
Add-Lines -Lines $irLines -Values @(
    '            _ => throw new ArgumentOutOfRangeException(nameof(@operator))',
    '        };',
    '    }',
    '',
    '    internal static IrTypeId GetBuiltInType(',
    '        IrFactory factory, IrTypeKind kind)',
    '    {',
    '        return kind switch',
    '        {')
foreach ($row in @($irBuiltInTypeRows)) {
    $irLines.Add(
        "            IrTypeKind.$($row.Kind) => factory.$($row.FactoryProperty),")
}
Add-Lines -Lines $irLines -Values @(
    '            _ => throw new ArgumentOutOfRangeException(nameof(kind))',
    '        };',
    '    }',
    '',
    '    internal static bool IsNullable(IrTypeKind kind)',
    '    {',
    '        return kind is')
for ($index = 0; $index -lt $nullableIrTypeKinds.Count; $index++) {
    $suffix = if ($index -eq $nullableIrTypeKinds.Count - 1) { ';' } else { ' or' }
    $irLines.Add(
        "            IrTypeKind.$($nullableIrTypeKinds[$index])$suffix")
}
Add-Lines -Lines $irLines -Values @(
    '    }',
    '',
    '',
    '    internal static int GetPurityKey(IrOpaquePurity purity)',
    '    {',
    '        return purity switch',
    '        {')
foreach ($row in @($irOpaquePurityRows | Sort-Object Key)) {
    $irLines.Add(
        "            IrOpaquePurity.$($row.Purity) => $($row.Key),")
}
Add-Lines -Lines $irLines -Values @(
    '            _ => throw new ArgumentOutOfRangeException(nameof(purity))',
    '        };',
    '    }',
    '',
    '    internal static (',
    '        int Key,',
    '        IrTypeKind? Operand,',
    '        IrTypeKind Result,',
    '        string Token) Get(IrBinaryOperator @operator)',
    '    {',
    '        return @operator switch',
    '        {')
foreach ($row in $orderedIrBinaryRows) {
    $operand = if ($null -eq $row.Operand) {
        'null'
    } else {
        "IrTypeKind.$($row.Operand)"
    }
    $irLines.Add(
        "            IrBinaryOperator.$($row.Operator) => " +
        "($($row.Key), $operand, IrTypeKind.$($row.Result), `"$($row.Token)`"),")
}
Add-Lines -Lines $irLines -Values @(
    '            _ => throw new ArgumentOutOfRangeException(nameof(@operator))',
    '        };',
    '    }',
    '}')

Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content ($lines -join "`n") `
    -DisplayPath 'SharpProof.Frontend/CSharpScalarSemantics.generated.cs' `
    -GeneratorCommand '.\scripts\Generate-CSharpScalarSemantics.ps1' `
    -Verify:$Verify

Update-SharpProofGeneratedFile `
    -Path $IrOutputPath `
    -Content ($irLines -join "`n") `
    -DisplayPath 'SharpProof.Ir/IrOperatorCatalog.generated.cs' `
    -GeneratorCommand '.\scripts\Generate-CSharpScalarSemantics.ps1' `
    -Verify:$Verify
