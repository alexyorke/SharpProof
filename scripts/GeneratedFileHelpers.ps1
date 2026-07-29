. (Join-Path $PSScriptRoot 'CSharpSourceMetrics.ps1')

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

        $existing = ConvertTo-SharpProofGeneratedText -Text (Get-Content -LiteralPath $Path -Raw)
        if (-not [string]::Equals($existing, $normalizedContent, [System.StringComparison]::Ordinal))
        {
            throw "$DisplayPath is stale. Run $GeneratorCommand."
        }

        return
    }

    [System.IO.File]::WriteAllText(
        $Path,
        $normalizedContent,
        [System.Text.UTF8Encoding]::new($false))
}
