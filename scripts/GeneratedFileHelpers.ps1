. (Join-Path $PSScriptRoot 'CSharpSourceMetrics.ps1')

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
