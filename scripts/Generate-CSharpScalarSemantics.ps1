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

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
    $CatalogPath = Join-Path `
        $repositoryRoot `
        'SharpProof.Frontend\CSharpScalarSemantics.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path `
        $repositoryRoot `
        'SharpProof.Frontend\CSharpScalarSemantics.generated.cs'
}
$CatalogPath = [IO.Path]::GetFullPath($CatalogPath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not [IO.File]::Exists($CatalogPath)) {
    throw "C# scalar-semantics catalog not found: $CatalogPath"
}

function Assert-Properties {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string[]]$Allowed,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $actual = @($Value.PSObject.Properties.Name)
    foreach ($name in $actual) {
        if ($name -notin $Allowed) {
            throw "$Context contains unsupported property '$name'."
        }
    }
    foreach ($name in $Allowed) {
        if ($name -notin $actual) {
            throw "$Context is missing required property '$name'."
        }
    }
}

function Assert-EnumName {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string[]]$Allowed,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value -isnot [string] -or [string]$Value -notin $Allowed) {
        throw "$Context must be one of: $($Allowed -join ', ')."
    }
    return [string]$Value
}

function Assert-Boolean {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value -isnot [bool]) {
        throw "$Context must be Boolean."
    }
    return [bool]$Value
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
        'builtInEquality') `
    -Context 'catalog'
if ([int]$catalog.schemaVersion -ne 1) {
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
$irBinaryOperators = @(
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
$unaryKinds = @('Not', 'Plus', 'Minus')
$irUnaryOperators = @('Not', 'Negate')

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
            'checkedArithmetic') `
        -Context 'binary operator'
    [pscustomobject]@{
        Kind = Assert-EnumName `
            -Value $binary.kind `
            -Allowed $binaryKinds `
            -Context 'binary operator kind'
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
    }
}
if (@($binaryRows.Kind | Select-Object -Unique).Count -ne $binaryRows.Count) {
    throw 'binaryOperators contains duplicate kinds.'
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

Assert-Properties `
    -Value $catalog.builtInEquality `
    -Allowed @('allowReferenceTypes', 'specialTypes') `
    -Context 'builtInEquality'
$allowReferenceEquality = Assert-Boolean `
    -Value $catalog.builtInEquality.allowReferenceTypes `
    -Context 'builtInEquality.allowReferenceTypes'
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

$lines = [Collections.Generic.List[string]]::new()
$lines.AddRange([string[]]@(
    '// <auto-generated />',
    '// Generated by scripts/Generate-CSharpScalarSemantics.ps1 from',
    '// SharpProof.Frontend/CSharpScalarSemantics.json.',
    '#nullable enable',
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
    '    bool requiresCheckedArithmetic = false) {',
    '    internal BinaryOperatorKind Kind { get; } = kind;',
    '    internal IrBinaryOperator IrOperator { get; } = irOperator;',
    '    internal bool IsIntegerArithmetic { get; } = isIntegerArithmetic;',
    '    internal bool RequiresCheckedArithmetic { get; } =',
    '        requiresCheckedArithmetic;',
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
    '    internal static bool TryGetInteger(',
    '        SpecialType type,',
    '        out CSharpIntegerSemantics semantics) {',
    '        foreach (var candidate in Integers)',
    '            if (candidate.SpecialType == type) {',
    '                semantics = candidate;',
    '                return true;',
    '            }',
    '        semantics = default;',
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
    '        out CSharpUnarySemantics semantics) {',
    '        foreach (var candidate in UnaryOperators)',
    '            if (candidate.Kind == kind) {',
    '                semantics = candidate;',
    '                return true;',
    '            }',
    '        semantics = default;',
    '        return false;',
    '    }',
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
    '        out CSharpBinarySemantics semantics) {',
    '        foreach (var candidate in BinaryOperators)',
    '            if (candidate.Kind == kind) {',
    '                semantics = candidate;',
    '                return true;',
    '            }',
    '        semantics = default;',
    '        return false;',
    '    }',
    '',
    '    private static bool SupportsBuiltInEquality(ITypeSymbol? type) =>',
    '        type == null ||')
if ($allowReferenceEquality) {
    $lines.Add('        type.IsReferenceType ||')
}
foreach ($type in $equalityTypes) {
    $lines.Add("        type.SpecialType == SpecialType.$type ||")
}
Add-Lines -Lines $lines -Values @(
    '        IsSupportedInteger(type.SpecialType);',
    '}')

Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content ($lines -join "`n") `
    -DisplayPath 'SharpProof.Frontend/CSharpScalarSemantics.generated.cs' `
    -GeneratorCommand '.\scripts\Generate-CSharpScalarSemantics.ps1' `
    -Verify:$Verify
