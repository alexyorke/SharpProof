[CmdletBinding()]
param(
    [string]$TextOverrideRelativePath = '',

    [string]$TextOverridePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Get-SharpProofReleaseVersion.ps1')
$hasTextOverrideRelativePath =
    -not [string]::IsNullOrWhiteSpace($TextOverrideRelativePath)
$hasTextOverridePath = -not [string]::IsNullOrWhiteSpace($TextOverridePath)
if ($hasTextOverrideRelativePath -ne $hasTextOverridePath) {
    throw (
        'TextOverrideRelativePath and TextOverridePath must be supplied ' +
        'together.')
}
$normalizedTextOverrideRelativePath = if ($hasTextOverrideRelativePath) {
    $TextOverrideRelativePath.Replace('\', '/')
}
else {
    ''
}
$resolvedTextOverridePath = if ($hasTextOverridePath) {
    (Resolve-Path -LiteralPath $TextOverridePath -ErrorAction Stop).Path
}
else {
    ''
}
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$repositoryDefaultBranch = 'master'
$currentMaintainedDocuments = @(
    'README.md',
    'SEMANTICS.md',
    'BUGS.md',
    'eng\agent-notes\status.md',
    'docs\README.md',
    'docs\getting-started.md',
    'docs\architecture.md',
    'docs\coverage-and-limits.md',
    'docs\container-development.md',
    'docs\analysis-limits.md',
    'docs\public-api.md',
    'docs\diagnostic-examples.md',
    'docs\unknown-reasons.md',
    'docs\native-smt-packaging.md',
    'docs\preview-support.md',
    'docs\release-constants.md',
    'docs\smt-lifecycle.md',
    'samples\README.md',
    'eng\acceptance\README.md',
    'SharpProof.Gates\README.md',
    'SharpProof.Gates\Corpus\README.md'
)
$datedEvidenceDocuments = @(
    'docs\code-usefulness-audit.md',
    'docs\soundness-notes\2026-08-08-relational-interprocedural-verification.md',
    'docs\soundness-notes\2026-07-25-api-spec-result-domains.md',
    'docs\soundness-notes\2026-07-25-hardening.md',
    'docs\soundness-notes\2026-07-29-formatting-neutral-source-metrics.md',
    'docs\soundness-notes\2026-07-29-semantic-precondition-vacuity.md',
    'docs\soundness-notes\2026-07-30-allocation-effect-replay.md'
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
    if ($hasTextOverrideRelativePath -and
        $RelativePath.Replace('\', '/') -ceq
            $normalizedTextOverrideRelativePath) {
        return Get-Content -LiteralPath $resolvedTextOverridePath -Raw
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

    try {
        $targetPath = Resolve-SharpProofContainedPath `
            -Root $repositoryRoot `
            -Path ([Uri]::UnescapeDataString($parts[0])) `
            -ParameterName 'Repository documentation link'
    }
    catch {
        throw "Broken repository documentation link in ${SourceRelativePath}: $Target"
    }
    if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
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

function Assert-BacktickedRepositoryPaths {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Content
    )

    if ($datedEvidenceDocuments -contains $RelativePath) {
        return
    }

    $sourcePath = Get-RepositoryPath $RelativePath
    $sourceDirectory = Split-Path $sourcePath -Parent
    $archivePathPrefixes = @('runtimes/', 'tools/shared/', 'tools/net')
    $pathPattern = '`(?<path>[^`\r\n]*[/\\][^`\r\n]*\.[A-Za-z0-9]{1,8})`'
    foreach ($match in [regex]::Matches($Content, $pathPattern)) {
        $text = $match.Groups['path'].Value
        $normalized = $text.Replace('\', '/')
        if ($normalized.Contains('://', [StringComparison]::Ordinal) -or
            $normalized.Contains('<', [StringComparison]::Ordinal) -or
            $normalized.Contains('>', [StringComparison]::Ordinal) -or
            ($archivePathPrefixes | Where-Object {
                $normalized.StartsWith($_, [StringComparison]::OrdinalIgnoreCase)
            })) {
            continue
        }

        $relative = $normalized.Replace('/', '\')
        if ($relative.StartsWith('.\', [StringComparison]::Ordinal)) {
            $relative = $relative.Substring(2)
        }
        $candidates = @(
            [IO.Path]::GetFullPath((Join-Path $sourceDirectory $relative)),
            [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relative))
        )
        $resolved = $false
        foreach ($candidate in $candidates) {
            try {
                $contained = Resolve-SharpProofContainedPath `
                    -Root $repositoryRoot `
                    -Path $candidate `
                    -ParameterName 'Backticked repository path'
                if (Test-Path -LiteralPath $contained -PathType Leaf) {
                    $resolved = $true
                    break
                }
            }
            catch {
                continue
            }
        }
        if (-not $resolved) {
            throw (
                "Broken backticked repository path in ${RelativePath}: $text")
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

function Assert-ParseableMarkdownFences {
    param(
        [Parameter(Mandatory)][string]$RelativePath,
        [Parameter(Mandatory)][string]$Content
    )

    $pattern =
        '^```(?<language>xml|powershell|pwsh)[ \t]*\n' +
        '(?<body>.*?)^```[ \t]*$'
    $ordinal = 0
    foreach ($match in [regex]::Matches(
            $Content,
            $pattern,
            [Text.RegularExpressions.RegexOptions]::Multiline -bor
                [Text.RegularExpressions.RegexOptions]::Singleline -bor
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $ordinal++
        $language = $match.Groups['language'].Value.ToLowerInvariant()
        $body = $match.Groups['body'].Value
        if ($language -eq 'xml') {
            try {
                [void][xml](
                    "<SharpProofSnippetRoot>`n" +
                    $body +
                    "`n</SharpProofSnippetRoot>")
            }
            catch {
                throw (
                    "$RelativePath XML fence $ordinal is not parseable: " +
                    $_.Exception.Message)
            }
            continue
        }

        $tokens = $null
        $errors = $null
        [void][Management.Automation.Language.Parser]::ParseInput(
            $body,
            [ref]$tokens,
            [ref]$errors)
        if ($errors.Count -ne 0) {
            throw (
                "$RelativePath PowerShell fence $ordinal is not parseable: " +
                ($errors -join '; '))
        }
    }
}

function Get-EnumMemberMap {
    param(
        [Parameter(Mandatory)][string]$Content
    )

    $membersByName = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
    $pattern = '(?:public|internal)\s+enum\s+' +
        '(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{(?<body>.*?)\}'
    foreach ($match in [regex]::Matches(
            $Content,
            $pattern,
            [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        $enumName = $match.Groups['name'].Value
        $members = @(
            $match.Groups['body'].Value.Split([char]',') |
                ForEach-Object {
                    $name = ($_ -split '=')[0].Trim()
                    if ($name.Length -ne 0) {
                        $name
                    }
                }
        )
        if (-not $membersByName.TryAdd($enumName, $members)) {
            throw "Duplicate enum '$enumName' in a documentation source."
        }
    }
    return $membersByName
}

function Get-EnumMembers {
    param(
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string, object]]$EnumMemberMap,
        [Parameter(Mandatory)][string]$EnumName
    )

    $members = $null
    if (-not $EnumMemberMap.TryGetValue($EnumName, [ref]$members)) {
        throw "Could not find enum '$EnumName' in a documentation source."
    }
    return @($members)
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
    Assert-BacktickedRepositoryPaths $relativePath $maintainedText
    Assert-ParseableMarkdownFences $relativePath $maintainedText
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
    if ([regex]::IsMatch(
            $maintainedText,
            '\b(?:requires?|must)\b[^.]{0,200}\bpublic(?:-|\s+)key\b' +
            '[^.]{0,100}\bmatch\b',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        throw (
            'Maintained documentation makes an obsolete public-key ' +
            "matching claim: $relativePath")
    }
}
foreach ($relativePath in $currentMaintainedDocuments) {
    $currentText = Get-RequiredText $relativePath
    foreach ($obsoleteHostBootstrap in @(
            'git bundle create',
            'repository.bundle',
            'Invoke-SharpProofDotnet.ps1')) {
        if ($currentText.Contains(
                $obsoleteHostBootstrap,
                [StringComparison]::Ordinal)) {
            throw (
                'Current documentation invokes obsolete host tooling ' +
                "'$obsoleteHostBootstrap': $relativePath")
        }
    }
}
foreach ($relativePath in @(
        'eng\diagnostics\diagnostic-descriptors.v1.json')) {
    Assert-RepositoryLinksInSource $relativePath
}

$packageVersion = Get-SharpProofReleaseVersion -RepositoryRoot $repositoryRoot
if ($packageVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.') {
    throw "Could not derive the product series from '$packageVersion'."
}
$productSeries = $Matches['major'] + '.' + $Matches['minor']
foreach ($productDocument in @(
        'README.md',
        'docs\README.md',
        'docs\architecture.md',
        'docs\coverage-and-limits.md')) {
    $productText = Get-RequiredText $productDocument
    $productMentions = [regex]::Matches(
        $productText,
        '\bSharpProof\s+(?<series>\d+\.\d+)(?:\.\d+)?')
    if ($productMentions.Count -eq 0) {
        throw "$productDocument is missing the SharpProof product series."
    }
    foreach ($mention in $productMentions) {
        if ($mention.Groups['series'].Value -ne $productSeries) {
            throw (
                "$productDocument has stale SharpProof product series " +
                "'$($mention.Groups['series'].Value)'; expected " +
                "'$productSeries'.")
        }
    }
}

$readme = Get-RequiredText 'README.md'
$launcherDiagnosticCodes = @('SP0047', 'SP0048')
$requiredReadmeText = @(
    $packageVersion,
    'SharpProofProfile',
    'SharpProofFeatures',
    'SharpProofVerifyPolicy',
    'SharpProofAssumptionPolicy',
    'SharpProofVerify=true',
    'SharpProof.Worker'
) + $launcherDiagnosticCodes + @(
    'SP0027',
    'Proven',
    'Refuted',
    'Unknown',
    'SHARPPROOF_CONTRACTS',
    'canonical Linux amd64 container',
    'compiler artifact',
    'SARIF',
    'docs/README.md'
)
$forbiddenReadmeText = @(
    'Deep Ensures',
    'DeepEnsures',
    'WorkerVerificationStatus',
    'WorkerVerificationReason',
    'SharpProof.Verifier.Win-x64',
    'supported only on Windows x64',
    'Windows Job Object'
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
    'SharpProof.Analyzer.Core\Configuration\AnalyzerConfigurationOptionRegistry.cs')
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

$diagnosticReference = Get-RequiredText 'docs\diagnostic-examples.md'
$descriptorCatalogPath =
    'eng\diagnostics\diagnostic-descriptors.v1.json'
$descriptorCatalog = Get-RequiredText $descriptorCatalogPath |
    ConvertFrom-Json
if ($descriptorCatalog.schemaVersion -ne 1) {
    throw 'Unsupported diagnostic-descriptor catalog schema.'
}
foreach ($output in @($descriptorCatalog.outputs)) {
    [void](Get-RequiredText ([string]$output.outputPath))
    foreach ($descriptor in @($output.diagnostics)) {
        $id = [string]$descriptor.id
        $helpLink = [string]$descriptor.helpLinkUri
        if ([string]::IsNullOrWhiteSpace($helpLink)) {
            throw "Diagnostic catalog is missing '$id' help link."
        }
        Assert-RepositoryDocumentLink $descriptorCatalogPath $helpLink
        if ($id -notmatch '^SP(?:CF)?\d{4}$') {
            continue
        }
        $anchor = '<a id="' + $id.ToLowerInvariant() + '"></a>'
        if (-not $diagnosticReference.Contains(
            $anchor,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Diagnostic catalog is missing '$id' and its help anchor."
        }
    }
}
$analyzerDescriptorOutput = @(
    $descriptorCatalog.outputs |
        Where-Object { $_.name -eq 'analyzer' }
)
if ($analyzerDescriptorOutput.Count -ne 1) {
    throw 'Could not derive analyzer diagnostic defaults.'
}
foreach ($descriptor in @($analyzerDescriptorOutput[0].diagnostics)) {
    $id = [string]$descriptor.id
    $severity = [string]$descriptor.defaultSeverity
    $enabled = [bool]$descriptor.isEnabledByDefault
    $row = [regex]::Match(
        $diagnosticReference,
        '(?m)^\|\s*`' + [regex]::Escape($id) +
        '`\s*\|[^|]*\|\s*(?<severity>Info|Warning|Error),\s*' +
        '(?<state>on|off)\s*\|')
    if (-not $row.Success -or
        $row.Groups['severity'].Value -ne $severity -or
        ($row.Groups['state'].Value -eq 'on') -ne $enabled) {
        throw (
            "Diagnostic catalog default for '$id' does not match " +
            "$severity, " + $(if ($enabled) { 'on' } else { 'off' }) + '.')
    }
}
foreach ($launcherDiagnostic in $launcherDiagnosticCodes) {
    $anchor = '<a id="' + $launcherDiagnostic.ToLowerInvariant() + '"></a>'
    if (-not $diagnosticReference.Contains(
            $anchor,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            "Diagnostic catalog is missing launcher diagnostic " +
            "'$launcherDiagnostic' and its help anchor.")
    }
}

$contractApiDescriptors = @(
    $analyzerDescriptorOutput[0].diagnostics |
        Where-Object { $_.id -in @('SP0047', 'SP0050') }
)
if ($contractApiDescriptors.Count -ne 2 -or
    @($contractApiDescriptors | ForEach-Object { [string]$_.id } |
        Sort-Object) -join ',' -cne 'SP0047,SP0050') {
    throw 'Diagnostic authority must contain exactly SP0047 and SP0050.'
}
$rejectedIdentitySource = Get-RequiredText (
    'SharpProof.Analyzer.Core\SharpProofControlAttributePolicy.cs')
if ([regex]::Matches(
        $rejectedIdentitySource,
        '"ContractApiIdentityRejected"').Count -ne 1) {
    throw (
        'Rejected contract API behavior must have one canonical ' +
        'ContractApiIdentityRejected reason.')
}
$requiredContractApiDocumentation = @(
    'SP0047 also reports `ContractApiIdentityRejected`',
    'A readable payload whose hash does not match the pin is rejected and every',
    'attempted use reports SP0047 `ContractApiIdentityRejected`',
    'SP0050 is reserved for a payload that cannot be read.'
)
foreach ($required in $requiredContractApiDocumentation) {
    if (-not $diagnosticReference.Contains(
            $required,
            [StringComparison]::Ordinal)) {
        throw (
            'Diagnostic documentation does not match rejected contract API ' +
            "behavior: missing '$required'.")
    }
}
$forbiddenContractApiSilenceClaims = @(
    'disable contract analysis without a diagnostic',
    'disables contract analysis without a diagnostic',
    'continues to disable contract analysis without a diagnostic'
)
foreach ($forbidden in $forbiddenContractApiSilenceClaims) {
    if ($diagnosticReference.Contains(
            $forbidden,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            'Diagnostic documentation contains the stale readable-payload ' +
            "silence claim: '$forbidden'.")
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
$portablePackageProps = [xml](Get-RequiredText (
    'SharpProof.Package\buildTransitive\SharpProof.props'))
$verifierPackageProps = [xml](Get-RequiredText (
    'SharpProof.Verifier\buildTransitive\SharpProof.Verifier.props'))
$packagePropsDocuments = @(
    $portablePackageProps,
    $verifierPackageProps
)
$workerPropertyNames = @(
    @(
        'SharpProofVerify'
        foreach ($packagePropsDocument in $packagePropsDocuments) {
            $packagePropsDocument.SelectNodes(
                '//CompilerVisibleProperty' +
                '[starts-with(@Include, "SharpProof")]') |
                ForEach-Object {
                    $_.GetAttribute('Include').Split(';',
                        [StringSplitOptions]::RemoveEmptyEntries) |
                        Where-Object { $_.StartsWith(
                            'SharpProof', [StringComparison]::Ordinal) }
                }
            $packagePropsDocument.SelectNodes(
                '//PropertyGroup/*[starts-with(local-name(), "SharpProofVerify") or local-name() = "SharpProofDotNetHost"]') |
                ForEach-Object { $_.Name }
        }
    ) |
        Sort-Object -Unique
)
foreach ($propertyName in $workerPropertyNames) {
    if (-not $limitReference.Contains(
        $propertyName,
        [StringComparison]::Ordinal)) {
        throw "Analysis-limit reference is missing property '$propertyName'."
    }
}

$compilerCallableLowerer = Get-RequiredText (
    'SharpProof.CompilerCollector\CompilerArtifact\CompilerCallableLowerer.cs')
$compilerPreparation = Get-RequiredText (
    'SharpProof.CompilerArtifact\CompilerArtifactModel.generated.cs')
$bodyExecutor = Get-RequiredText (
    'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs')
$blockBound = [regex]::Match(
    $compilerCallableLowerer,
    'const\s+int\s+MaximumBodyBlocks\s*=\s*(?<value>\d+)\s*;')
$instructionBound = [regex]::Match(
    $compilerPreparation,
    'const\s+int\s+MaximumInstructions\s*=\s*(?<value>\d+)\s*;')
$operationFactor = [regex]::Match(
    $bodyExecutor,
    'DefaultMaximumSymbolicOperations\s*=\s*' +
    'CompilerPreparedBody\.MaximumInstructions\s*\*\s*(?<value>\d+)\s*;')
if (-not $blockBound.Success -or -not $instructionBound.Success -or
    -not $operationFactor.Success) {
    throw 'Could not derive compiler/worker body execution bounds.'
}
$maximumInstructions = [int]::Parse(
    $instructionBound.Groups['value'].Value,
    [Globalization.CultureInfo]::InvariantCulture)
$symbolicOperationFactor = [int]::Parse(
    $operationFactor.Groups['value'].Value,
    [Globalization.CultureInfo]::InvariantCulture)
$fixedBodyBounds = @(
    [pscustomobject]@{
        Label = 'Reachable CFG blocks'
        Value = $blockBound.Groups['value'].Value
    },
    [pscustomobject]@{
        Label = 'Lowered body instructions'
        Value = $maximumInstructions
    },
    [pscustomobject]@{
        Label = 'Symbolic operations'
        Value = $maximumInstructions * $symbolicOperationFactor
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
    'SharpProof.Worker.Protocol\ProtocolModel.generated.cs')
$protocolEnumMemberMap = Get-EnumMemberMap $protocolSource
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
    foreach ($value in Get-EnumMembers $protocolEnumMemberMap $enumName) {
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
    'SharpProof.CompilerArtifact\CompilerArtifactModel.generated.cs')
$compilerArtifactVersion = [regex]::Match(
    $compilerArtifactSource,
    'CompilerManifestArtifactVersions\s*\{[\s\S]*?\bCurrent\s*=\s*(?<value>\d+)\s*;')
$relationalSummaryVersion = [regex]::Match(
    $compilerArtifactSource,
    'CompilerRelationalSummaryVersions\s*\{[\s\S]*?\bCurrent\s*=\s*(?<value>\d+)\s*;')
$specificationPackVersion = [regex]::Match(
    $compilerArtifactSource,
    'CompilerSpecificationPackVersions\s*\{[\s\S]*?\bCurrent\s*=\s*(?<value>\d+)\s*;')
if (-not $protocolVersion.Success -or
    -not $cacheVersion.Success -or
    -not $manifestVersion.Success -or
    -not $compilerArtifactVersion.Success -or
    -not $relationalSummaryVersion.Success -or
    -not $specificationPackVersion.Success) {
    throw (
        'Could not derive worker protocol, cache, manifest, compiler-' +
        'artifact, relational-summary, and specification-pack versions.')
}

$derivedVersions = [ordered]@{
    Protocol = $protocolVersion.Groups['value'].Value
    Cache = $cacheVersion.Groups['value'].Value
    Manifest = $manifestVersion.Groups['value'].Value
    CompilerArtifact = $compilerArtifactVersion.Groups['value'].Value
    RelationalSummary = $relationalSummaryVersion.Groups['value'].Value
    SpecificationPack = $specificationPackVersion.Groups['value'].Value
}
$acceptanceContract = Get-RequiredText 'eng\acceptance\contract.json' |
    ConvertFrom-Json
$protocolSchema = Get-RequiredText (
    'SharpProof.Worker.Protocol\ProtocolModel.schema.json') |
    ConvertFrom-Json
$typedResultDocumentation = $protocolSchema.documentation
if ($null -eq $typedResultDocumentation) {
    throw 'Protocol schema is missing typed-result documentation authority.'
}
$typedResultEnums = @(
    'WorkerClaimOutcome',
    'WorkerEffectEvidenceCertainty'
)
$typedResultRows = [ordered]@{}
foreach ($enumName in $typedResultEnums) {
    $declarations = @($protocolSchema.declarations | Where-Object {
            [string]$_.kind -ceq 'enum' -and
            [string]$_.name -ceq $enumName
        })
    $documentationProperty =
        $typedResultDocumentation.PSObject.Properties[$enumName]
    if ($declarations.Count -ne 1 -or $null -eq $documentationProperty) {
        throw "Protocol schema is missing $enumName documentation authority."
    }
    $members = @($declarations[0].members | ForEach-Object {
            [string]$_.name
        })
    $documented = @($documentationProperty.Value)
    $documentedNames = @($documented | ForEach-Object {
            [string]$_.name
        })
    if (($members -join "`n") -cne ($documentedNames -join "`n") -or
        @($documented | Where-Object {
                [string]::IsNullOrWhiteSpace([string]$_.meaning) -or
                ([string]$_.meaning).Contains('|', [StringComparison]::Ordinal) -or
                ([string]$_.meaning).Contains("`n", [StringComparison]::Ordinal)
            }).Count -ne 0) {
        throw (
            "$enumName documentation must exactly follow schema member " +
            'order with one safe nonblank meaning per member.')
    }
    $typedResultRows[$enumName] = $documented
}
$effectCertaintyTables = @($protocolSchema.validationTables | Where-Object {
        [string]$_.name -ceq 'EffectCertainty'
    })
if ($effectCertaintyTables.Count -ne 1) {
    throw 'Protocol schema must own exactly one EffectCertainty tuple table.'
}
$effectCertaintyTable = $effectCertaintyTables[0]
$effectParameterNames = @($effectCertaintyTable.parameters | ForEach-Object {
        [string]$_.name
    })
if (($effectParameterNames -join ',') -cne 'outcome,reason,certainty' -or
    @($effectCertaintyTable.rows).Count -eq 0 -or
    @($effectCertaintyTable.rows | Where-Object {
            @($_).Count -ne 3 -or
            @($_ | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0
        }).Count -ne 0) {
    throw 'Protocol EffectCertainty tuple authority is malformed.'
}
$typedResultLines = [Collections.Generic.List[string]]::new()
$typedResultLines.Add('<!-- BEGIN SHARPPROOF TYPED EFFECT RESULTS -->')
foreach ($enumName in $typedResultEnums) {
    $typedResultLines.Add("### ``$enumName``")
    $typedResultLines.Add('')
    $typedResultLines.Add('| Member | Meaning |')
    $typedResultLines.Add('|---|---|')
    foreach ($row in @($typedResultRows[$enumName])) {
        $typedResultLines.Add(
            "| ``$([string]$row.name)`` | $([string]$row.meaning) |")
    }
    $typedResultLines.Add('')
}
$typedResultLines.Add('### Allowed effect-result tuples')
$typedResultLines.Add('')
$typedResultLines.Add('| Outcome | Reason | Certainty |')
$typedResultLines.Add('|---|---|---|')
foreach ($row in @($effectCertaintyTable.rows)) {
    $typedResultLines.Add(
        "| ``$([string]$row[0])`` | ``$([string]$row[1])`` | ``$([string]$row[2])`` |")
}
$typedResultLines.Add('<!-- END SHARPPROOF TYPED EFFECT RESULTS -->')
$expectedTypedResultBlock = $typedResultLines -join "`n"
$unknownReasons = Get-RequiredText 'docs\unknown-reasons.md'
$typedBlockPattern =
    [regex]::Escape('<!-- BEGIN SHARPPROOF TYPED EFFECT RESULTS -->') +
    '[\s\S]*?' +
    [regex]::Escape('<!-- END SHARPPROOF TYPED EFFECT RESULTS -->')
$typedBlocks = [regex]::Matches($unknownReasons, $typedBlockPattern)
if ($typedBlocks.Count -ne 1 -or
    $typedBlocks[0].Value -cne $expectedTypedResultBlock) {
    throw (
        'docs/unknown-reasons.md typed result block must exactly match the ' +
        'protocol schema member and tuple authority.')
}
$typedResultReadmeClaim =
    'The schema-owned typed result table includes `VacuousEntry` and the full' +
    "`n" +
    '`Unavailable` domain; see [unknown reasons]' +
    '(docs/unknown-reasons.md#worker-verification-records).'
if ([regex]::Matches(
        $readme,
        [regex]::Escape($typedResultReadmeClaim)).Count -ne 1) {
    throw 'README.md must contain the exact typed-result documentation claim.'
}
$containerCpuLimit = [int]$acceptanceContract.container.defaultCpuLimit
$containerMemoryMiB = [int]$acceptanceContract.container.defaultMemoryMiB
$testProjectCpuDivisor =
    [int]$acceptanceContract.automation.testProjectCpuDivisor
$packageTestCpuPercent =
    [int]$acceptanceContract.automation.packageTestCpuPercent
$buildCpuPercent =
    [int]$acceptanceContract.automation.buildCpuPercent
$mutationParallelism =
    [int]$acceptanceContract.automation.mutationParallelism
if ($containerCpuLimit -ne 0 -or
    $containerMemoryMiB -le 0 -or
    $testProjectCpuDivisor -le 0 -or
    $packageTestCpuPercent -le 0 -or
    $packageTestCpuPercent -gt 100 -or
    $buildCpuPercent -le 0 -or
    $buildCpuPercent -gt 100 -or
    $mutationParallelism -le 0) {
    throw 'Acceptance resource and concurrency authority is invalid.'
}
$resourceClaims = @(
    ("Containers use all CPUs available to Docker and up to " +
        "$containerMemoryMiB MiB by default.")
    "Semantic-test scheduling uses every container-visible CPU."
    ("Package integration tests use $packageTestCpuPercent% of " +
        "container-visible CPU lanes by default.")
    ("Other test-project concurrency auto-detects the available CPUs " +
        "and uses one lane per $testProjectCpuDivisor CPUs.")
    ("Parallel prerequisite builds use $buildCpuPercent% of " +
        "container-visible CPU lanes by default.")
    ("Trusted mutations use $mutationParallelism deterministic weighted lanes.")
)
foreach ($resourceDocument in @(
        'README.md',
        'docs\container-development.md')) {
    $resourceText = Get-RequiredText $resourceDocument
    foreach ($claim in $resourceClaims) {
        $claimCount = [regex]::Matches(
            $resourceText,
            [regex]::Escape($claim)).Count
        if ($claimCount -cne 1) {
            throw (
                "$resourceDocument must contain exactly one catalog-derived " +
                "resource claim '$claim'; found $claimCount.")
        }
    }
}
$devCheckPlanScript = Get-RepositoryPath (
    'scripts\Get-SharpProofDevCheckPlan.ps1')
$debugCheckPlan = & $devCheckPlanScript -Configuration Debug |
    ConvertFrom-Json
$releaseCheckPlan = & $devCheckPlanScript -Configuration Release |
    ConvertFrom-Json
foreach ($plan in @($debugCheckPlan, $releaseCheckPlan)) {
    if ([int]$plan.schemaVersion -ne 1 -or
        [string]$plan.command -cne 'check') {
        throw 'Developer-check command plan authority is invalid.'
    }
}
$debugCommands = @($debugCheckPlan.commands)
$releaseCommands = @($releaseCheckPlan.commands)
$debugPacks = @($debugCommands | Where-Object {
        [string]$_.id -clike 'package-pack:*'
    })
$releasePacks = @($releaseCommands | Where-Object {
        [string]$_.id -clike 'package-pack:*'
    })
if (@($debugCommands | Where-Object {
            [string]$_.id -ceq 'solution-build' -and
            [string]$_.configuration -ceq 'Debug'
        }).Count -ne 1 -or
    @($debugCommands | Where-Object {
            [string]$_.id -ceq 'package-product-build' -and
            [string]$_.configuration -ceq 'Release'
        }).Count -ne 1 -or
    @($debugPacks | Where-Object {
            [string]$_.configuration -cne 'Release' -or -not [bool]$_.noBuild
        }).Count -ne 0 -or
    @($releaseCommands | Where-Object {
            [string]$_.id -ceq 'solution-build' -and
            [string]$_.configuration -ceq 'Release'
        }).Count -ne 1 -or
    @($releaseCommands | Where-Object {
            [string]$_.id -ceq 'package-product-build'
        }).Count -ne 0 -or
    @($releasePacks | Where-Object {
            [string]$_.configuration -cne 'Release' -or -not [bool]$_.noBuild
        }).Count -ne 0) {
    throw 'Developer-check command plan has an unsupported build topology.'
}
$checkPlanClaims = @(
    ("The default Debug check concurrently performs one Debug solution " +
        "build and one Release package-product build, then runs " +
        "$($debugPacks.Count) Release " +
        "pack commands with ``--no-build``.")
    ("The Release check performs one Release solution build and " +
        "$($releasePacks.Count) Release pack commands with ``--no-build``.")
)
foreach ($checkPlanDocument in @(
        'README.md',
        'docs\container-development.md')) {
    $checkPlanText = Get-RequiredText $checkPlanDocument
    foreach ($claim in $checkPlanClaims) {
        $claimCount = [regex]::Matches(
            $checkPlanText,
            [regex]::Escape($claim)).Count
        if ($claimCount -cne 1) {
            throw (
                "$checkPlanDocument must contain exactly one command-plan " +
                "claim '$claim'; found $claimCount.")
        }
    }
}
$contractVersions = [ordered]@{
    Protocol = $acceptanceContract.worker.protocolVersion
    Cache = $acceptanceContract.cache.schemaVersion
    Manifest = $acceptanceContract.worker.manifestSchemaVersion
    CompilerArtifact = $acceptanceContract.worker.compilerArtifactSchemaVersion
    RelationalSummary = $acceptanceContract.worker.relationalSummarySchemaVersion
    SpecificationPack = $acceptanceContract.worker.specificationPackSchemaVersion
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
        "compiler artifact schema version $($derivedVersions.CompilerArtifact)",
        "relational-summary schema version $($derivedVersions.RelationalSummary)",
        "specification-pack schema version $($derivedVersions.SpecificationPack)")) {
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
    },
    [pscustomobject]@{
        Name = 'relational-summary schema'
        Pattern = '\brelational-summary\s+schema\s+version\s+(?<value>\d+)\b'
        Expected = $derivedVersions.RelationalSummary
    },
    [pscustomobject]@{
        Name = 'specification-pack schema'
        Pattern = '\bspecification-pack\s+schema\s+version\s+(?<value>\d+)\b'
        Expected = $derivedVersions.SpecificationPack
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

Write-Host (
    "SharpProof documentation matches code-derived package, protocol, " +
    'cache, manifest, compiler-artifact, relational-summary, and ' +
    'specification-pack versions, acceptance-contract versions, ' +
    'configuration, diagnostics, API specs, worker options, protocol enums, ' +
    'links, anchors, and parseable XML/PowerShell fences.')
