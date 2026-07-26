function ConvertTo-SharpProofGeneratedText
{
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)

    $normalized = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    return $normalized.TrimEnd("`n".ToCharArray()) + "`n"
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

    $normalizedContent = ConvertTo-SharpProofGeneratedText -Text $Content
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
