. (Join-Path $PSScriptRoot 'CSharpSourceMetrics.ps1')

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

function Get-MemberArray
{
    param(
        [object]$Object,
        [string]$Name
    )

    $member = $Object.PSObject.Properties[$Name]
    if ($null -eq $member -or $null -eq $member.Value)
    {
        return @()
    }
    return @($member.Value)
}

function Read-SharpProofSchema
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Context,
        [string]$ExpectedNamespace
    )

    $schema = Get-Content -LiteralPath $Path -Raw |
        ConvertFrom-Json -Depth 100
    if ([int](Get-RequiredMember $schema 'schemaVersion' 'schema') -ne 1)
    {
        throw "Only $Context schema version 1 is supported."
    }
    $namespace = [string](Get-RequiredMember $schema 'namespace' 'schema')
    $jsonNamingPolicy = [string](
        Get-RequiredMember $schema 'jsonNamingPolicy' 'schema')
    if ($jsonNamingPolicy -ne 'camelCase')
    {
        throw "Unsupported JSON naming policy '$jsonNamingPolicy'."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedNamespace) -and
        $namespace -ne $ExpectedNamespace)
    {
        throw "Unsupported $Context namespace '$namespace'."
    }
    return $schema
}

function Assert-Properties
{
    param(
        [Alias('Value')][object]$Object,
        [Alias('Names')][string[]]$Allowed,
        [string]$Context
    )

    $actual = @($Object.PSObject.Properties.Name)
    foreach ($name in $actual)
    {
        if ($name -notin $Allowed)
        {
            throw "$Context contains unsupported property '$name'."
        }
    }
    foreach ($name in $Allowed)
    {
        if ($name -notin $actual)
        {
            throw "$Context is missing required property '$name'."
        }
    }
}

function Assert-UniqueJsonProperties
{
    param(
        [Parameter(Mandatory = $true)]
        [System.Text.Json.JsonElement]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    if ($Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Array)
    {
        $index = 0
        foreach ($item in $Value.EnumerateArray())
        {
            Assert-UniqueJsonProperties $item "$Context[$index]"
            $index++
        }
        return
    }
    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object)
    {
        return
    }

    $names = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $Value.EnumerateObject())
    {
        if (-not $names.Add($property.Name))
        {
            throw "$Context contains duplicate property '$($property.Name)'."
        }
        Assert-UniqueJsonProperties $property.Value `
            "$Context.$($property.Name)"
    }
}

function Assert-EnumValue
{
    param(
        [object]$Value,
        [string[]]$Allowed,
        [string]$Context
    )

    if ($Value -isnot [string] -or [string]$Value -notin $Allowed)
    {
        throw "$Context must be one of: $($Allowed -join ', ')."
    }
    return [string]$Value
}

function Assert-Boolean
{
    param(
        [AllowNull()][object]$Value,
        [string]$Context,
        [string]$TypeDescription = 'Boolean'
    )

    if ($Value -isnot [bool])
    {
        throw "$Context must be $TypeDescription."
    }
    return [bool]$Value
}

function Assert-EnumName([object]$Value, [string[]]$Allowed, [string]$Context)
{
    return Assert-EnumValue $Value $Allowed $Context
}

function Assert-PascalCaseIdentifier
{
    param(
        [object]$Value,
        [string]$Context
    )

    if ($Value -isnot [string] -or
        [string]$Value -cnotmatch '\A[A-Z][A-Za-z0-9]*\z')
    {
        throw "$Context must be a safe PascalCase C# identifier."
    }
    return [string]$Value
}

function Assert-CSharpIdentifier([object]$Value, [string]$Context)
{
    return Assert-PascalCaseIdentifier $Value $Context
}

function Assert-Identifier
{
    param([string]$Value, [string]$Context)

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$')
    {
        throw "$Context is not a C# identifier: '$Value'."
    }
}

function Assert-TypeName
{
    param([string]$Value, [string]$Context)

    if ($Value -notmatch '^[A-Za-z_(][A-Za-z0-9_?.<>, \[\]()]*$')
    {
        throw "$Context is not an approved C# type: '$Value'."
    }
}

function Required([object]$Object, [string]$Name, [string]$Context)
{
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value)
    {
        throw "$Context must define '$Name'."
    }
    return $property.Value
}

function Identifier([string]$Value, [string]$Context)
{
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$')
    {
        throw "$Context is not a C# identifier: '$Value'."
    }
    return $Value
}

function TypeName([string]$Value, [string]$Context)
{
    if ($Value -notmatch '^[A-Za-z_(][A-Za-z0-9_?.<>, \[\]()]*$')
    {
        throw "$Context is not an approved C# type: '$Value'."
    }
    return $Value
}

function NamespaceName([string]$Value, [string]$Context)
{
    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$')
    {
        throw "$Context is not a C# namespace: '$Value'."
    }
    return $Value
}

function Get-SharpProofRepositoryRoot([string]$ScriptRoot)
{
    return (Resolve-Path (Join-Path $ScriptRoot '..')).Path
}

function Resolve-SharpProofPath
{
    param(
        [AllowNull()][string]$Path,
        [Parameter(Mandatory = $true)][string]$DefaultPath
    )

    if ([string]::IsNullOrWhiteSpace($Path))
    {
        $Path = $DefaultPath
    }
    return [IO.Path]::GetFullPath($Path)
}

function ConvertTo-CSharpString
{
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Value
    )

    if ($null -eq $Value)
    {
        return 'null'
    }

    $escaped = $Value.Replace('\', '\\').
        Replace('"', '\"').
        Replace("`r", '\r').
        Replace("`n", '\n').
        Replace("`t", '\t')
    return '"' + $escaped + '"'
}

function ConvertTo-SharpProofGeneratedText
{
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)

    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    return $normalized.TrimEnd("`n".ToCharArray()) + "`n"
}

function Format-SharpProofGeneratedCSharp
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$DisplayPath
    )

    $tree = $script:CSharpSyntaxTreeType::ParseText($Content)
    $parseErrors = @($tree.GetDiagnostics() | Where-Object {
        $_.Severity.ToString() -eq 'Error'
    })
    if ($parseErrors.Count -ne 0)
    {
        throw (
            "$DisplayPath contains generated C# parse errors: " +
            (($parseErrors | ForEach-Object { $_.ToString() }) -join '; '))
    }

    $normalized = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    $lines = $normalized.Split("`n")
    $braceIndentByLine = @{}
    foreach ($token in $tree.GetRoot().DescendantTokens())
    {
        if ((Get-CSharpSyntaxKindName $token) -ne 'OpenBraceToken')
        {
            continue
        }
        $tokenLineSpan = $token.GetLocation().GetLineSpan()
        $lineNumber = $tokenLineSpan.StartLinePosition.Line
        if ($lineNumber -lt 0 -or $lineNumber -ge $lines.Count -or
            $lines[$lineNumber] -notmatch '[ \t]+\{$')
        {
            continue
        }

        $owner = $token.Parent
        if ($null -ne $owner -and
            $owner.GetType().Name -in @('BlockSyntax', 'AccessorListSyntax'))
        {
            $owner = $owner.Parent
        }
        if ($null -eq $owner)
        {
            continue
        }
        $ownerLineSpan = $owner.GetLocation().GetLineSpan()
        $ownerLine = $ownerLineSpan.StartLinePosition.Line
        if ($ownerLine -lt 0 -or $ownerLine -ge $lines.Count)
        {
            continue
        }
        $braceIndentByLine[$lineNumber] =
            [regex]::Match($lines[$ownerLine], '^[ \t]*').Value
    }

    $formatted = [Collections.Generic.List[string]]::new()
    for ($lineNumber = 0; $lineNumber -lt $lines.Count; $lineNumber++)
    {
        $line = $lines[$lineNumber]
        $match = [regex]::Match(
            $line,
            '^(?<indent>[ \t]*)(?<body>.*\S)[ \t]+\{$',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if ($match.Success)
        {
            $formatted.Add(
                $match.Groups['indent'].Value +
                $match.Groups['body'].Value)
            $braceIndent = if ($braceIndentByLine.ContainsKey($lineNumber))
            {
                $braceIndentByLine[$lineNumber]
            }
            else
            {
                $match.Groups['indent'].Value
            }
            $formatted.Add($braceIndent + '{')
        }
        else
        {
            $formatted.Add($line)
        }
    }
    return $formatted -join "`n"
}

function Update-SharpProofGeneratedFile
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content,
        [Parameter(Mandatory = $true)][string]$DisplayPath,
        [Parameter(Mandatory = $true)][string]$GeneratorCommand,
        [switch]$Verify
    )

    $candidate = if ($Path.EndsWith(
            '.cs',
            [System.StringComparison]::OrdinalIgnoreCase))
    {
        Format-SharpProofGeneratedCSharp `
            -Content $Content `
            -DisplayPath $DisplayPath
    }
    else
    {
        $Content
    }
    $normalizedContent = ConvertTo-SharpProofGeneratedText -Text $candidate
    if ($Verify)
    {
        if (-not (Test-Path -LiteralPath $Path))
        {
            throw "$DisplayPath is missing. Run $GeneratorCommand."
        }

        $encoding = [System.Text.UTF8Encoding]::new($false)
        $expectedBytes = $encoding.GetBytes($normalizedContent)
        $actualBytes = [System.IO.File]::ReadAllBytes($Path)
        if ([Convert]::ToBase64String($actualBytes) -cne
            [Convert]::ToBase64String($expectedBytes))
        {
            throw "$DisplayPath is stale. Run $GeneratorCommand."
        }

        return
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    $temporaryPath = [System.IO.Path]::Combine(
        $directory,
        '.' + [System.IO.Path]::GetFileName($fullPath) + '.' +
            [Guid]::NewGuid().ToString('N') + '.tmp')
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $bytes = $encoding.GetBytes($normalizedContent)
    try
    {
        $stream = [System.IO.File]::Open(
            $temporaryPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        try
        {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally
        {
            $stream.Dispose()
        }

        [System.IO.File]::Move($temporaryPath, $fullPath, $true)
        $temporaryPath = $null
    }
    finally
    {
        if ($null -ne $temporaryPath -and
            [System.IO.File]::Exists($temporaryPath))
        {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
}
