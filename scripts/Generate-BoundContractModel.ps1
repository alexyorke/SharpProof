[CmdletBinding()]
param(
    [Parameter()][string]$SchemaPath,
    [Parameter()][string]$OutputPath,
    [Parameter()][Alias('Check')][switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Get-SharpProofRepositoryRoot $PSScriptRoot
$SchemaPath = Resolve-SharpProofPath $SchemaPath (
    Join-Path $repositoryRoot 'SharpProof.Contracts\BoundContractModel.schema.json')
$OutputPath = Resolve-SharpProofPath $OutputPath (
    Join-Path $repositoryRoot 'SharpProof.Contracts\BoundContractModel.generated.cs')
$schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json

if ([int](Required $schema 'schemaVersion' 'Bound contract model') -ne 1) {
    throw 'Bound contract model schema version must be 1.'
}
if ([string](Required $schema 'namespace' 'Bound contract model') -ne
    'SharpProof.Contracts') {
    throw 'Bound contract model namespace must be SharpProof.Contracts.'
}

$lines = New-SharpProofGeneratedHeader `
    -Generator 'Generate-BoundContractModel.ps1' `
    -Source 'SharpProof.Contracts/BoundContractModel.schema.json.' `
    -Notes @('Declarative bound-contract vocabulary and storage only.') `
    -Nullable
$lines.Add('')
$lines.Add('namespace SharpProof.Contracts;')

foreach ($enum in @($schema.enums)) {
    $name = [string](Required $enum 'name' 'Bound contract enum')
    Assert-Identifier $name 'Bound contract enum name'
    $members = @(Required $enum 'members' "enum '$name'")
    if ($members.Count -eq 0) {
        throw "enum '$name' must define members."
    }
    $lines.Add('')
    $lines.Add("public enum $name")
    $lines.Add('{')
    for ($index = 0; $index -lt $members.Count; $index++) {
        $member = [string]$members[$index]
        Assert-Identifier $member "enum '$name' member"
        $suffix = if ($index -lt $members.Count - 1) { ',' } else { '' }
        $lines.Add("    $member$suffix")
    }
    $lines.Add('}')
}

foreach ($class in @($schema.classes)) {
    $name = [string](Required $class 'name' 'Bound contract class')
    Assert-Identifier $name 'Bound contract class name'
    $constructor = Required $class 'constructor' "class '$name'"
    $access = [string](Required $constructor 'access' "class '$name' constructor")
    if ($access -notin @('internal', 'public')) {
        throw "class '$name' has unsupported constructor access '$access'."
    }
    $parameters = @(Required $constructor 'parameters' "class '$name' constructor")
    $assignments = @(Required $constructor 'assignments' "class '$name' constructor")
    if ($parameters.Count -ne $assignments.Count) {
        throw "class '$name' constructor assignments must match parameters."
    }
    $lines.Add('')
    $lines.Add("public sealed class $name")
    $lines.Add('{')
    $lines.Add("    $access $name(")
    $parametersByName =
        [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $parameters.Count; $index++) {
        $parameter = $parameters[$index]
        $type = [string](Required $parameter 'type' "class '$name' parameter")
        $parameterName = [string](Required $parameter 'name' "class '$name' parameter")
        Assert-TypeName $type "class '$name' parameter type"
        Assert-Identifier $parameterName "class '$name' parameter name"
        if (-not $parametersByName.TryAdd($parameterName, $parameter)) {
            throw "class '$name' constructor parameters must be unique."
        }
        $comma = if ($index -lt $parameters.Count - 1) { ',' } else { '' }
        $lines.Add("        $type $parameterName$comma")
    }
    $lines.Add('    )')
    $lines.Add('    {')
    foreach ($assignment in $assignments) {
        $propertyName = [string]$assignment
        Assert-Identifier $propertyName "class '$name' assignment"
        $parameterName =
            $propertyName.Substring(0, 1).ToLowerInvariant() +
            $propertyName.Substring(1)
        if (-not $parametersByName.ContainsKey($parameterName)) {
            throw "class '$name' assignment '$propertyName' has no matching parameter."
        }
        $lines.Add("        $propertyName = $parameterName;")
    }
    $lines.Add('    }')
    foreach ($property in @(Required $class 'properties' "class '$name'")) {
        $propertyName = [string](Required $property 'name' "class '$name' property")
        $type = [string](Required $property 'type' "class '$name' property")
        Assert-Identifier $propertyName "class '$name' property name"
        Assert-TypeName $type "class '$name' property type"
        $lines.Add("    public $type $propertyName { get; }")
    }
    $projectionProperty = $class.PSObject.Properties['projections']
    $projections = if ($null -eq $projectionProperty) {
        @()
    }
    else {
        @($projectionProperty.Value)
    }
    foreach ($projection in $projections) {
        $propertyName = [string](Required $projection 'name' "class '$name' projection")
        $type = [string](Required $projection 'type' "class '$name' projection")
        $expression = [string](Required $projection 'expression' "class '$name' projection")
        Assert-Identifier $propertyName "class '$name' projection name"
        Assert-TypeName $type "class '$name' projection type"
        if ($expression -notmatch '^[A-Za-z_][A-Za-z0-9_ .?=<>|&!()]+$') {
            throw "class '$name' projection expression is not approved."
        }
        $lines.Add("    public $type $propertyName => $expression;")
    }
    $lines.Add('}')
}

Update-SharpProofGeneratedFile `
    -Path $OutputPath `
    -Content ($lines -join "`n") `
    -DisplayPath 'SharpProof.Contracts/BoundContractModel.generated.cs' `
    -GeneratorCommand '.\scripts\Generate-BoundContractModel.ps1' `
    -Verify:$Verify
$verb = if ($Verify) { 'Verified' } else { 'Generated' }
Write-Host "$verb deterministic bound-contract model."
