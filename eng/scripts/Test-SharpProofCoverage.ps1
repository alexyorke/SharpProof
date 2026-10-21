[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CoverageRoot,

    [Parameter()]
    [string]$ComparisonRef,

    [Parameter()]
    [string]$BaselinePath,

    [Parameter()]
    [string]$SummaryPath,

    [Parameter()]
    [switch]$ReportOnly,

    [Parameter()]
    [switch]$IncludeWorkingTree
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Get-SharpProofTcbPaths.ps1')
. (Join-Path $PSScriptRoot 'SharpProof.CoverageShardEvidence.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
. (Join-Path $PSScriptRoot 'Resolve-SharpProofSourceDocument.ps1')
function Test-CoverageFloor {
    param(
        [long]$CoveredLines,
        [long]$CoverableLines,
        [double]$LinePercent,
        [double]$Minimum
    )

    # A 100% floor requires every authenticated point to be covered, even
    # when the displayed percentage rounds a small uncovered remainder away.
    if ($Minimum -eq 100.0) {
        return $CoveredLines -eq $CoverableLines
    }
    return $LinePercent + 0.005 -ge $Minimum
}

function Get-OrdinalSortedUniqueStrings {
    param([AllowEmptyCollection()][Collections.Generic.IEnumerable[string]]$Values)
    $sorted = [Collections.Immutable.ImmutableSortedSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    if ($null -ne $Values -and -not ($Values -is [Collections.Immutable.ImmutableArray[string]] -and $Values.IsDefault)) {
        foreach ($value in $Values) { [void]$sorted.Add($value) }
    }
    return [Collections.Immutable.ImmutableArray]::CreateRange[string]($sorted.ToImmutable())
}

function Test-ClearlyNonSemanticSourceLine {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Line
    )

    $trimmed = $Line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed -in @('{', '}', 'try')) {
        return $true
    }
    if ($trimmed.StartsWith('//', [StringComparison]::Ordinal) -or
        $trimmed.StartsWith('#pragma warning ', [StringComparison]::Ordinal)) {
        # Warning directives change compilation diagnostics, not executable code.
        return $true
    }

    $withoutBlockComments = [regex]::Replace(
        $trimmed,
        '/\*.*?\*/',
        '')
    $withoutBlockComments = $withoutBlockComments.Trim()
    return $withoutBlockComments.Length -eq 0 -or
    $withoutBlockComments -in @('{', '}') -or
    $withoutBlockComments.StartsWith('//', [StringComparison]::Ordinal)
}

function Test-HasAddedSemanticSourceLines {
    param(
        [AllowEmptyCollection()][Nullable[Collections.Immutable.ImmutableArray[string]]]$SourceLines,
        [AllowEmptyCollection()][Collections.Generic.IEnumerable[int]]$AddedLines
    )
    $addedLineSnapshot = if ($null -eq $AddedLines) {
        , [Collections.Immutable.ImmutableHashSet[int]]::Empty
    }
    else { , [Collections.Immutable.ImmutableHashSet]::CreateRange[int]($AddedLines) }
    foreach ($number in $addedLineSnapshot) {
        if ($number -le 0 -or $null -eq $SourceLines -or $SourceLines.IsDefault -or
            $number -gt $SourceLines.Length -or
            -not (Test-ClearlyNonSemanticSourceLine -Line $SourceLines[$number - 1])) {
            return $true
        }
    }
    return $false
}

function Resolve-DurableComparisonCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Reference
    )

    $authority = $Reference
    if ($Reference -notmatch '^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$') {
        if ($Reference -ceq 'HEAD' -or $Reference -ceq '@') {
            throw (
                "ComparisonRef '$Reference' is not a durable explicit " +
                'comparison authority.')
        }

        $symbolic = Invoke-SharpProofGitText `
            -RepositoryRoot $repositoryRoot `
            -Arguments @(
            'rev-parse',
            '--symbolic-full-name',
            '--verify',
            $Reference) `
            -FailureMessage (
            "ComparisonRef '$Reference' is not a durable explicit " +
            'comparison authority.')
        $authority = $symbolic.Trim()
        if ([string]::IsNullOrWhiteSpace($authority) -or
            $authority.Contains("`n") -or
            $authority.Contains("`r") -or
            -not $authority.StartsWith(
                'refs/',
                [StringComparison]::Ordinal)) {
            throw (
                "ComparisonRef '$Reference' is not a durable explicit " +
                'comparison authority.')
        }
    }

    $commit = (Invoke-SharpProofGitText `
            -RepositoryRoot $repositoryRoot `
            -Arguments @('rev-parse', '--verify', "$authority^{commit}") `
            -FailureMessage (
            "ComparisonRef '$Reference' is not a durable explicit " +
            'comparison authority.')).Trim()
    if ($commit -notmatch '^(?:[0-9a-f]{40}|[0-9a-f]{64})$') {
        throw (
            "ComparisonRef '$Reference' did not resolve to one exact commit.")
    }

    return $commit
}

$resolvedCoverageRoot = (Resolve-Path `
        -LiteralPath $CoverageRoot `
        -ErrorAction Stop).Path
if (-not (Test-Path `
            -LiteralPath $resolvedCoverageRoot `
            -PathType Container)) {
    throw "CoverageRoot is not a directory: $resolvedCoverageRoot"
}
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $resolvedBaselinePath = Join-Path `
        $repositoryRoot `
        'eng\coverage\baseline.json'
}
else {
    $resolvedBaselinePath = (Resolve-Path `
            -LiteralPath $BaselinePath `
            -ErrorAction Stop).Path
}
$baseline = Get-Content -LiteralPath $resolvedBaselinePath -Raw |
    ConvertFrom-Json
if ($baseline.schemaVersion -ne 1 -or
    $null -eq $baseline.projects -or
    $null -eq $baseline.declarationOnlyTcbFiles -or
    @($baseline.projects.PSObject.Properties).Count -eq 0 -or
    [double]$baseline.minimumAggregateLinePercent -lt 0 -or
    [double]$baseline.minimumAggregateLinePercent -gt 100 -or
    [double]$baseline.minimumChangedTcbLinePercent -lt 0 -or
    [double]$baseline.minimumChangedTcbLinePercent -gt 100) {
    throw 'Unsupported or invalid coverage baseline.'
}
foreach ($property in $baseline.projects.PSObject.Properties) {
    if ([double]$property.Value -lt 0 -or
        [double]$property.Value -gt 100) {
        throw "Invalid coverage baseline for '$($property.Name)'."
    }
}
$declarationOnlyTcbFiles =
[Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
    [StringComparer]::Ordinal)
foreach ($value in @($baseline.declarationOnlyTcbFiles)) {
    $path = [string]$value
    if ([string]::IsNullOrWhiteSpace($path) -or
        [IO.Path]::IsPathRooted($path) -or
        $path.Contains('\') -or
        $path -match '(^|/)\.\.?(/|$)' -or
        -not $path.EndsWith(
            '.cs',
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $declarationOnlyTcbFiles.Add($path)) {
        throw "Invalid declaration-only TCB coverage path '$path'."
    }
}
$declarationOnlyTcbFiles = $declarationOnlyTcbFiles.ToImmutable()
$reports = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
        Get-ChildItem `
            -LiteralPath $resolvedCoverageRoot `
            -Recurse `
            -Filter '*.cobertura*.xml' `
            -File |
            Where-Object { Test-SharpProofCoverageReportName -Path $_.Name } |
            Where-Object {
                $_.Name -match '\.cobertura(?:\[\d+\])?\.xml$' -and
                [IO.Path]::GetRelativePath(
                    $resolvedCoverageRoot,
                    $_.FullName).Replace('\', '/') -cnotmatch '(^|/)In(/|$)'
            } |
            Sort-Object FullName
    ))
if ($reports.Length -eq 0) {
    throw "No Cobertura XML reports were found under $resolvedCoverageRoot."
}
if ([string]::IsNullOrWhiteSpace($ComparisonRef) -and -not $ReportOnly) {
    throw 'ComparisonRef is required for changed-TCB coverage enforcement.'
}
$comparisonCommit = if ([string]::IsNullOrWhiteSpace($ComparisonRef)) {
    ''
}
else {
    Resolve-DurableComparisonCommit -Reference $ComparisonRef
}

$coverageAuthorityPath = Join-Path `
    $resolvedCoverageRoot `
    'coverage-authority.json'
if (-not (Test-Path -LiteralPath $coverageAuthorityPath -PathType Leaf)) {
    throw (
        "Coverage authority evidence is missing: '$coverageAuthorityPath'.")
}
$recordedAuthority = Get-Content `
    -LiteralPath $coverageAuthorityPath `
    -Raw | ConvertFrom-Json
$authorityScript = Join-Path $PSScriptRoot 'Get-SharpProofProductionInventory.ps1'
$LASTEXITCODE = 0
$coverageIndependentAuthorityTimer = [Diagnostics.Stopwatch]::StartNew()
try {
    $recomputedAuthorityJson = & $authorityScript -RepositoryRoot $repositoryRoot -Configuration Release -RequirePdb
    if ($LASTEXITCODE -ne 0) {
        throw 'Production inventory authority could not be recomputed from current MSBuild/PDB inputs.'
    }
}
finally {
    $coverageIndependentAuthorityTimer.Stop()
    Write-Host ('Coverage phase: name=validation-independent-authority elapsedMilliseconds={0}' -f $coverageIndependentAuthorityTimer.ElapsedMilliseconds)
}
$recomputedAuthority = ($recomputedAuthorityJson -join [Environment]::NewLine) |
    ConvertFrom-Json
if ($recordedAuthority.schemaVersion -ne 1 -or
    $recordedAuthority.commit -cne $recomputedAuthority.commit -or
    $recordedAuthority.commit -cne (Invoke-SharpProofGitText `
            -RepositoryRoot $repositoryRoot `
            -Arguments @('rev-parse', 'HEAD') `
            -FailureMessage 'Could not resolve the current repository commit.' `
            -TrimOutput) -or
    $recordedAuthority.configuration -cne 'Release') {
    throw (
        'Coverage authority evidence does not match the exact current ' +
        'commit, evaluated MSBuild inventory, binaries, and portable-PDB universe.')
}
$authorityProjectNames = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
        $recomputedAuthority.projects |
            ForEach-Object { [string]$_.name } |
            Sort-Object))
$baselineProjectNames = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
        $baseline.projects.PSObject.Properties |
            ForEach-Object { [string]$_.Name } |
            Sort-Object))
if (($authorityProjectNames -join [Environment]::NewLine) -cne
    ($baselineProjectNames -join [Environment]::NewLine)) {
    throw (
        'Coverage baseline project floors do not match the independently ' +
        'evaluated production inventory.')
}
$authorityProjectsByName = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($authorityProject in $recomputedAuthority.projects) {
    $authorityProjectName = [string]$authorityProject.name
    if ($authorityProjectsByName.ContainsKey($authorityProjectName)) {
        throw "Coverage authority has duplicate project '$authorityProjectName'."
    }
    $authorityProjectsByName.Add($authorityProjectName, $authorityProject)
}
$authorityProjectsByName = $authorityProjectsByName.ToImmutable()
$expectedAuthorityModules = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($recomputedAuthority.modules | Sort-Object project))
$expectedModuleIdentities = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
        $expectedAuthorityModules |
            ForEach-Object {
                [string]$_.project + ':' + [string]$_.assemblyName + ':' +
                [string]$_.moduleMvid + ':' + [string]$_.pdbCodeViewGuid
            } |
            Sort-Object))
$expectedModuleIdentityText = $expectedModuleIdentities -join ','
$expectedAssemblyNames = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
    [StringComparer]::Ordinal)
foreach ($module in $expectedAuthorityModules) {
    if (-not $expectedAssemblyNames.Add([string]$module.assemblyName)) {
        throw "Coverage authority has a duplicate assembly name '$($module.assemblyName)'."
    }
}
$expectedAssemblyNames = $expectedAssemblyNames.ToImmutable()
# Each production compilation owns distinct runtime obligations, even when
# another assembly compiles the same physical source. Keep report MAX and PDB
# ranges inside that ownership boundary throughout ordinary and changed coverage.
$modulesByProject = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
$projectModuleKeys = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
$compileModuleKeys = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($module in $expectedAuthorityModules) {
    $project = $null
    if (-not $authorityProjectsByName.TryGetValue([string]$module.project, [ref]$project) -or
        [string]$project.assemblyName -cne [string]$module.assemblyName -or
        $modulesByProject.ContainsKey([string]$module.project)) {
        throw "Coverage authority has inconsistent production module ownership: '$($module.assemblyName)'."
    }
    $modulesByProject.Add([string]$module.project, $module)
    $projectModuleKeys[[string]$module.project] = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($file in @($project.compile)) {
        $compilePath = [string]$file.path
        if (-not $compileModuleKeys.ContainsKey($compilePath)) {
            $compileModuleKeys[$compilePath] = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        }
        $compileModuleKeys[$compilePath].Add([string]$module.assemblyName + ':' + $compilePath)
    }
}
$modulesByProject = $modulesByProject.ToImmutable()
if ($modulesByProject.Count -ne $authorityProjectsByName.Count) {
    throw 'Coverage authority does not bind every production project to exactly one module.'
}
$expectedLineHits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
$permittedLineRanges = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
$permittedLineCredits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
# Preserve authenticated direct collector records separately from the ordinary universe.
$reportedLineHits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
$expectedSequencePointCount = 0
foreach ($module in $expectedAuthorityModules) {
    foreach ($document in @($module.documents | Sort-Object path)) {
        $path = [string]$document.path
        $sourceKey = [string]$module.assemblyName + ':' + $path
        $project = $authorityProjectsByName[[string]$module.project]
        if (@($project.compile | Where-Object { [string]$_.path -ceq $path }).Count -ne 1) {
            throw "Coverage authority source has no unique owning Compile item: '$sourceKey'."
        }
        $projectModuleKeys[[string]$module.project].Add($sourceKey)
        if (-not $reportedLineHits.ContainsKey($sourceKey)) {
            $reportedLineHits[$sourceKey] = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, int]()
        }
        if ($expectedLineHits.ContainsKey($sourceKey)) {
            $fileLines = $expectedLineHits[$sourceKey]
        }
        else {
            $fileLines = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, int]()
            $expectedLineHits[$sourceKey] = $fileLines
        }
        if (-not $permittedLineRanges.ContainsKey($sourceKey)) {
            $permittedLineRanges[$sourceKey] = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
            $permittedLineCredits[$sourceKey] =
            [Collections.Immutable.ImmutableDictionary]::CreateBuilder[int, object]()
        }
        $documentSequencePoints =
        [Collections.Immutable.ImmutableHashSet]::CreateBuilder[int]()
        foreach ($value in @($document.sequencePoints | Sort-Object)) {
            $number = [int]$value
            if ($number -le 0) {
                throw (
                    "Coverage authority has an invalid sequence point " +
                    "'${path}:$number'.")
            }
            [void]$documentSequencePoints.Add($number)
            if (-not $fileLines.ContainsKey($number)) {
                $fileLines[$number] = 0
                $expectedSequencePointCount++
            }
        }
        $documentSequencePoints = $documentSequencePoints.ToImmutable()
        foreach ($range in @($document.sequencePointRanges)) {
            $startLine = [int]$range.startLine
            $endLine = [int]$range.endLine
            if ($startLine -le 0 -or $endLine -lt $startLine) {
                throw (
                    "Coverage authority has an invalid sequence-point range " +
                    "'${path}:$startLine-$endLine'.")
            }
            $permittedLineRanges[$sourceKey].Add((
                    [ordered]@{
                        startLine  = $startLine
                        endLine    = $endLine
                        creditLine = if ($documentSequencePoints.Contains($startLine)) {
                            $startLine
                        }
                        else {
                            0
                        }
                    }).AsReadOnly())
        }
    }
}
if ($expectedLineHits.Count -eq 0 -or $expectedSequencePointCount -eq 0) {
    throw 'Coverage authority contains no production sequence points.'
}

$frozenProjectModuleKeys = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($entry in $projectModuleKeys.GetEnumerator()) {
    $frozenProjectModuleKeys.Add($entry.Key, $entry.Value.ToImmutable())
}
$projectModuleKeys = $frozenProjectModuleKeys.ToImmutable()
$frozenCompileModuleKeys = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($entry in $compileModuleKeys.GetEnumerator()) {
    $frozenCompileModuleKeys.Add($entry.Key, $entry.Value.ToImmutable())
}
$compileModuleKeys = $frozenCompileModuleKeys.ToImmutable()

$frozenRanges = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($entry in $permittedLineRanges.GetEnumerator()) {
    $frozenRanges.Add($entry.Key, $entry.Value.ToImmutable())
}
$permittedLineRanges = $frozenRanges.ToImmutable()

$lineHits = $expectedLineHits
$reportedModuleDocuments = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
function Resolve-CoverageSourcePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [Collections.Immutable.ImmutableArray[string]]$SourceRoots
    )

    if ($SourceRoots.IsDefault) { throw [ArgumentException]::new('SourceRoots must be initialized.', 'SourceRoots') }
    return Resolve-SharpProofSourceDocument -RepositoryRoot $repositoryRoot `
        -DocumentPath $FileName -SourceRoots ([string[]]@($SourceRoots))
}

# Reuse an identical class only after its module/source and every line have
# passed this validation. MAX merging is idempotent, including zero hits and
# overlapping PDB credits. Any changed record must take the full validation path.
$validatedClassRecords = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, int]([StringComparer]::Ordinal)
$reusedClassRecords = 0
$validatedClassCount = 0
$coverageMergeTimer = [Diagnostics.Stopwatch]::StartNew()
$reportFiles = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$sourcePathCaches = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($report in $reports) {
    $reportFiles.Add(([ordered]@{
                path  = [IO.Path]::GetRelativePath(
                    $resolvedCoverageRoot,
                    $report.FullName).Replace('\', '/')
                bytes = [int64]$report.Length
            }).AsReadOnly())
    # Parse the authenticated report directly; avoid constructing and recoding
    # a second full XML string before XmlDocument performs the same validation.
    $document = [Xml.XmlDocument]::new()
    $document.Load($report.FullName)
    $authorityNodes = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            $document.SelectNodes('/coverage/sharpProofAuthority')))
    if ($authorityNodes.Length -ne 1) {
        throw (
            "Coverage report must contain exactly one authority envelope: " +
            $report.FullName)
    }
    $authorityNode = $authorityNodes[0]
    if ([string]$authorityNode.schemaVersion -cne '1' -or
        [string]$authorityNode.commit -cne [string]$recomputedAuthority.commit -or
        [string]$authorityNode.modules -cne $expectedModuleIdentityText) {
        throw (
            "Coverage report authority does not match current commit/universe: " +
            $report.FullName)
    }
    $sourceRoots = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            $document.SelectNodes('/coverage/sources/source') |
                ForEach-Object { [string]$_.InnerText }
        ))
    # The private source snapshot is immutable during validation. Reuse path
    # resolution only for the same ordered source-root array and filename;
    # different roots still receive independent physical-boundary validation.
    $sourceRootsKey = ConvertTo-Json -InputObject ([string[]]@($sourceRoots)) -Compress
    [object]$sourcePathCache = $null
    if (-not $sourcePathCaches.TryGetValue($sourceRootsKey, [ref]$sourcePathCache)) {
        $sourcePathCache = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
        $sourcePathCaches.Add($sourceRootsKey, $sourcePathCache)
    }
    $classes = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($document.SelectNodes('//class')))
    if ($classes.Length -eq 0) {
        throw "Coverage report has no classes: $($report.FullName)"
    }
    $reportLineCount = 0
    $hasProductionPackage = $false
    foreach ($class in $classes) {
        $package = $class.ParentNode.ParentNode
        if ($null -eq $package -or
            -not $package.HasAttribute('name')) {
            throw "Coverage report class has no package identity: $($report.FullName)"
        }
        $packageName = $package.GetAttribute('name')
        if (-not $expectedAssemblyNames.Contains($packageName)) {
            continue
        }
        $hasProductionPackage = $true
        if (-not $class.HasAttribute('filename')) {
            throw "Coverage report class has no source filename: $($report.FullName)"
        }
        $reportedFileName = $class.GetAttribute('filename').Replace('\', '/')
        if ($reportedFileName.Contains('/obj/', [StringComparison]::Ordinal) -or
            $reportedFileName.Contains('/bin/', [StringComparison]::Ordinal)) {
            continue
        }
        [string]$relativePath = $null
        if (-not $sourcePathCache.TryGetValue(
                $reportedFileName, [ref]$relativePath)) {
            $relativePath = Resolve-CoverageSourcePath `
                -FileName $reportedFileName `
                -SourceRoots $sourceRoots
            $sourcePathCache.Add($reportedFileName, $relativePath)
        }
        if (-not $relativePath.EndsWith(
                '.cs',
                [StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Contains('/obj/', [StringComparison]::Ordinal) -or
            $relativePath.Contains('/bin/', [StringComparison]::Ordinal)) {
            continue
        }
        $sourceKey = $packageName + ':' + $relativePath
        if (-not $expectedLineHits.ContainsKey($sourceKey)) {
            throw (
                "Coverage report source is outside the authenticated source " +
                "universe: '$sourceKey'.")
        }
        $classRecordKey = $sourceKey + ':' + [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($class.OuterXml)))
        $cachedLineCount = 0
        if ($validatedClassRecords.TryGetValue($classRecordKey, [ref]$cachedLineCount)) {
            $reportLineCount += $cachedLineCount
            $reusedClassRecords++
            continue
        }
        $validatedClassCount++
        $fileHits = $lineHits[$sourceKey]
        $fileCredits = $permittedLineCredits[$sourceKey]
        $fileReportedHits = $reportedLineHits[$sourceKey]
        $lines = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($class.SelectNodes('.//line')))
        if ($lines.Length -gt 0) {
            [void]$reportedModuleDocuments.Add($sourceKey)
        }
        foreach ($line in $lines) {
            $number = 0
            $hits = 0
            if (-not [int]::TryParse(
                    $line.GetAttribute('number'),
                    [Globalization.NumberStyles]::Integer,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$number) -or
                -not [int]::TryParse(
                    $line.GetAttribute('hits'),
                    [Globalization.NumberStyles]::Integer,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$hits) -or
                $number -le 0 -or
                $hits -lt 0) {
                throw (
                    "Coverage report contains a malformed sequence point: " +
                    $report.FullName)
            }
            # Reports repeat the same source lines across test projects. Resolve
            # each module/source/line against its authenticated PDB ranges
            # once per validation, then merge hits for every overlapping start.
            [object]$credits = $null
            if (-not $fileCredits.TryGetValue($number, [ref]$credits)) {
                $credits = Resolve-SharpProofCoverageLineCredits `
                    -Ranges $permittedLineRanges[$sourceKey] `
                    -Number $number -SourcePath $sourceKey
                $fileCredits.Add($number, $credits)
            }
            # Only store direct hits after the same source and PDB-range validation.
            if (-not $fileReportedHits.ContainsKey($number) -or
                $hits -gt $fileReportedHits[$number]) {
                $fileReportedHits[$number] = $hits
            }
            # Zero records still receive full source/PDB validation and remain
            # direct evidence. They cannot increase any nonnegative MAX value.
            if ($hits -gt 0) {
                foreach ($credit in $credits) {
                    if ($hits -gt $fileHits[$credit]) {
                        $fileHits[$credit] = $hits
                    }
                }
            }
            $reportLineCount++
        }
        $validatedClassRecords.Add($classRecordKey, $lines.Length)
    }
    if ($hasProductionPackage -and $reportLineCount -eq 0) {
        throw "Coverage report has no production sequence points: $($report.FullName)"
    }
}

$coverageMergeTimer.Stop()
Write-Verbose ('Coverage phase: name=validation-class-records elapsedMilliseconds={0} validated={1} reused={2}' -f $coverageMergeTimer.ElapsedMilliseconds, $validatedClassCount, $reusedClassRecords)

$frozenHits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($entry in $lineHits.GetEnumerator()) {
    $frozenHits.Add($entry.Key, $entry.Value.ToImmutable())
}
$lineHits = $frozenHits.ToImmutable()
$expectedLineHits = $lineHits
$frozenReportedHits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($entry in $reportedLineHits.GetEnumerator()) {
    $frozenReportedHits.Add($entry.Key, $entry.Value.ToImmutable())
}
$reportedLineHits = $frozenReportedHits.ToImmutable()
$frozenCredits = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
foreach ($entry in $permittedLineCredits.GetEnumerator()) {
    $frozenCredits.Add($entry.Key, $entry.Value.ToImmutable())
}
$permittedLineCredits = $frozenCredits.ToImmutable()
$reportedModuleDocuments = $reportedModuleDocuments.ToImmutable()
$reportFiles = $reportFiles.ToImmutable()

function Test-QualifiedDeclarationDocument {
    param(
        [Parameter(Mandatory = $true)] $Document,
        [Parameter()] $RecordedDocument,
        [Parameter(Mandatory = $true)] [string]$AssemblyName,
        [Parameter(Mandatory = $true)] $ReportedModuleDocuments,
        [Parameter(Mandatory = $true)] [string]$SourcePath
    )
    if ($null -eq $RecordedDocument -or
        -not $ReportedModuleDocuments.Contains($AssemblyName + ':' + [string]$Document.path)) {
        return $false
    }
    foreach ($property in @('sourceHashAlgorithm', 'sourceHash')) {
        if ($null -eq $Document.PSObject.Properties[$property] -or
            $null -eq $RecordedDocument.PSObject.Properties[$property] -or
            [string]$Document.$property -cne [string]$RecordedDocument.$property) {
            return $false
        }
    }
    $bytes = [IO.File]::ReadAllBytes($SourcePath)
    $hash = switch ([string]$Document.sourceHashAlgorithm) {
        '8829d00f-11b8-4213-878b-770e8597ac16' { [Security.Cryptography.SHA256]::HashData($bytes) }
        'ff1816ec-aa5e-4d10-87f7-6f4963833460' { [Security.Cryptography.SHA1]::HashData($bytes) }
        default { return $false }
    }
    return [Convert]::ToHexString($hash).ToLowerInvariant() -ceq [string]$Document.sourceHash
}

function Get-QualifiedDeclarationQueries {
    param(
        [Parameter(Mandatory = $true)] [string]$Path,
        [Parameter(Mandatory = $true)] [string]$SourcePath,
        [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [Collections.IEnumerable]$Modules,
        [Parameter(Mandatory = $true)] $RecordedAuthority,
        [Parameter(Mandatory = $true)] $Projects,
        [Parameter(Mandatory = $true)] $ReportedModuleDocuments
    )
    if ($Modules -is [Collections.Immutable.ImmutableArray[object]] -and $Modules.IsDefault) { throw [ArgumentException]::new('Modules must be initialized.', 'Modules') }
    $moduleSnapshot = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($Modules))
    $queries = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($module in $moduleSnapshot) {
        $project = $Projects[[string]$module.project]
        if (@($project.compile | Where-Object { $_.path -ceq $Path }).Count -eq 0) { continue }
        $documents = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($module.documents | Where-Object { $_.path -ceq $Path }))
        if ($documents.Length -ne 1) { return }
        $document = $documents[0]
        $recordedModules = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($RecordedAuthority.modules | Where-Object {
                    $_.project -ceq $module.project -and $_.assemblyName -ceq $module.assemblyName -and
                    $_.moduleMvid -ceq $module.moduleMvid -and $_.pdbCodeViewGuid -ceq $module.pdbCodeViewGuid
                }))
        if ($recordedModules.Length -ne 1) { return }
        $recordedDocuments = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($recordedModules[0].documents | Where-Object { $_.path -ceq $Path }))
        if ($recordedDocuments.Length -ne 1 -or
            -not (Test-QualifiedDeclarationDocument -Document $document -RecordedDocument $recordedDocuments[0] `
                    -AssemblyName ([string]$module.assemblyName) -ReportedModuleDocuments $ReportedModuleDocuments `
                    -SourcePath $SourcePath)) { return }
        $queries.Add(([ordered]@{
                    path = $Path; sourcePath = $SourcePath
                    languageVersion     = [string]$project.parseOptions.languageVersion
                    preprocessorSymbols = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($project.parseOptions.preprocessorSymbols))
                }).AsReadOnly())
    }
    return $queries.ToImmutable()
}

function Test-OmitDeclarationLine {
    param(
        [Parameter(Mandatory = $true)] [int]$Number,
        [Parameter(Mandatory = $true)] $DeclarationLines,
        [Parameter(Mandatory = $true)] [AllowEmptyCollection()] [Collections.IEnumerable]$Ranges
    )
    if ($Ranges -is [Collections.Immutable.ImmutableArray[object]] -and $Ranges.IsDefault) { throw [ArgumentException]::new('Ranges must be initialized.', 'Ranges') }
    $rangeSnapshot = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($Ranges))
    # The syntax classifier admits only declaration tokens with no executable
    # initializer, accessor, arrow body, or constructor initializer. A PDB
    # range credited to another line may overlap that header, especially for
    # async state machines; the header itself has no executable sequence point.
    # Retain a range only when its ordinary credit belongs to this exact line.
    return $DeclarationLines.Contains($Number) -and @($rangeSnapshot | Where-Object {
            $_.creditLine -eq $Number
        }).Count -eq 0
}

function Measure-Coverage {
    param(
        [Parameter(Mandatory = $true)]
        [Collections.Immutable.ImmutableArray[string]]$Paths
    )

    if ($Paths.IsDefault) { throw [ArgumentException]::new('Paths must be initialized.', 'Paths') }
    $covered = 0
    $coverable = 0
    $uncovered = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($path in $Paths) {
        foreach ($entry in $lineHits[$path].GetEnumerator()) {
            $coverable++
            if ($entry.Value -gt 0) {
                $covered++
            }
            else {
                $uncovered.Add("${path}:$($entry.Key)")
            }
        }
    }
    $percent = if ($coverable -eq 0) {
        100.0
    }
    else {
        100.0 * $covered / $coverable
    }
    return ([ordered]@{
            coveredLines   = $covered
            coverableLines = $coverable
            linePercent    = [Math]::Round($percent, 2)
            uncoveredLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $uncovered.ToImmutable()))
        }).AsReadOnly()
}

$projects = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$aggregateCovered = 0
$aggregateCoverable = 0
foreach ($property in $baseline.projects.PSObject.Properties |
        Sort-Object Name) {
    $projectName = $property.Name
    $authorityProject = $null
    if (-not $authorityProjectsByName.TryGetValue(
            $projectName,
            [ref]$authorityProject)) {
        throw (
            "Coverage authority expected exactly one production project " +
            "named '$projectName', but found none.")
    }
    # Include every authenticated PDB document in this project's Compile
    # inventory, including linked and generated sources already in the universe.
    $paths = $projectModuleKeys[$projectName]
    $paths = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $paths))
    if ($paths.Length -eq 0) {
        throw "Coverage did not contain production project '$projectName'."
    }
    $measurement = Measure-Coverage -Paths $paths
    # Aggregate sums assembly/source/start obligations. Shared source compiled
    # into two production assemblies contributes once for each compilation.
    $aggregateCovered += $measurement.coveredLines
    $aggregateCoverable += $measurement.coverableLines
    $minimum = [double]$property.Value
    $projects.Add(([ordered]@{
                name               = $projectName
                coveredLines       = $measurement.coveredLines
                coverableLines     = $measurement.coverableLines
                linePercent        = $measurement.linePercent
                minimumLinePercent = $minimum
                uncoveredLines     = $measurement.uncoveredLines
                passed             = Test-CoverageFloor `
                    -CoveredLines $measurement.coveredLines `
                    -CoverableLines $measurement.coverableLines `
                    -LinePercent $measurement.linePercent `
                    -Minimum $minimum
            }).AsReadOnly())
}

$projects = $projects.ToImmutable()
$aggregatePercent = if ($aggregateCoverable -eq 0) {
    100.0
}
else {
    100.0 * $aggregateCovered / $aggregateCoverable
}
$aggregate = ([ordered]@{
        coveredLines   = $aggregateCovered
        coverableLines = $aggregateCoverable
        linePercent    = [Math]::Round($aggregatePercent, 2)
    }).AsReadOnly()
$aggregateMinimum = [double]$baseline.minimumAggregateLinePercent
$aggregatePassed =
Test-CoverageFloor `
    -CoveredLines $aggregate.coveredLines `
    -CoverableLines $aggregate.coverableLines `
    -LinePercent $aggregate.linePercent `
    -Minimum $aggregateMinimum

$changedTcb = ([ordered]@{
        comparisonRef           = $comparisonCommit
        canonicalFiles          = 0
        changedFiles            = 0
        coverageFiles           = 0
        metadataFiles           = 0
        changedMetadataFiles    = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@()))
        coveredLines            = 0
        omittedDeclarationLines = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@()))
        unmappedLines           = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@()))
        coverableLines          = 0
        linePercent             = 100.0
        minimumLinePercent      = [double]$baseline.minimumChangedTcbLinePercent
        declarationOnlyFiles    = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@()))
        nonCoverableFiles       = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@()))
        uncoveredLines          = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@()))
        passed                  = $true
    }).AsReadOnly()
if (-not [string]::IsNullOrWhiteSpace($comparisonCommit)) {
    $contractPath = Join-Path $repositoryRoot 'eng\acceptance\contract.json'
    $contract = Get-Content -LiteralPath $contractPath -Raw |
        ConvertFrom-Json
    $canonicalTcbPaths = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-SharpProofTcbPaths -Contract $contract -IncludeAcceptanceContract -ProductionInventory $recomputedAuthority))
    $coverageTcbPaths = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            $canonicalTcbPaths |
                Where-Object {
                    $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)
                }))
    $coverageTcbFiles = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    foreach ($coveragePath in $coverageTcbPaths) {
        [void]$coverageTcbFiles.Add($coveragePath)
    }
    foreach ($declarationOnlyPath in $declarationOnlyTcbFiles) {
        if (-not $coverageTcbFiles.Contains($declarationOnlyPath) -or
            -not (Test-Path -LiteralPath (
                    Join-Path $repositoryRoot $declarationOnlyPath) -PathType Leaf)) {
            throw "Declaration-only TCB coverage path is not canonical: '$declarationOnlyPath'."
        }
    }
    $coverageTcbFiles = $coverageTcbFiles.ToImmutable()
    $diffTarget = "$comparisonCommit...HEAD"
    if ($IncludeWorkingTree) {
        $mergeBaseOutput = Invoke-SharpProofGitText `
            -RepositoryRoot $repositoryRoot `
            -Arguments @('merge-base', $comparisonCommit, 'HEAD') `
            -FailureMessage (
            "Could not resolve the merge base for comparison ref '$ComparisonRef'.")
        $mergeBase = $mergeBaseOutput.Trim()
        if ([string]::IsNullOrWhiteSpace($mergeBase) -or
            $mergeBase.Contains("`n") -or
            $mergeBase.Contains("`r")) {
            throw "Could not resolve the merge base for comparison ref '$ComparisonRef'."
        }
        $diffTarget = $mergeBase
    }
    $changedTcbFiles = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string](
        [StringComparer]::Ordinal)
    $changedFileArguments = @(
        'diff',
        '--name-only',
        '-z',
        '--find-renames',
        '-l10000',
        $diffTarget,
        '--'
    )
    $changedFileOutput = Invoke-SharpProofGitText `
        -RepositoryRoot $repositoryRoot `
        -Arguments $changedFileArguments `
        -FailureMessage (
        "git changed-file enumeration failed for comparison ref '$ComparisonRef'.")
    $changedFileNames = $changedFileOutput -split ([string][char]0)
    foreach ($changedFileName in $changedFileNames) {
        $normalized = ([string]$changedFileName).Replace('\', '/')
        if (-not [string]::IsNullOrWhiteSpace($normalized) -and $normalized -cin $canonicalTcbPaths) {
            [void]$changedTcbFiles.Add($normalized)
        }
    }
    $changedTcbFiles = $changedTcbFiles.ToImmutable()
    $changedLines = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    $patch = if ($changedTcbFiles.Count -eq 0) {
        ''
    }
    else {
        Invoke-SharpProofGitText `
            -RepositoryRoot $repositoryRoot `
            -Arguments (@(
                '-c',
                'core.quotePath=false',
                'diff',
                '--unified=0',
                '--find-renames',
                '-l10000',
                $diffTarget,
                '--'
            )) `
            -FailureMessage (
            "git diff failed for changed TCB paths for comparison ref '$ComparisonRef'.")
    }
    $currentPath = $null
    foreach ($line in $patch.Split([char]10)) {
        $line = $line.TrimEnd([char]13)
        if ($line.StartsWith('--- a/', [StringComparison]::Ordinal)) {
            $currentPath = $line.Substring(6)
            continue
        }
        if ($line.StartsWith('+++ b/', [StringComparison]::Ordinal)) {
            $currentPath = $line.Substring(6)
            continue
        }
        $match = [Text.RegularExpressions.Regex]::Match(
            $line,
            '^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,(?<count>\d+))? @@')
        if (-not $match.Success) {
            continue
        }
        if ($null -eq $currentPath) {
            throw (
                "git diff did not identify the changed TCB path for comparison " +
                "ref '$ComparisonRef'.")
        }
        if (-not $changedTcbFiles.Contains($currentPath)) { continue }
        $start = [int]$match.Groups['start'].Value
        $count = if ($match.Groups['count'].Success) {
            [int]$match.Groups['count'].Value
        }
        else {
            1
        }
        if (-not $changedLines.ContainsKey($currentPath)) {
            $changedLines[$currentPath] =
            [Collections.Immutable.ImmutableHashSet]::CreateBuilder[int]()
        }
        for ($number = $start;
            $number -lt $start + $count;
            $number++) {
            [void]$changedLines[$currentPath].Add($number)
        }
    }
    $frozenChangedLines = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    foreach ($entry in $changedLines.GetEnumerator()) {
        $frozenChangedLines.Add($entry.Key, $entry.Value.ToImmutable())
    }
    $changedLines = $frozenChangedLines.ToImmutable()
    $changedCovered = 0
    $changedCoverable = 0
    $changedMetadataInputs = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($changedTcbFiles | Where-Object { -not $coverageTcbFiles.Contains($_) }))
    $changedMetadataFiles = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $changedMetadataInputs))
    $nonCoverableChangedFiles =
    [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $declarationOnlyChangedFiles =
    [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $uncoveredChangedLines = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $omittedDeclarationLines = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $unmappedLines = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $declarationQueries = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($path in $changedTcbFiles) {
        if (-not $coverageTcbFiles.Contains($path)) { continue }
        $sourcePath = Join-Path $repositoryRoot $path
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { continue }
        $queries = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(Get-QualifiedDeclarationQueries -Path $path -SourcePath $sourcePath `
                    -Modules $expectedAuthorityModules -RecordedAuthority $recordedAuthority `
                    -Projects $authorityProjectsByName -ReportedModuleDocuments $reportedModuleDocuments))
        foreach ($query in $queries) { $declarationQueries.Add($query) }
    }
    $declarationQueries = $declarationQueries.ToImmutable()
    $declarationLinesByPath = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    $classifications = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(& (Join-Path $PSScriptRoot 'Read-SharpProofDeclarationLines.ps1') `
                -RepositoryRoot $repositoryRoot -Documents ([object[]]@($declarationQueries))))
    $qualifiedUnmappedPaths = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    $invalidSyntaxPaths = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    foreach ($classification in $classifications) {
        $path = [string]$classification.path
        if ($classification.validSyntax) { [void]$qualifiedUnmappedPaths.Add($path) }
        else { [void]$invalidSyntaxPaths.Add($path) }
        $numbers = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[int]()
        foreach ($number in $classification.declarationLines) { [void]$numbers.Add([int]$number) }
        if ($declarationLinesByPath.ContainsKey($path)) {
            $declarationLinesByPath[$path].IntersectWith($numbers)
        }
        else { $declarationLinesByPath[$path] = $numbers }
    }
    # Every owning compilation must qualify; invalid syntax in any parse context
    # also prevents a path from being treated as unmeasurable.
    $qualifiedUnmappedPaths.ExceptWith($invalidSyntaxPaths)
    $frozenDeclarationLines = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::Ordinal)
    foreach ($entry in $declarationLinesByPath.GetEnumerator()) {
        $frozenDeclarationLines.Add($entry.Key, $entry.Value.ToImmutable())
    }
    $declarationLinesByPath = $frozenDeclarationLines.ToImmutable()
    foreach ($changedPath in $changedTcbFiles) {
        if (-not $coverageTcbFiles.Contains($changedPath)) {
            # The canonical union also contains metadata, such as the
            # acceptance contract. Metadata changes participate in the
            # changed-TCB selection, but have no C# line
            # sequence points. Record them explicitly instead of treating
            # them as missing coverage or silently dropping them.
            continue
        }
        $sourcePath = Join-Path $repositoryRoot (
            $changedPath.Replace(
                '/',
                [string][IO.Path]::DirectorySeparatorChar))
        $sourceLines = if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            , [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-Content -LiteralPath $sourcePath))
        }
        else {
            , [Collections.Immutable.ImmutableArray[string]]::Empty
        }
        $addedLines = if ($changedLines.ContainsKey($changedPath)) {
            , $changedLines[$changedPath]
        }
        else {
            , [Collections.Immutable.ImmutableHashSet[int]]::Empty
        }
        if (-not (Test-HasAddedSemanticSourceLines -SourceLines $sourceLines -AddedLines $addedLines)) {
            # Keep the changed path in reporting, but deletion-only and
            # added-trivia-only changes have no new executable line to credit.
            continue
        }
        $changedSourceKeys = if ($compileModuleKeys.ContainsKey($changedPath)) {
            , $compileModuleKeys[$changedPath]
        }
        else {
            , [Collections.Immutable.ImmutableArray[string]]::Empty
        }
        if (-not $changedLines.ContainsKey($changedPath) -or
            $changedLines[$changedPath].Count -eq 0 -or
            $changedSourceKeys.Length -eq 0) {
            # Coverlet emits zero-hit sequence points for executable code.
            # A changed file that has no corresponding report entry is not
            # trusted as covered. This also fails closed for non-C# TCB files,
            # deleted files, and binary changes until explicit evidence is
            # supplied by a separate gate.
            if ($declarationOnlyTcbFiles.Contains($changedPath)) {
                $declarationOnlyChangedFiles.Add($changedPath)
            }
            else {
                $nonCoverableChangedFiles.Add($changedPath)
            }
            continue
        }
        foreach ($sourceKey in $changedSourceKeys) {
            $coveragePath = if ($changedSourceKeys.Length -gt 1) { $sourceKey } else { $changedPath }
            if (-not $lineHits.ContainsKey($sourceKey)) {
                # Compile ownership without a module PDB document cannot borrow
                # declaration or execution evidence from another compilation.
                if ($declarationOnlyTcbFiles.Contains($changedPath)) {
                    $declarationOnlyChangedFiles.Add($coveragePath)
                }
                else { $nonCoverableChangedFiles.Add($coveragePath) }
                continue
            }
            $fileHits = $lineHits[$sourceKey]
            foreach ($number in $changedLines[$changedPath]) {
                if ($number -gt 0 -and $number -le $sourceLines.Length -and
                    (Test-ClearlyNonSemanticSourceLine `
                        -Line $sourceLines[$number - 1])) {
                    # Clearly trivia-only and brace-only changes do not alter
                    # trusted execution and need no sequence point.
                    continue
                }
                $changedLineHits = if ($fileHits.ContainsKey($number)) {
                    $fileHits[$number]
                }
                else {
                    $rangeHitFound = $false
                    [double]$rangeHitMaximum = 0
                    foreach ($range in $permittedLineRanges[$sourceKey]) {
                        if ($range.creditLine -gt 0 -and $number -ge $range.startLine -and $number -le $range.endLine) {
                            $candidateHit = $fileHits[$range.creditLine]
                            if (-not $rangeHitFound -or $candidateHit -gt $rangeHitMaximum) {
                                $rangeHitMaximum = $candidateHit
                                $rangeHitFound = $true
                            }
                        }
                    }
                    if ($rangeHitFound) { $rangeHitMaximum }
                    else { $null }
                }
                # Compiler-generated accessors have real PDB ranges but no ordinary
                # creditable start. A genuine direct record can still cover its exact
                # physical changed-TCB line without expanding the ordinary universe.
                $directHits = $reportedLineHits[$sourceKey]
                if ($directHits.ContainsKey($number) -and
                    ($null -eq $changedLineHits -or
                    $directHits[$number] -gt $changedLineHits)) {
                    $changedLineHits = $directHits[$number]
                }
                if ($null -eq $changedLineHits) {
                    if ($declarationLinesByPath.ContainsKey($changedPath) -and
                        (Test-OmitDeclarationLine -Number $number `
                            -DeclarationLines $declarationLinesByPath[$changedPath] `
                            -Ranges $permittedLineRanges[$sourceKey])) {
                        $omittedDeclarationLines.Add("${coveragePath}:$number")
                        continue
                    }
                    if ($qualifiedUnmappedPaths.Contains($changedPath) -and
                        $number -gt 0 -and $number -le $sourceLines.Length -and
                        -not $directHits.ContainsKey($number) -and
                        @($permittedLineRanges[$sourceKey] | Where-Object {
                                $number -ge $_.startLine -and $number -le $_.endLine
                            }).Count -eq 0) {
                        # This authenticated document has no compiler sequence-point
                        # span or collector record for the line. Report it separately;
                        # absence of a measurement is not executed coverage. Generated
                        # and zero-hit spans still participate in the denominator.
                        $unmappedLines.Add("${coveragePath}:$number")
                        continue
                    }
                    # Missing qualification or any unhit executable/generated span
                    # remains an uncovered obligation.
                    $identifier = "${coveragePath}:$number"
                    $changedCoverable++
                    $uncoveredChangedLines.Add($identifier)
                    continue
                }
                $changedCoverable++
                if ($changedLineHits -gt 0) {
                    $changedCovered++
                }
                else {
                    $uncoveredChangedLines.Add("${coveragePath}:$number")
                }
            }
        }
    }
    $nonCoverableChangedFiles = $nonCoverableChangedFiles.ToImmutable()
    $declarationOnlyChangedFiles = $declarationOnlyChangedFiles.ToImmutable()
    $uncoveredChangedLines = $uncoveredChangedLines.ToImmutable()
    $omittedDeclarationLines = $omittedDeclarationLines.ToImmutable()
    $unmappedLines = $unmappedLines.ToImmutable()
    $changedPercent = if ($changedCoverable -eq 0) {
        100.0
    }
    else {
        100.0 * $changedCovered / $changedCoverable
    }
    $changedPercent = [Math]::Round($changedPercent, 2)
    $changedTcb = ([ordered]@{
            comparisonRef           = $comparisonCommit
            canonicalFiles          = $canonicalTcbPaths.Length
            changedFiles            = $changedTcbFiles.Count
            coverageFiles           = $coverageTcbPaths.Length
            metadataFiles           = $canonicalTcbPaths.Length - $coverageTcbPaths.Length
            changedMetadataFiles    = $changedMetadataFiles
            coveredLines            = $changedCovered
            omittedDeclarationLines = ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $omittedDeclarationLines)))
            unmappedLines           = ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $unmappedLines)))
            coverableLines          = $changedCoverable
            linePercent             = $changedPercent
            minimumLinePercent      = [double]$baseline.minimumChangedTcbLinePercent
            declarationOnlyFiles    = ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $declarationOnlyChangedFiles)))
            nonCoverableFiles       = ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $nonCoverableChangedFiles)))
            uncoveredLines          = ([Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-OrdinalSortedUniqueStrings -Values $uncoveredChangedLines)))
            passed                  = $nonCoverableChangedFiles.Count -eq 0 -and
            (Test-CoverageFloor `
                -CoveredLines $changedCovered `
                -CoverableLines $changedCoverable `
                -LinePercent $changedPercent `
                -Minimum ([double]$baseline.minimumChangedTcbLinePercent))
        }).AsReadOnly()
}

$summary = ([ordered]@{
        schemaVersion = 1
        commit        = Invoke-SharpProofGitText `
            -RepositoryRoot $repositoryRoot `
            -Arguments @('rev-parse', 'HEAD') `
            -FailureMessage 'Could not resolve the current repository commit.' `
            -TrimOutput
        reportCount   = $reports.Length
        reportFiles   = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($reportFiles | Sort-Object { $_.path })))
        authority     = ([ordered]@{
                schemaVersion      = 1
                commit             = [string]$recomputedAuthority.commit
                moduleCount        = $expectedAuthorityModules.Length
                sequencePointCount = $expectedSequencePointCount
            }).AsReadOnly()
        aggregate     = ([ordered]@{
                coveredLines       = $aggregate.coveredLines
                coverableLines     = $aggregate.coverableLines
                linePercent        = $aggregate.linePercent
                minimumLinePercent = $aggregateMinimum
                passed             = $aggregatePassed
            }).AsReadOnly()
        projects      = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($projects)))
        changedTcb    = $changedTcb
        passed        = $aggregatePassed -and
        @($projects | Where-Object { -not $_.passed }).Count -eq 0 -and
        $changedTcb.passed
    }).AsReadOnly()
if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
    $fullSummaryPath = [IO.Path]::GetFullPath($SummaryPath)
    $summaryDirectory = [IO.Path]::GetDirectoryName($fullSummaryPath)
    if (-not (Test-Path -LiteralPath $summaryDirectory)) {
        New-Item -ItemType Directory -Path $summaryDirectory |
            Out-Null
    }
    $json = ($summary | ConvertTo-Json -Depth 8) -replace "`r`n", "`n"
    [IO.File]::WriteAllText(
        $fullSummaryPath,
        $json + "`n",
        [Text.UTF8Encoding]::new($false))
}
$summary | ConvertTo-Json -Depth 8
if (-not $summary.passed -and -not $ReportOnly) {
    throw 'SharpProof coverage regressed below its checked-in baseline.'
}
