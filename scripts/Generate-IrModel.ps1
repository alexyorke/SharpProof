[CmdletBinding()]
param(
    [Parameter()][string]$SchemaPath,
    [Parameter()][string]$OutputPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')
. (Join-Path $PSScriptRoot 'Assert-SharpProofUniqueJsonProperties.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($SchemaPath))
{
    $SchemaPath = Join-Path $repositoryRoot 'SharpProof.Ir\IrModel.schema.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $repositoryRoot 'SharpProof.Ir\IrModel.generated.cs'
}
$SchemaPath = [IO.Path]::GetFullPath($SchemaPath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not [IO.File]::Exists($SchemaPath))
{
    throw "IR model schema not found: $SchemaPath"
}

function Get-RequiredMember
{
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $member = $Object.PSObject.Properties[$Name]
    if ($null -eq $member -or $null -eq $member.Value)
    {
        throw "$Context must define '$Name'."
    }
    return $member.Value
}

function Get-OptionalArray
{
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $member = $Object.PSObject.Properties[$Name]
    if ($null -eq $member -or $null -eq $member.Value)
    {
        return @()
    }
    return @($member.Value)
}

function Get-OptionalString
{
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $member = $Object.PSObject.Properties[$Name]
    if ($null -eq $member -or $null -eq $member.Value)
    {
        return ''
    }
    return [string]$member.Value
}

function Get-OptionalBoolean
{
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $member = $Object.PSObject.Properties[$Name]
    if ($null -eq $member -or $null -eq $member.Value)
    {
        return $false
    }
    if ($member.Value -isnot [bool])
    {
        throw "'$Name' must be a Boolean."
    }
    return [bool]$member.Value
}

function Assert-Identifier
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$')
    {
        throw "$Context is not a C# identifier: '$Value'."
    }
}

function Assert-TypeName
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_?.<>, ]*$')
    {
        throw "$Context is not an approved C# type: '$Value'."
    }
}

$cSharpKeywords = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
@(
    'abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch',
    'char', 'checked', 'class', 'const', 'continue', 'decimal', 'default',
    'delegate', 'do', 'double', 'else', 'enum', 'event', 'explicit',
    'extern', 'false', 'finally', 'fixed', 'float', 'for', 'foreach',
    'goto', 'if', 'implicit', 'in', 'int', 'interface', 'internal', 'is',
    'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator',
    'out', 'override', 'params', 'private', 'protected', 'public',
    'readonly', 'ref', 'return', 'sbyte', 'sealed', 'short', 'sizeof',
    'stackalloc', 'static', 'string', 'struct', 'switch', 'this', 'throw',
    'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe',
    'ushort', 'using', 'virtual', 'void', 'volatile', 'while'
) | ForEach-Object {
    [void]$cSharpKeywords.Add($_)
}

function ConvertTo-CSharpIdentifier
{
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($cSharpKeywords.Contains($Value))
    {
        return "@$Value"
    }
    return $Value
}

function Assert-ConstructorExpression
{
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)]
        [Collections.Generic.HashSet[string]]$Parameters,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Parameters.Contains($Value))
    {
        return
    }
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$')
    {
        throw "$Context is not a constructor parameter or enum member: '$Value'."
    }
}

$schemaJson = Get-Content -LiteralPath $SchemaPath -Raw
$schemaDocument = [System.Text.Json.JsonDocument]::Parse($schemaJson)
try
{
    Assert-SharpProofUniqueJsonProperties `
        -Value $schemaDocument.RootElement `
        -Context 'IR model schema'
}
finally
{
    $schemaDocument.Dispose()
}
$schema = $schemaJson | ConvertFrom-Json
if ([int](Get-RequiredMember $schema 'schemaVersion' 'IR model schema') -ne 1)
{
    throw 'IR model schema version must be 1.'
}
$namespace = [string](Get-RequiredMember $schema 'namespace' 'IR model schema')
if ($namespace -ne 'SharpProof.Ir')
{
    throw "IR model namespace must be 'SharpProof.Ir'."
}
$declarations = @(
    Get-RequiredMember $schema 'declarations' 'IR model schema')
if ($declarations.Count -eq 0)
{
    throw 'IR model schema must contain declarations.'
}

$declarationNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($declaration in $declarations)
{
    $name = [string](
        Get-RequiredMember $declaration 'name' 'IR model declaration')
    Assert-Identifier $name 'IR model declaration name'
    if (-not $declarationNames.Add($name))
    {
        throw "IR model schema repeats declaration '$name'."
    }
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('// <auto-generated>')
$lines.Add('// Generated by scripts/Generate-IrModel.ps1 from')
$lines.Add('// SharpProof.Ir/IrModel.schema.json (schema version 1).')
$lines.Add('// Only declarative IR vocabulary and storage are generated here.')
$lines.Add('// Handwritten validation and computation remain in IrProgram.cs.')
$lines.Add('// Do not edit this file directly.')
$lines.Add('// </auto-generated>')
$lines.Add('#nullable enable')
$lines.Add('')
$lines.Add("namespace $namespace;")
$lines.Add('')
$lines.Add('public interface IIrIdentifierTag')
$lines.Add('{')
$lines.Add('    string Prefix { get; }')
$lines.Add('}')
foreach ($tag in @(
        @{ Name = 'IrIdentityTag'; Prefix = 'identity' },
        @{ Name = 'IrTermTag'; Prefix = 'ir' },
        @{ Name = 'IrVariableTag'; Prefix = 'v' },
        @{ Name = 'IrTypeTag'; Prefix = 't' },
        @{ Name = 'IrMemberTag'; Prefix = 'm' },
        @{ Name = 'IrStringTag'; Prefix = 's' },
        @{ Name = 'IrOperationTag'; Prefix = 'op' },
        @{ Name = 'IrBlockTag'; Prefix = 'b' },
        @{ Name = 'IrInstructionTag'; Prefix = 'i' })) {
    $lines.Add('')
    $lines.Add("public readonly record struct $($tag.Name) : IIrIdentifierTag")
    $lines.Add('{')
    $lines.Add('    public string Prefix => "' + $tag.Prefix + '";')
    $lines.Add('}')
}

foreach ($declaration in $declarations)
{
    $kind = [string](
        Get-RequiredMember $declaration 'kind' 'IR model declaration')
    $name = [string]$declaration.name
    $lines.Add('')
    if ($kind -eq 'enum')
    {
        $members = @(
            Get-RequiredMember $declaration 'members' "enum '$name'")
        if ($members.Count -eq 0)
        {
            throw "Enum '$name' must define members."
        }
        $memberNames = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        $memberValues = [Collections.Generic.HashSet[int]]::new()
        $lines.Add("public enum $name")
        $lines.Add('{')
        for ($index = 0; $index -lt $members.Count; $index++)
        {
            $member = $members[$index]
            $memberName = [string](
                Get-RequiredMember $member 'name' "enum '$name' member")
            Assert-Identifier $memberName "Enum '$name' member"
            if (-not $memberNames.Add($memberName))
            {
                throw "Enum '$name' repeats member '$memberName'."
            }
            $valueMember = $member.PSObject.Properties['value']
            if ($null -eq $valueMember -or
                $valueMember.Value -isnot [ValueType])
            {
                throw "Enum '$name.$memberName' must have an Int32 value."
            }
            try
            {
                $value = [Convert]::ToInt32(
                    $valueMember.Value,
                    [Globalization.CultureInfo]::InvariantCulture)
            }
            catch
            {
                throw "Enum '$name.$memberName' must have an Int32 value."
            }
            if ([decimal]$valueMember.Value -ne [decimal]$value)
            {
                throw "Enum '$name.$memberName' must have an Int32 value."
            }
            if (-not $memberValues.Add($value))
            {
                throw "Enum '$name' repeats numeric value '$value'."
            }
            $comma = if ($index -lt $members.Count - 1) { ',' } else { '' }
            $lines.Add("    $memberName = $value$comma")
        }
        $lines.Add('}')
        continue
    }
    if ($kind -ne 'class')
    {
        throw "Declaration '$name' has unsupported kind '$kind'."
    }

    $accessibility = [string](
        Get-RequiredMember $declaration 'accessibility' "class '$name'")
    if ($accessibility -notin 'public', 'internal')
    {
        throw "Class '$name' has invalid accessibility '$accessibility'."
    }
    $modifier = [string](
        Get-RequiredMember $declaration 'modifier' "class '$name'")
    if ($modifier -notin 'abstract', 'sealed')
    {
        throw "Class '$name' has invalid modifier '$modifier'."
    }
    $partial = Get-OptionalBoolean $declaration 'partial'
    $partialSource = if ($partial) { ' partial' } else { '' }
    $baseType = Get-OptionalString $declaration 'baseType'
    if ($baseType.Length -ne 0)
    {
        Assert-Identifier $baseType "Class '$name' base type"
        if (-not $declarationNames.Contains($baseType))
        {
            throw "Class '$name' references unknown base type '$baseType'."
        }
    }
    $baseSource = if ($baseType.Length -eq 0) { '' } else { " : $baseType" }
    $lines.Add(
        "$accessibility $modifier$partialSource class $name$baseSource")
    $lines.Add('{')

    $properties = @(
        Get-RequiredMember $declaration 'properties' "class '$name'")
    $propertyNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $generatedProperties = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $properties)
    {
        $propertyName = [string](
            Get-RequiredMember $property 'name' "class '$name' property")
        $propertyType = [string](
            Get-RequiredMember $property 'type' "property '$name.$propertyName'")
        $propertyAccessibility = [string](
            Get-RequiredMember `
                $property `
                'accessibility' `
                "property '$name.$propertyName'")
        $implementation = [string](
            Get-RequiredMember `
                $property `
                'implementation' `
                "property '$name.$propertyName'")
        Assert-Identifier $propertyName "Class '$name' property"
        Assert-TypeName $propertyType "Property '$name.$propertyName'"
        if (-not $propertyNames.Add($propertyName))
        {
            throw "Class '$name' repeats property '$propertyName'."
        }
        if ($propertyAccessibility -notin 'public', 'internal')
        {
            throw "Property '$name.$propertyName' has invalid accessibility."
        }
        if ($implementation -notin 'generated', 'handwritten')
        {
            throw "Property '$name.$propertyName' has invalid implementation."
        }
        if ($implementation -eq 'handwritten')
        {
            if (-not $partial)
            {
                throw "Class '$name' has handwritten members but is not partial."
            }
            continue
        }
        [void]$generatedProperties.Add($propertyName)
    }

    $constructor = Get-RequiredMember `
        $declaration `
        'constructor' `
        "class '$name'"
    $constructorAccessibility = [string](
        Get-RequiredMember `
            $constructor `
            'accessibility' `
            "class '$name' constructor")
    if ($constructorAccessibility -notin 'internal', 'private protected')
    {
        throw "Class '$name' constructor has invalid accessibility."
    }
    $constructorImplementation = [string](
        Get-RequiredMember `
            $constructor `
            'implementation' `
            "class '$name' constructor")
    if ($constructorImplementation -notin 'generated', 'handwritten')
    {
        throw "Class '$name' constructor has invalid implementation."
    }
    if ($constructorImplementation -eq 'handwritten' -and -not $partial)
    {
        throw "Class '$name' has a handwritten constructor but is not partial."
    }
    $parameters = @(
        Get-RequiredMember `
            $constructor `
            'parameters' `
            "class '$name' constructor")
    $parameterNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $parameterSources = [Collections.Generic.List[string]]::new()
    foreach ($parameter in $parameters)
    {
        $parameterName = [string](
            Get-RequiredMember `
                $parameter `
                'name' `
                "class '$name' constructor parameter")
        $parameterType = [string](
            Get-RequiredMember `
                $parameter `
                'type' `
                "class '$name' constructor parameter '$parameterName'")
        Assert-Identifier $parameterName "Class '$name' constructor parameter"
        Assert-TypeName `
            $parameterType `
            "Class '$name' constructor parameter '$parameterName'"
        if (-not $parameterNames.Add($parameterName))
        {
            throw "Class '$name' repeats constructor parameter '$parameterName'."
        }
        $parameterSources.Add(
            "$parameterType $(ConvertTo-CSharpIdentifier $parameterName)")
    }

    $baseArguments = @(Get-OptionalArray $constructor 'baseArguments')
    foreach ($baseArgument in $baseArguments)
    {
        Assert-ConstructorExpression `
            ([string]$baseArgument) `
            $parameterNames `
            "Class '$name' base argument"
    }
    if ($baseArguments.Count -ne 0 -and $baseType.Length -eq 0)
    {
        throw "Class '$name' has base arguments without a base type."
    }

    $assignments = @(Get-OptionalArray $constructor 'assignments')
    $assignedProperties = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($assignment in $assignments)
    {
        $propertyName = [string](
            Get-RequiredMember `
                $assignment `
                'property' `
                "class '$name' constructor assignment")
        $parameterName = [string](
            Get-RequiredMember `
                $assignment `
                'parameter' `
                "class '$name' constructor assignment")
        if (-not $generatedProperties.Contains($propertyName))
        {
            throw (
                "Class '$name' constructor assigns unknown generated property " +
                "'$propertyName'.")
        }
        if (-not $parameterNames.Contains($parameterName))
        {
            throw (
                "Class '$name' constructor assigns from unknown parameter " +
                "'$parameterName'.")
        }
        if (-not $assignedProperties.Add($propertyName))
        {
            throw "Class '$name' assigns property '$propertyName' more than once."
        }
    }
    if ($constructorImplementation -eq 'handwritten')
    {
        if ($assignments.Count -ne 0 -or $baseArguments.Count -ne 0)
        {
            throw (
                "Class '$name' handwritten constructor cannot define generated " +
                'assignments or base arguments.')
        }
    }
    elseif (-not $assignedProperties.SetEquals($generatedProperties))
    {
        $missing = @(
            $generatedProperties |
                Where-Object { -not $assignedProperties.Contains($_) } |
                Sort-Object)
        throw (
            "Class '$name' generated constructor does not assign exactly its " +
            "generated properties. Missing: $($missing -join ', ').")
    }

    if ($constructorImplementation -eq 'generated')
    {
        if ($parameterSources.Count -le 3)
        {
            $parameterSource = $parameterSources -join ', '
            $constructorLine =
                "    $constructorAccessibility $name($parameterSource)"
            if ($baseArguments.Count -ne 0)
            {
                $escapedBaseArguments = @(
                    $baseArguments | ForEach-Object {
                        $value = [string]$_
                        if ($parameterNames.Contains($value))
                        {
                            ConvertTo-CSharpIdentifier $value
                        }
                        else
                        {
                            $value
                        }
                    })
                $constructorLine +=
                    " : base($($escapedBaseArguments -join ', '))"
            }
            $lines.Add($constructorLine)
        }
        else
        {
            $lines.Add("    $constructorAccessibility $name(")
            for ($index = 0; $index -lt $parameterSources.Count; $index++)
            {
                $comma = if ($index -lt $parameterSources.Count - 1)
                {
                    ','
                }
                else
                {
                    ''
                }
                $lines.Add("        $($parameterSources[$index])$comma")
            }
            $close = '    )'
            if ($baseArguments.Count -ne 0)
            {
                $escapedBaseArguments = @(
                    $baseArguments | ForEach-Object {
                        $value = [string]$_
                        if ($parameterNames.Contains($value))
                        {
                            ConvertTo-CSharpIdentifier $value
                        }
                        else
                        {
                            $value
                        }
                    })
                $close += " : base($($escapedBaseArguments -join ', '))"
            }
            $lines.Add($close)
        }
        $lines.Add('    {')
        if ($assignments.Count -ne 0)
        {
            if ($assignments.Count -eq 1)
            {
                $assignment = $assignments[0]
                $lines.Add(
                    "        $($assignment.property) = " +
                    "$(ConvertTo-CSharpIdentifier ([string]$assignment.parameter));")
            }
            else
            {
                $propertySource = @(
                    $assignments | ForEach-Object { [string]$_.property })
                $parameterSource = @(
                    $assignments | ForEach-Object {
                        ConvertTo-CSharpIdentifier ([string]$_.parameter)
                    })
                $lines.Add(
                    "        ($($propertySource -join ', ')) =")
                $lines.Add(
                    "            ($($parameterSource -join ', '));")
            }
        }
        $lines.Add('    }')
    }

    foreach ($property in $properties)
    {
        if ([string]$property.implementation -ne 'generated')
        {
            continue
        }
        $lines.Add('')
        $lines.Add(
            "    $($property.accessibility) $($property.type) " +
            "$($property.name)")
        $lines.Add('    {')
        $lines.Add('        get;')
        $lines.Add('    }')
    }
    $lines.Add('}')
}

$content = $lines -join "`n"
Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content $content `
    -DisplayPath 'SharpProof.Ir/IrModel.generated.cs' `
    -GeneratorCommand '.\scripts\Generate-IrModel.ps1' `
    -Verify:$Verify

$aliasLines = [Collections.Generic.List[string]]::new()
$aliasLines.Add('// <auto-generated/>')
$aliasLines.Add('// Generated by scripts/Generate-IrModel.ps1.')
$aliasLines.Add('// IR identifier aliases are declarative projections.')
$aliasLines.Add('// Do not edit this file directly.')
$aliasLines.Add('')
$aliasLines.Add('global using IrIdentityId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrIdentityTag>;')
$aliasLines.Add('global using IrId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrTermTag>;')
$aliasLines.Add('global using IrVarId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;')
$aliasLines.Add('global using IrTypeId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrTypeTag>;')
$aliasLines.Add('global using IrMemberId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;')
$aliasLines.Add('global using IrStringId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrStringTag>;')
$aliasLines.Add('global using OperationId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrOperationTag>;')
$aliasLines.Add('global using IrBlockId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrBlockTag>;')
$aliasLines.Add('global using IrInstructionId =')
$aliasLines.Add('    SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrInstructionTag>;')
Update-SharpProofGeneratedFile `
    -Path (Join-Path $repositoryRoot 'SharpProof.Ir\IrIdentifierAliases.cs') `
    -Content ($aliasLines -join "`n") `
    -DisplayPath 'SharpProof.Ir/IrIdentifierAliases.cs' `
    -GeneratorCommand '.\scripts\Generate-IrModel.ps1' `
    -Verify:$Verify

$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb SharpProof.Ir/IrModel.generated.cs and IR identifier aliases."
