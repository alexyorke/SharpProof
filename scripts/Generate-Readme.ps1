[CmdletBinding()]
param(
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'GeneratedFileHelpers.ps1')

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$pages = @(
    @{
        Name = "README"
        TemplatePath = "README.source.md"
        OutputPath = "README.md"
        ManifestPath = "docs/readme-examples/readme-examples.json"
        Marker = "<!-- README_EXAMPLES -->"
        Header = "<!-- Generated from README.source.md by scripts/Generate-Readme.ps1. -->"
    },
    @{
        Name = "diagnostic examples"
        TemplatePath = "docs/diagnostic-examples.source.md"
        OutputPath = "docs/diagnostic-examples.md"
        ManifestPath = "docs/readme-examples/diagnostic-examples.json"
        Marker = "<!-- DIAGNOSTIC_EXAMPLES -->"
        Header = "<!-- Generated from docs/diagnostic-examples.source.md by scripts/Generate-Readme.ps1. -->"
    },
    @{
        Name = "symbolic query examples"
        TemplatePath = "docs/symbolic-query-examples.source.md"
        OutputPath = "docs/symbolic-query-examples.md"
        ManifestPath = "docs/readme-examples/symbolic-examples.json"
        Marker = "<!-- SYMBOLIC_QUERY_EXAMPLES -->"
        Header = "<!-- Generated from docs/symbolic-query-examples.source.md by scripts/Generate-Readme.ps1. -->"
    }
)

function Get-ReadmeExampleTests {
    param([string]$Root)

    $map = @{}
    $files = Get-ChildItem -Path (Join-Path $Root "SharpProof.Test"), (Join-Path $Root "SharpProof.ToolingTest") -Filter *.cs -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    $pattern = '(?ms)(?<attributes>(?:^[ \t]*\[[^\r\n]+\][ \t]*\r?\n)+)[ \t]*public\s+(?:async\s+)?(?:Task(?:<[^>\r\n]+>)?|ValueTask(?:<[^>\r\n]+>)?|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)'
    foreach ($file in $files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($match in [System.Text.RegularExpressions.Regex]::Matches($content, $pattern)) {
            $attributes = $match.Groups['attributes'].Value
            $exampleMatch = [System.Text.RegularExpressions.Regex]::Match(
                $attributes,
                'ReadmeExample\("(?<id>[^"]+)"\)')
            $testMatch = [System.Text.RegularExpressions.Regex]::IsMatch(
                $attributes,
                '(?:^|[,\[\s])Test(?:Attribute)?(?:\s*[,\]\(])')
            if (-not $exampleMatch.Success -or -not $testMatch) {
                continue
            }

            $id = $exampleMatch.Groups['id'].Value
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
            throw "Generated example '$($example.Id)' is missing a [ReadmeExample] test."
        }

        $sourceFile = Join-Path $Root $example.SourcePath
        $outputFile = Join-Path $Root $example.OutputPath
        if (-not (Test-Path -LiteralPath $sourceFile)) {
            throw "Missing generated example source file: $($example.SourcePath)"
        }

        if (-not (Test-Path -LiteralPath $outputFile)) {
            throw "Missing generated example output file: $($example.OutputPath)"
        }

        $sourceText = ConvertTo-SharpProofGeneratedText -Text (Get-Content -LiteralPath $sourceFile -Raw)
        $outputText = ConvertTo-SharpProofGeneratedText -Text (Get-Content -LiteralPath $outputFile -Raw)
        $diagnosticIds = @()
        if ($example.PSObject.Properties.Name -contains "DiagnosticId" -and
            -not [string]::IsNullOrWhiteSpace($example.DiagnosticId)) {
            $diagnosticIds += $example.DiagnosticId
        }
        if ($example.PSObject.Properties.Name -contains "DiagnosticIds") {
            $diagnosticIds += @($example.DiagnosticIds) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        }
        foreach ($diagnosticId in $diagnosticIds | Sort-Object -Unique) {
            [void]$builder.AppendLine(('<a id="{0}"></a>' -f $diagnosticId.ToLowerInvariant()))
            [void]$builder.AppendLine()
        }
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

function Get-ManifestExamples {
    param([string]$ManifestFile)

    if (-not (Test-Path -LiteralPath $ManifestFile)) {
        throw "Missing generated example manifest: $ManifestFile"
    }

    $examples = Get-Content -LiteralPath $ManifestFile -Raw | ConvertFrom-Json
    if ($null -eq $examples) {
        throw "Manifest '$ManifestFile' is empty."
    }

    return @($examples)
}

function Get-GeneratedPage {
    param(
        [hashtable]$Page,
        [hashtable]$Tests,
        [string]$Root
    )

    $templateFile = Join-Path $Root $Page.TemplatePath
    if (-not (Test-Path -LiteralPath $templateFile)) {
        throw "Missing template file: $($Page.TemplatePath)"
    }

    $template = ConvertTo-SharpProofGeneratedText -Text (Get-Content -LiteralPath $templateFile -Raw)
    if (-not $template.Contains($Page.Marker)) {
        throw "Template '$($Page.TemplatePath)' is missing marker '$($Page.Marker)'."
    }

    $examples = Get-ManifestExamples -ManifestFile (Join-Path $Root $Page.ManifestPath)
    $generatedExamples = Convert-ToGeneratedExamplesMarkdown -Examples $examples -Tests $Tests -Root $Root
    $content = $Page.Header + "`n`n" + $template.Replace($Page.Marker, $generatedExamples)
    return @{
        Examples = $examples
        Content = ConvertTo-SharpProofGeneratedText -Text $content
    }
}

$tests = Get-ReadmeExampleTests -Root $repositoryRoot
$generatedPages = @()
$allExampleIds = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::Ordinal)

foreach ($page in $pages) {
    $generated = Get-GeneratedPage -Page $page -Tests $tests -Root $repositoryRoot
    foreach ($example in $generated.Examples) {
        [void]$allExampleIds.Add($example.Id)
    }

    $generatedPages += @{
        Page = $page
        Content = $generated.Content
    }
}

$missingExamples = @($tests.Keys | Where-Object { -not $allExampleIds.Contains($_) } | Sort-Object)
if ($missingExamples.Count -ne 0) {
    throw "One or more [ReadmeExample] ids do not have manifest entries: $($missingExamples -join ', ')"
}

foreach ($generatedPage in $generatedPages) {
    $outputFile = Join-Path $repositoryRoot $generatedPage.Page.OutputPath
    Update-SharpProofGeneratedFile `
        -Path $outputFile `
        -Content $generatedPage.Content `
        -DisplayPath $generatedPage.Page.OutputPath `
        -GeneratorCommand '.\scripts\Generate-Readme.ps1' `
        -Verify:$Verify
}

if ($Verify) {
    Write-Host "Generated example pages are up to date."
    return
}

foreach ($generatedPage in $generatedPages) {
    Write-Host ("Regenerated {0}." -f $generatedPage.Page.OutputPath)
}
