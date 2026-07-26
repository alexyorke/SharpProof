[CmdletBinding()]
param(
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryDocumentPrefix =
    'https://github.com/alexyorke/SharpProof/blob/main/'
$maintainedDocuments = @(
    'README.md',
    'SEMANTICS.md',
    'docs\README.md',
    'docs\architecture.md',
    'docs\coverage-and-limits.md',
    'docs\analysis-limits.md',
    'docs\diagnostic-examples.md',
    'docs\unknown-reasons.md',
    'docs\native-smt-packaging.md',
    'docs\smt-lifecycle.md',
    'docs\soundness-notes\2026-07-25-api-spec-result-domains.md',
    'docs\soundness-notes\2026-07-25-hardening.md',
    'eng\acceptance\README.md',
    'SharpProof.Gates\README.md',
    'SharpProof.Gates\Corpus\README.md'
)

function Get-RepositoryPath {
    param([Parameter(Mandatory)][string]$RelativePath)

    return [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot $RelativePath))
}

function Get-RequiredText {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Get-RepositoryPath $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required documentation source is missing: $RelativePath"
    }
    return Get-Content -LiteralPath $path -Raw
}

function Assert-LfUtf8Document {
    param([Parameter(Mandatory)][string]$RelativePath)

    $path = Get-RepositoryPath $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required maintained document is missing: $RelativePath"
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF) {
        throw "Maintained document must not use a UTF-8 BOM: $RelativePath"
    }
    $content = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($content.Contains("`r", [StringComparison]::Ordinal)) {
        throw "Maintained document must use LF line endings: $RelativePath"
    }
}

function Get-MarkdownAnchors {
    param([Parameter(Mandatory)][string]$Content)

    $anchors = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($match in [regex]::Matches(
        $Content,
        '<a\s+id\s*=\s*["''](?<id>[^"'']+)["'']\s*>\s*</a>',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        [void]$anchors.Add($match.Groups['id'].Value)
    }

    $duplicates = @{}
    foreach ($match in [regex]::Matches(
        $Content,
        '^(?:#{1,6})[ \t]+(?<heading>.+?)[ \t]*#*[ \t]*$',
        [System.Text.RegularExpressions.RegexOptions]::Multiline)) {
        $heading = $match.Groups['heading'].Value
        $heading = [regex]::Replace($heading, '<[^>]+>', '')
        $heading = [regex]::Replace($heading, '\[([^\]]+)\]\([^)]+\)', '$1')
        $heading = $heading.Replace('`', '')
        $slug = $heading.ToLowerInvariant()
        $slug = [regex]::Replace($slug, '[^\p{L}\p{Nd}\s-]', '')
        $slug = [regex]::Replace($slug.Trim(), '\s+', '-')
        if ($slug.Length -eq 0) {
            continue
        }
        $baseSlug = $slug
        if ($duplicates.ContainsKey($baseSlug)) {
            $duplicates[$baseSlug]++
            $slug = $baseSlug + '-' + $duplicates[$baseSlug]
        }
        else {
            $duplicates[$baseSlug] = 0
        }
        [void]$anchors.Add($slug)
    }
    return $anchors
}

function Assert-MarkdownLinks {
    param([Parameter(Mandatory)][string]$RelativePath)

    $sourcePath = Get-RepositoryPath $RelativePath
    $content = Get-Content -LiteralPath $sourcePath -Raw
    foreach ($match in [regex]::Matches(
        $content,
        '(?<!!)\[[^\]]+\]\((?<target><[^>]+>|[^)\s]+)(?:\s+"[^"]*")?\)')) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        if ($target.StartsWith(
            $repositoryDocumentPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            $target = $target.Substring($repositoryDocumentPrefix.Length)
        }
        elseif ($target -match '^(?:https?://|mailto:)') {
            continue
        }

        $parts = $target.Split([char]'#', 2)
        $targetPath = if ($parts[0].Length -eq 0) {
            $sourcePath
        }
        else {
            $decodedPath = [Uri]::UnescapeDataString($parts[0])
            $combinedPath = Join-Path `
                -Path (Split-Path $sourcePath -Parent) `
                -ChildPath $decodedPath
            [System.IO.Path]::GetFullPath($combinedPath)
        }
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            throw "Broken local Markdown link in ${RelativePath}: $target"
        }

        if ($parts.Length -eq 2 -and $parts[1].Length -ne 0) {
            $targetContent = Get-Content -LiteralPath $targetPath -Raw
            $anchors = Get-MarkdownAnchors $targetContent
            $fragment = [Uri]::UnescapeDataString($parts[1])
            if (-not $anchors.Contains($fragment)) {
                throw "Broken Markdown anchor in ${RelativePath}: $target"
            }
        }
    }
}

function Get-EnumMembers {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$EnumName
    )

    $match = [regex]::Match(
        $Content,
        "(?:public|internal)\s+enum\s+$EnumName\s*\{(?<body>.*?)\}",
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) {
        throw "Could not find enum '$EnumName' in a documentation source."
    }
    return @(
        $match.Groups['body'].Value.Split([char]',') |
            ForEach-Object {
                $name = ($_ -split '=')[0].Trim()
                if ($name.Length -ne 0) {
                    $name
                }
            }
    )
}

foreach ($relativePath in $maintainedDocuments) {
    Assert-LfUtf8Document $relativePath
    Assert-MarkdownLinks $relativePath
}

$releaseXml = [xml](Get-RequiredText 'SharpProof.Release.props')
$versionPrefix = $releaseXml.SelectSingleNode(
    '//SharpProofVersionPrefix').InnerText
$versionExpression = $releaseXml.SelectSingleNode(
    '//SharpProofPackageVersion').InnerText
$packageVersion = $versionExpression.Replace(
    '$(SharpProofVersionPrefix)',
    $versionPrefix)

$readme = Get-RequiredText 'README.md'
$requiredReadmeText = @(
    $packageVersion,
    'sharpproof_mode',
    'SharpProofVerify=true',
    'SharpProof.Worker',
    'SP0027',
    'Proven',
    'Refuted',
    'Unknown',
    'SHARPPROOF_CONTRACTS',
    'Windows x64',
    'docs/README.md'
)
$forbiddenReadmeText = @(
    'Deep Ensures'
)
foreach ($required in $requiredReadmeText) {
    if (-not $readme.Contains($required, [StringComparison]::Ordinal)) {
        throw "README.md is missing required current-product text: $required"
    }
}
foreach ($forbidden in $forbiddenReadmeText) {
    if ($readme.Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "README.md still advertises overstated text: $forbidden"
    }
}

$configurationSource = Get-RequiredText (
    'SharpProof.Analyzer\Configuration\AnalyzerConfigurationOptionRegistry.cs')
$configurationMatch = [regex]::Match(
    $configurationSource,
    '"sharpproof_mode"\s*,\s*\[(?<values>[^\]]+)\]',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $configurationMatch.Success) {
    throw 'Could not derive sharpproof_mode values from the analyzer registry.'
}
$modes = @(
    [regex]::Matches(
        $configurationMatch.Groups['values'].Value,
        '"(?<value>[^"]+)"') |
        ForEach-Object { $_.Groups['value'].Value }
)
foreach ($mode in $modes) {
    $modeMarker = '`' + $mode + '`'
    if (-not $readme.Contains($modeMarker, [StringComparison]::Ordinal)) {
        throw "README.md is missing analyzer mode '$mode'."
    }
}

$diagnosticCatalog = Get-RequiredText 'docs\diagnostic-examples.md'
$descriptorSources = @(
    'SharpProof.Analyzer\GeneratedDiagnosticDescriptors.cs',
    'SharpProof.ContractForGenerator\GeneratedDiagnosticDescriptors.cs'
)
foreach ($descriptorSource in $descriptorSources) {
    $content = Get-RequiredText $descriptorSource
    $ids = [regex]::Matches(
        $content,
        '"(?<id>SP(?:CF)?\d{4})"') |
        ForEach-Object { $_.Groups['id'].Value } |
        Sort-Object -Unique
    foreach ($id in $ids) {
        $anchor = '<a id="' + $id.ToLowerInvariant() + '"></a>'
        if (-not $diagnosticCatalog.Contains(
            $anchor,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Diagnostic catalog is missing '$id' and its help anchor."
        }
    }
}

$architecture = Get-RequiredText 'docs\architecture.md'
$architectureAnchors = Get-MarkdownAnchors $architecture
if (-not $architectureAnchors.Contains('mechanized-boundaries')) {
    throw 'Architecture documentation is missing the SPMETA help anchor.'
}

$coverage = Get-RequiredText 'docs\coverage-and-limits.md'
$apiSpecSource = Get-RequiredText 'SharpProof.Specs\ApiSpecTable.cs'
$apiSpecIds = [regex]::Matches(
    $apiSpecSource,
    '"(?<id>bcl\.[a-z0-9.-]+)"') |
    ForEach-Object { $_.Groups['id'].Value } |
    Sort-Object -Unique
foreach ($apiSpecId in $apiSpecIds) {
    if (-not $coverage.Contains($apiSpecId, [StringComparison]::Ordinal)) {
        throw "Coverage documentation is missing API spec '$apiSpecId'."
    }
}

$limitReference = Get-RequiredText 'docs\analysis-limits.md'
$packageProps = [xml](Get-RequiredText (
    'SharpProof.Package\buildTransitive\SharpProof.props'))
$workerPropertyNames = @(
    $packageProps.SelectNodes(
        '//PropertyGroup/*[starts-with(local-name(), "SharpProofVerify")]') |
        ForEach-Object { $_.Name } |
        Sort-Object -Unique
)
foreach ($propertyName in $workerPropertyNames) {
    if (-not $limitReference.Contains(
        $propertyName,
        [StringComparison]::Ordinal)) {
        throw "Analysis-limit reference is missing property '$propertyName'."
    }
}

$callableVerifier = Get-RequiredText 'SharpProof.Worker\CallableVerifier.cs'
$fixedBodyBounds = [ordered]@{
    maximumBodyBlocks = 'Reachable CFG blocks'
    maximumBodyPaths = 'Normal-return paths'
    maximumExecutionStates = 'Symbolic execution states'
}
foreach ($entry in $fixedBodyBounds.GetEnumerator()) {
    $boundMatch = [regex]::Match(
        $callableVerifier,
        'const\s+int\s+' + $entry.Key + '\s*=\s*(?<value>\d+)\s*;')
    if (-not $boundMatch.Success) {
        throw "Could not derive worker body bound '$($entry.Key)'."
    }
    $displayValue = [int]::Parse(
        $boundMatch.Groups['value'].Value,
        [Globalization.CultureInfo]::InvariantCulture).ToString(
            'N0',
            [Globalization.CultureInfo]::InvariantCulture)
    $expectedRow = '| ' + $entry.Value + ' | ' + $displayValue + ' |'
    if (-not $limitReference.Contains(
        $expectedRow,
        [StringComparison]::Ordinal)) {
        throw (
            "Analysis-limit reference is missing code-derived body bound: " +
            $expectedRow)
    }
}

$unknownReference = Get-RequiredText 'docs\unknown-reasons.md'
$protocolSource = Get-RequiredText (
    'SharpProof.Worker.Protocol\ProtocolModel.cs')
$workerReasons = Get-EnumMembers $protocolSource 'WorkerVerificationReason'
foreach ($reason in $workerReasons) {
    if (-not $unknownReference.Contains(
        $reason,
        [StringComparison]::Ordinal)) {
        throw "Unknown-reason reference is missing worker reason '$reason'."
    }
}

if ($Verify) {
    Write-Host (
        "SharpProof documentation matches code-derived version, modes, " +
        'diagnostics, API specs, worker options, reasons, links, and anchors.')
}
else {
    Write-Host 'SharpProof documentation validation passed.'
}
