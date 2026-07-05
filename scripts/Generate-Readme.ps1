[CmdletBinding()]
param(
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repositoryRoot "README.source.md"
$readmePath = Join-Path $repositoryRoot "README.md"
$manifestPath = Join-Path $repositoryRoot "docs/readme-examples/examples.json"
$marker = "<!-- README_EXAMPLES -->"

function Normalize-Text {
    param([string]$Text)

    $normalized = $Text.Replace("`r`n", "`n")
    return $normalized.TrimEnd("`r`n".ToCharArray()) + "`n"
}

function Get-ReadmeExampleTests {
    param([string]$Root)

    $map = @{}
    $files = Get-ChildItem -Path (Join-Path $Root "SharpProof.Test"), (Join-Path $Root "SharpProof.ToolingTest") -Filter *.cs -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    $pattern = '\[ReadmeExample\("(?<id>[^"]+)"\)\]\s*\[Test\]\s*public\s+(?:async\s+Task|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)'
    foreach ($file in $files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [System.Text.RegularExpressions.Regex]::Matches($content, $pattern)) {
            $id = $match.Groups["id"].Value
            $name = $match.Groups["name"].Value
            if ($map.ContainsKey($id)) {
                throw "Duplicate [ReadmeExample] id '$id' found in '$($file.FullName)'."
            }

            $map[$id] = "{0}.{1}" -f [System.IO.Path]::GetFileNameWithoutExtension($file.Name), $name
        }
    }

    return $map
}

function Convert-ToGeneratedExamplesMarkdown {
    param(
        [object[]]$Examples,
        [hashtable]$Tests,
        [string]$Root
    )

    $builder = New-Object System.Text.StringBuilder
    foreach ($example in $Examples) {
        if (-not $Tests.ContainsKey($example.Id)) {
            throw "README example '$($example.Id)' is missing a [ReadmeExample] test."
        }

        $sourceFile = Join-Path $Root $example.SourcePath
        $outputFile = Join-Path $Root $example.OutputPath
        if (-not (Test-Path -LiteralPath $sourceFile)) {
            throw "Missing README example source file: $($example.SourcePath)"
        }

        if (-not (Test-Path -LiteralPath $outputFile)) {
            throw "Missing README example output file: $($example.OutputPath)"
        }

        $sourceText = Normalize-Text (Get-Content -LiteralPath $sourceFile -Raw)
        $outputText = Normalize-Text (Get-Content -LiteralPath $outputFile -Raw)
        [void]$builder.AppendLine("### $($example.Title)")
        [void]$builder.AppendLine()
        [void]$builder.AppendLine($example.Summary)
        [void]$builder.AppendLine()
        [void]$builder.AppendLine("Backed by test: ``$($Tests[$example.Id])``.")
        [void]$builder.AppendLine()
        if ($example.PSObject.Properties.Name -contains "Command" -and -not [string]::IsNullOrWhiteSpace($example.Command)) {
            [void]$builder.AppendLine("Command:")
            [void]$builder.AppendLine()
            [void]$builder.AppendLine('```powershell')
            [void]$builder.AppendLine($example.Command)
            [void]$builder.AppendLine('```')
            [void]$builder.AppendLine()
        }

        [void]$builder.AppendLine(('Source (`{0}`):' -f $example.SourcePath))
        [void]$builder.AppendLine()
        [void]$builder.AppendLine(('```{0}' -f $example.Language))
        [void]$builder.Append($sourceText)
        [void]$builder.AppendLine('```')
        [void]$builder.AppendLine()
        [void]$builder.AppendLine(('{0}:' -f $example.OutputLabel))
        [void]$builder.AppendLine()
        [void]$builder.AppendLine(('```{0}' -f $example.OutputLanguage))
        [void]$builder.Append($outputText)
        [void]$builder.AppendLine('```')
        [void]$builder.AppendLine()
    }

    return $builder.ToString().TrimEnd("`r`n".ToCharArray())
}

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Missing README source file: $sourcePath"
}

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing README example manifest: $manifestPath"
}

$template = Normalize-Text (Get-Content -LiteralPath $sourcePath -Raw)
if (-not $template.Contains($marker)) {
    throw "README source is missing marker '$marker'."
}

$examples = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$tests = Get-ReadmeExampleTests -Root $repositoryRoot
$testIds = @($tests.Keys | Sort-Object)
$exampleIds = @($examples.Id | Sort-Object)
$missingExamples = @($testIds | Where-Object { $_ -notin $exampleIds })
if ($missingExamples.Count -ne 0) {
    throw "One or more [ReadmeExample] ids do not have manifest entries: $($missingExamples -join ', ')"
}

$generatedExamples = Convert-ToGeneratedExamplesMarkdown -Examples $examples -Tests $tests -Root $repositoryRoot
$generatedReadme = "<!-- Generated from README.source.md by scripts/Generate-Readme.ps1. -->`n`n" +
    $template.Replace($marker, $generatedExamples)
$generatedReadme = Normalize-Text $generatedReadme

if ($Verify) {
    if (-not (Test-Path -LiteralPath $readmePath)) {
        throw "README.md is missing. Run .\scripts\Generate-Readme.ps1."
    }

    $existing = Normalize-Text (Get-Content -LiteralPath $readmePath -Raw)
    if (-not [string]::Equals($existing, $generatedReadme, [System.StringComparison]::Ordinal)) {
        throw "README.md is stale. Run .\scripts\Generate-Readme.ps1."
    }

    Write-Host "README.md is up to date."
    return
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($readmePath, $generatedReadme, $utf8NoBom)
Write-Host "Regenerated README.md from README.source.md."
