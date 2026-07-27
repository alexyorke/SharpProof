[CmdletBinding()]
param(
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryDefaultBranch = 'master'
$currentMaintainedDocuments = @(
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
    'eng\acceptance\README.md',
    'SharpProof.Gates\README.md',
    'SharpProof.Gates\Corpus\README.md'
)
$datedEvidenceDocuments = @(
    'docs\soundness-notes\2026-07-25-api-spec-result-domains.md',
    'docs\soundness-notes\2026-07-25-hardening.md'
)
$maintainedDocuments = @(
    $currentMaintainedDocuments + $datedEvidenceDocuments
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

function Assert-RepositoryDocumentLink {
    param(
        [Parameter(Mandatory)][string]$SourceRelativePath,
        [Parameter(Mandatory)][string]$Target
    )

    if (-not $Target.StartsWith(
            $repositoryDocumentPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            "Repository documentation link in $SourceRelativePath must use " +
            "the '$repositoryDefaultBranch' branch: $Target")
    }

    $relativeTarget = $Target.Substring($repositoryDocumentPrefix.Length)
    $parts = $relativeTarget.Split([char]'#', 2)
    if ($parts[0].Length -eq 0) {
        throw "Repository documentation link has no file path in ${SourceRelativePath}: $Target"
    }

    $targetPath = Get-RepositoryPath (
        [Uri]::UnescapeDataString($parts[0]))
    if (-not $targetPath.StartsWith(
            $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
        throw "Broken repository documentation link in ${SourceRelativePath}: $Target"
    }

    if ($parts.Length -eq 2 -and $parts[1].Length -ne 0) {
        $targetContent = Get-Content -LiteralPath $targetPath -Raw
        $anchors = Get-MarkdownAnchors $targetContent
        $fragment = [Uri]::UnescapeDataString($parts[1])
        if (-not $anchors.Contains($fragment)) {
            throw "Broken repository documentation anchor in ${SourceRelativePath}: $Target"
        }
    }
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
            "$repositoryUrl/blob/",
            [StringComparison]::OrdinalIgnoreCase)) {
            Assert-RepositoryDocumentLink $RelativePath $target
            continue
        }
        if ($target -match '^(?:https?://|mailto:)') {
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

function Assert-RepositoryLinksInSource {
    param([Parameter(Mandatory)][string]$RelativePath)

    $content = Get-RequiredText $RelativePath
    $pattern =
        [regex]::Escape("$repositoryUrl/blob/") + '[^"''\s)]+'
    foreach ($match in [regex]::Matches($content, $pattern)) {
        Assert-RepositoryDocumentLink $RelativePath $match.Value
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

$releaseXml = [xml](Get-RequiredText 'SharpProof.Release.props')
$repositoryUrl = $releaseXml.SelectSingleNode(
    '//SharpProofProjectUrl').InnerText.TrimEnd('/')
$repositoryDocumentPrefix =
    "$repositoryUrl/blob/$repositoryDefaultBranch/"

foreach ($relativePath in $maintainedDocuments) {
    Assert-LfUtf8Document $relativePath
    Assert-MarkdownLinks $relativePath
    $maintainedText = Get-RequiredText $relativePath
    foreach ($obsoleteWorkerTerm in @(
            'WorkerVerificationStatus',
            'WorkerVerificationReason',
            'DeepEnsures')) {
        if ($maintainedText.Contains(
                $obsoleteWorkerTerm,
                [StringComparison]::Ordinal)) {
            throw (
                "Maintained documentation still uses obsolete worker term " +
                "'$obsoleteWorkerTerm': $relativePath")
        }
    }
}
foreach ($relativePath in @(
        'SharpProof.Analyzer\GeneratedDiagnosticDescriptors.cs',
        'SharpProof.Meta.Analyzers\MetaDiagnosticDescriptors.cs')) {
    Assert-RepositoryLinksInSource $relativePath
}

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
    'SharpProofProfile',
    'SharpProofFeatures',
    'SharpProofVerifyPolicy',
    'SharpProofAssumptionPolicy',
    'SharpProofVerify=true',
    'SharpProof.Worker',
    'SP0047',
    'SP0048',
    'SP0027',
    'Proven',
    'Refuted',
    'Unknown',
    'SHARPPROOF_CONTRACTS',
    'Windows x64',
    'compiler artifact',
    'SARIF',
    'docs/README.md'
)
$forbiddenReadmeText = @(
    'Deep Ensures',
    'DeepEnsures',
    'WorkerVerificationStatus',
    'WorkerVerificationReason'
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
$configurationOptions = [regex]::Matches(
    $configurationSource,
    'new\("(?<key>sharpproof_[^"]+)"\s*,\s*\[(?<values>[^\]]+)\]',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if ($configurationOptions.Count -eq 0) {
    throw 'Could not derive analyzer options from the analyzer registry.'
}
foreach ($option in $configurationOptions) {
    $key = $option.Groups['key'].Value
    if (-not $readme.Contains($key, [StringComparison]::Ordinal)) {
        throw "README.md is missing analyzer option '$key'."
    }
    $values = [regex]::Matches(
        $option.Groups['values'].Value,
        '"(?<value>[^"]+)"') |
        ForEach-Object { $_.Groups['value'].Value }
    foreach ($value in $values) {
        $valueMarker = '`' + $value + '`'
        if (-not $readme.Contains($valueMarker, [StringComparison]::Ordinal)) {
            throw "README.md is missing '$key' value '$value'."
        }
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
foreach ($launcherDiagnostic in @('SP0047', 'SP0048')) {
    $anchor = '<a id="' + $launcherDiagnostic.ToLowerInvariant() + '"></a>'
    if (-not $diagnosticCatalog.Contains(
            $anchor,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            "Diagnostic catalog is missing launcher diagnostic " +
            "'$launcherDiagnostic' and its help anchor.")
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
    'SharpProofProfile',
    'SharpProofFeatures',
    'SharpProofVerifyPolicy',
    'SharpProofAssumptionPolicy',
    'SharpProofMode',
    'SharpProofVerify'
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
$compilerCallableLowerer = Get-RequiredText (
    'SharpProof.Analyzer\CompilerArtifact\CompilerCallableLowerer.cs')
$blockBound = [regex]::Match(
    $compilerCallableLowerer,
    'const\s+int\s+MaximumBodyBlocks\s*=\s*(?<value>\d+)\s*;')
$executorBounds = [regex]::Match(
    $callableVerifier,
    'prepared\.ParameterBindings,\s*(?<paths>\d+),\s*(?<states>\d+)\s*\)',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $blockBound.Success -or -not $executorBounds.Success) {
    throw 'Could not derive compiler/worker body execution bounds.'
}
$fixedBodyBounds = @(
    [pscustomobject]@{
        Label = 'Reachable CFG blocks'
        Value = $blockBound.Groups['value'].Value
    },
    [pscustomobject]@{
        Label = 'Normal-return paths'
        Value = $executorBounds.Groups['paths'].Value
    },
    [pscustomobject]@{
        Label = 'Symbolic execution states'
        Value = $executorBounds.Groups['states'].Value
    }
)
foreach ($entry in $fixedBodyBounds) {
    $displayValue = [int]::Parse(
        $entry.Value,
        [Globalization.CultureInfo]::InvariantCulture).ToString(
            'N0',
            [Globalization.CultureInfo]::InvariantCulture)
    $expectedRow = '| ' + $entry.Label + ' | ' + $displayValue + ' |'
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
foreach ($enumName in @(
        'WorkerFeatureSet',
        'WorkerVerifyPolicy',
        'WorkerAssumptionPolicy',
        'WorkerRunStatus',
        'WorkerRunFailureReason',
        'WorkerCallableCoverage',
        'WorkerCallableCoverageReason',
        'WorkerClaimOutcome',
        'WorkerClaimReason',
        'WorkerCacheStatus')) {
    foreach ($value in Get-EnumMembers $protocolSource $enumName) {
        if (-not $unknownReference.Contains(
                $value,
                [StringComparison]::Ordinal)) {
            throw (
                "Unknown-reason reference is missing '$enumName' value " +
                "'$value'.")
        }
    }
}

$protocolVersion = [regex]::Match(
    $protocolSource,
    'WorkerProtocolVersions\s*\{\s*public const string Current = "(?<value>\d+)"')
$cacheVersion = [regex]::Match(
    $protocolSource,
    'WorkerCacheVersions\s*\{\s*public const int Current = (?<value>\d+)')
$manifestVersion = [regex]::Match(
    $protocolSource,
    'WorkerManifestVersions\s*\{\s*public const int Current = (?<value>\d+)')
$compilerArtifactSource = Get-RequiredText (
    'SharpProof.CompilerArtifact\CompilerManifestArtifact.cs')
$compilerArtifactVersion = [regex]::Match(
    $compilerArtifactSource,
    'CompilerManifestArtifactVersions\s*\{[\s\S]*?\bCurrent\s*=\s*(?<value>\d+)\s*;')
if (-not $protocolVersion.Success -or
    -not $cacheVersion.Success -or
    -not $manifestVersion.Success -or
    -not $compilerArtifactVersion.Success) {
    throw (
        'Could not derive worker protocol, cache, manifest, and compiler-' +
        'artifact versions.')
}

$derivedVersions = [ordered]@{
    Protocol = $protocolVersion.Groups['value'].Value
    Cache = $cacheVersion.Groups['value'].Value
    Manifest = $manifestVersion.Groups['value'].Value
    CompilerArtifact = $compilerArtifactVersion.Groups['value'].Value
}
$acceptanceContract = Get-RequiredText 'eng\acceptance\contract.json' |
    ConvertFrom-Json
$contractVersions = [ordered]@{
    Protocol = $acceptanceContract.worker.protocolVersion
    Cache = $acceptanceContract.cache.schemaVersion
    Manifest = $acceptanceContract.worker.manifestSchemaVersion
    CompilerArtifact = $acceptanceContract.worker.compilerArtifactSchemaVersion
}
foreach ($name in $derivedVersions.Keys) {
    $expected = $derivedVersions[$name]
    $actual = [string]$contractVersions[$name]
    if ($actual -ne $expected) {
        throw (
            "Acceptance contract $name version '$actual' does not match " +
            "the code-derived version '$expected'.")
    }
}

foreach ($expected in @(
        "protocol version $($derivedVersions.Protocol)",
        "cache schema version $($derivedVersions.Cache)",
        "manifest schema version $($derivedVersions.Manifest)",
        "compiler artifact schema version $($derivedVersions.CompilerArtifact)")) {
    if (-not $readme.Contains($expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "README.md is missing code-derived worker text: $expected"
    }
}

$versionMentionRules = @(
    [pscustomobject]@{
        Name = 'protocol'
        Pattern = '\b(?:protocol\s+version\s+|protocol-v)(?<value>\d+)\b'
        Expected = $derivedVersions.Protocol
    },
    [pscustomobject]@{
        Name = 'cache schema'
        Pattern = '\bcache\s+schema\s+version\s+(?<value>\d+)\b'
        Expected = $derivedVersions.Cache
    },
    [pscustomobject]@{
        Name = 'claim-manifest schema'
        Pattern = '(?<!compiler )(?<!compiler-)\b(?:claim-)?manifest\s+schema\s+version\s+(?<value>\d+)\b'
        Expected = $derivedVersions.Manifest
    },
    [pscustomobject]@{
        Name = 'compiler-artifact schema'
        Pattern = '\bcompiler(?:\s+artifact|-manifest(?:\s+attestation)?)\s+schema\s+version\s+(?<value>\d+)\b'
        Expected = $derivedVersions.CompilerArtifact
    },
    [pscustomobject]@{
        Name = 'compiler-artifact schema'
        Pattern = '\bschema-(?<value>\d+)\s+compiler(?:\s+artifact|\s+evidence|\s+snapshot|-manifest)\b'
        Expected = $derivedVersions.CompilerArtifact
    }
)
foreach ($relativePath in $currentMaintainedDocuments) {
    $content = Get-RequiredText $relativePath
    foreach ($rule in $versionMentionRules) {
        foreach ($match in [regex]::Matches(
                $content,
                $rule.Pattern,
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            if ($match.Groups['value'].Value -ne $rule.Expected) {
                throw (
                    "Current documentation has stale $($rule.Name) version " +
                    "'$($match.Groups['value'].Value)' in $relativePath; " +
                    "code declares '$($rule.Expected)'.")
            }
        }
    }
}

if ($Verify) {
    Write-Host (
        "SharpProof documentation matches code-derived package, protocol, " +
        'cache, manifest, and compiler-artifact versions, acceptance-contract ' +
        'versions, configuration, diagnostics, API specs, worker options, ' +
        'protocol enums, links, and anchors.')
}
else {
    Write-Host 'SharpProof documentation validation passed.'
}
