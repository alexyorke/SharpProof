[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$AssemblyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$paths = @($AssemblyPath | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
$directories = @($paths | ForEach-Object { [System.IO.Path]::GetDirectoryName($_) } | Sort-Object -Unique)
$resolve = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = ([System.Reflection.AssemblyName]::new($eventArgs.Name)).Name + '.dll'
    foreach ($directory in $directories)
    {
        $candidate = Join-Path $directory $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf)
        {
            return [System.Reflection.Assembly]::LoadFrom($candidate)
        }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolve)

function Format-TypeName {
    param([Parameter(Mandatory = $true)][Type]$Type)

    if ($Type.IsByRef) { return "$(Format-TypeName $Type.GetElementType())&" }
    if ($Type.IsPointer) { return "$(Format-TypeName $Type.GetElementType())*" }
    if ($Type.IsArray)
    {
        return "$(Format-TypeName $Type.GetElementType())[$(',' * ($Type.GetArrayRank() - 1))]"
    }
    if ($Type.IsGenericParameter) { return "!$($Type.GenericParameterPosition):$($Type.Name)" }
    if (-not $Type.IsGenericType) { return $Type.FullName ?? $Type.Name }
    $definition = $Type.GetGenericTypeDefinition()
    $name = ($definition.FullName ?? $definition.Name) -replace '`\d+', ''
    $arguments = @($Type.GetGenericArguments() | ForEach-Object { Format-TypeName $_ })
    return "$name<$($arguments -join ',')>"
}

function Format-DefaultValue {
    param($Value)

    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return '"' + ($Value -replace '\\', '\\' -replace '"', '\"') + '"' }
    if ($Value -is [char]) { return "'$Value'" }
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($Value -is [Type]) { return "typeof($(Format-TypeName $Value))" }
    if ($Value.GetType().IsEnum)
    {
        return "$(Format-TypeName $Value.GetType()):$([Convert]::ToInt64($Value, [Globalization.CultureInfo]::InvariantCulture))"
    }
    if ($Value -is [System.Reflection.Missing]) { return 'missing' }
    if ($Value -is [DBNull]) { return 'dbnull' }
    return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Format-AttributeArgument {
    param([Parameter(Mandatory = $true)][System.Reflection.CustomAttributeTypedArgument]$Argument)

    $value = if ($Argument.Value -is [System.Collections.IEnumerable] -and
        $Argument.Value -isnot [string])
    {
        '[' + ((@($Argument.Value) | ForEach-Object { Format-AttributeArgument $_ }) -join ',') + ']'
    }
    else
    {
        Format-DefaultValue $Argument.Value
    }
    return "$(Format-TypeName $Argument.ArgumentType):$value"
}

function Format-Attributes {
    param([Parameter(Mandatory = $true)]$Provider)

    $attributes = @($Provider.GetCustomAttributesData() | ForEach-Object {
        $attribute = $_
        $arguments = @($attribute.ConstructorArguments | ForEach-Object { Format-AttributeArgument $_ })
        $named = @($attribute.NamedArguments | ForEach-Object {
            "$($_.MemberName)=$(Format-AttributeArgument $_.TypedValue)"
        } | Sort-Object)
        "$(Format-TypeName $attribute.AttributeType)($($arguments -join ',');$($named -join ','))"
    } | Sort-Object)
    if ($attributes.Count -eq 0) { return '' }
    return " attrs=[$($attributes -join '|')]"
}

function Format-GenericParameter {
    param([Parameter(Mandatory = $true)][Type]$Parameter)

    $constraints = @($Parameter.GetGenericParameterConstraints() | ForEach-Object { Format-TypeName $_ } | Sort-Object)
    return "$($Parameter.Name){$($Parameter.GenericParameterAttributes);$($constraints -join ',')}"
}

function Format-Parameter {
    param([Parameter(Mandatory = $true)][System.Reflection.ParameterInfo]$Parameter)

    $prefix = if ($Parameter.IsOut) { 'out ' } elseif ($Parameter.IsIn -and $Parameter.ParameterType.IsByRef) { 'in ' } elseif ($Parameter.ParameterType.IsByRef) { 'ref ' } else { '' }
    $optional = if ($Parameter.IsOptional) { "=$(Format-DefaultValue $Parameter.DefaultValue)" } else { '' }
    return "$prefix$(Format-TypeName $Parameter.ParameterType) $($Parameter.Name)$optional$(Format-Attributes $Parameter)"
}

function Format-Method {
    param([Parameter(Mandatory = $true)][System.Reflection.MethodInfo]$Method)

    $flags = @(
        if ($Method.IsStatic) { 'static' }
        if ($Method.IsAbstract) { 'abstract' }
        elseif ($Method.IsVirtual -and -not $Method.IsFinal) { 'virtual' }
        elseif ($Method.IsFinal -and $Method.IsVirtual) { 'sealed-virtual' }
    )
    $generic = if ($Method.IsGenericMethodDefinition)
    {
        '<' + (($Method.GetGenericArguments() | ForEach-Object { Format-GenericParameter $_ }) -join ',') + '>'
    }
    else { '' }
    $parameters = @($Method.GetParameters() | ForEach-Object { Format-Parameter $_ })
    return "method $($flags -join ' ') $(Format-TypeName $Method.ReturnType) $($Method.Name)$generic($($parameters -join ','))$(Format-Attributes $Method) return$(Format-Attributes $Method.ReturnParameter)"
}

function Format-TypeDeclaration {
    param([Parameter(Mandatory = $true)][Type]$Type)

    $kind = if ($Type.IsInterface) { 'interface' } elseif ($Type.IsEnum) { 'enum' } elseif ($Type.IsValueType) { 'struct' } elseif ($Type.IsClass) { 'class' } else { 'type' }
    $flags = @(
        if ($Type.IsNestedPublic) { 'nested-public' } else { 'public' }
        if ($Type.IsAbstract -and $Type.IsSealed) { 'static' }
        elseif ($Type.IsAbstract -and -not $Type.IsInterface) { 'abstract' }
        elseif ($Type.IsSealed -and -not $Type.IsValueType) { 'sealed' }
    )
    $baseType = if ($null -ne $Type.BaseType) { Format-TypeName $Type.BaseType } else { '-' }
    $interfaces = @($Type.GetInterfaces() | ForEach-Object { Format-TypeName $_ } | Sort-Object)
    "type $($flags -join ' ') $kind $(Format-TypeName $Type) : $baseType [$($interfaces -join ',')]$(Format-Attributes $Type)"

    $binding = [System.Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly'
    $members = [System.Collections.Generic.List[string]]::new()
    foreach ($parameter in $Type.GetGenericArguments())
    {
        if ($parameter.DeclaringType -eq $Type) { $members.Add("generic $(Format-GenericParameter $parameter)") }
    }
    foreach ($constructor in $Type.GetConstructors($binding))
    {
        $parameters = @($constructor.GetParameters() | ForEach-Object { Format-Parameter $_ })
        $prefix = if ($constructor.IsStatic) { 'static ' } else { '' }
        $members.Add("ctor $prefix($($parameters -join ','))$(Format-Attributes $constructor)")
    }
    foreach ($method in $Type.GetMethods($binding))
    {
        if (-not $method.IsSpecialName -or $method.Name.StartsWith('op_', [StringComparison]::Ordinal))
        {
            $members.Add((Format-Method $method))
        }
    }
    foreach ($property in $Type.GetProperties($binding))
    {
        $access = @(
            if ($null -ne $property.GetMethod) { 'get' }
            if ($null -ne $property.SetMethod) { 'set' }
        ) -join ','
        $index = @($property.GetIndexParameters() | ForEach-Object { Format-Parameter $_ })
        $members.Add("property $access $(Format-TypeName $property.PropertyType) $($property.Name)[$($index -join ',')]$(Format-Attributes $property)")
    }
    foreach ($field in $Type.GetFields($binding))
    {
        $flags = @(
            if ($field.IsLiteral) { 'const' } elseif ($field.IsStatic) { 'static' }
            if ($field.IsInitOnly) { 'readonly' }
        ) -join ' '
        $value = if ($field.IsLiteral) { "=$(Format-DefaultValue $field.GetRawConstantValue())" } else { '' }
        $members.Add("field $flags $(Format-TypeName $field.FieldType) $($field.Name)$value$(Format-Attributes $field)")
    }
    foreach ($event in $Type.GetEvents($binding))
    {
        $members.Add("event $(Format-TypeName $event.EventHandlerType) $($event.Name)$(Format-Attributes $event)")
    }
    foreach ($member in $members | Sort-Object) { "  $member" }
}

try
{
    foreach ($path in $paths | Sort-Object)
    {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Assembly '$path' does not exist." }
        $assembly = [System.Reflection.Assembly]::LoadFrom($path)
        "assembly $($assembly.GetName().Name)"
        foreach ($type in $assembly.GetExportedTypes() | Sort-Object FullName)
        {
            Format-TypeDeclaration $type
        }
    }
}
finally
{
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolve)
}
