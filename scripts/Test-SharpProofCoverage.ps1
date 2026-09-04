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

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Get-SharpProofTcbPaths.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force

function ConvertTo-OrdinalSortedArray {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Values
    )

    $items = [Collections.Generic.List[string]]::new()
    foreach ($value in $Values) {
        $items.Add([string]$value)
    }
    $items.Sort([StringComparer]::Ordinal)
    return $items.ToArray()
}
function Test-ClearlyNonSemanticSourceLine {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Line
    )

    $trimmed = $Line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed -in @('{', '}')) {
        return $true
    }
    if ($trimmed.StartsWith('//', [StringComparison]::Ordinal)) {
        return $true
    }
    return $trimmed.StartsWith('/*', [StringComparison]::Ordinal) -and
        $trimmed.EndsWith('*/', [StringComparison]::Ordinal)
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
    [Collections.Generic.HashSet[string]]::new(
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
$reports = @(
    Get-ChildItem `
        -LiteralPath $resolvedCoverageRoot `
        -Recurse `
        -Filter '*.cobertura.xml' `
        -File |
        Where-Object {
            [IO.Path]::GetRelativePath(
                $resolvedCoverageRoot,
                $_.FullName).Replace('\', '/') -cnotmatch '(^|/)In(/|$)'
        } |
        Sort-Object FullName
)
if ($reports.Count -eq 0) {
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
$recomputedAuthorityJson = & $authorityScript -RepositoryRoot $repositoryRoot -Configuration Release -RequirePdb
if ($LASTEXITCODE -ne 0) {
    throw 'Production inventory authority could not be recomputed from current MSBuild/PDB inputs.'
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
$authorityProjectNames = @(
    $recomputedAuthority.projects |
        ForEach-Object { [string]$_.name } |
        Sort-Object)
$baselineProjectNames = @(
    $baseline.projects.PSObject.Properties |
        ForEach-Object { [string]$_.Name } |
        Sort-Object)
if (($authorityProjectNames -join [Environment]::NewLine) -cne
    ($baselineProjectNames -join [Environment]::NewLine)) {
    throw (
        'Coverage baseline project floors do not match the independently ' +
        'evaluated production inventory.')
}
$authorityProjectsByName = [Collections.Generic.Dictionary[string,
    Collections.Generic.List[object]]]::new([StringComparer]::Ordinal)
foreach ($authorityProject in $recomputedAuthority.projects) {
    $authorityProjectName = [string]$authorityProject.name
    $authorityProjectMatches = $null
    if (-not $authorityProjectsByName.TryGetValue(
            $authorityProjectName,
            [ref]$authorityProjectMatches)) {
        $authorityProjectMatches = [Collections.Generic.List[object]]::new()
        $authorityProjectsByName.Add(
            $authorityProjectName,
            $authorityProjectMatches)
    }
    $authorityProjectMatches.Add($authorityProject)
}
$expectedAuthorityModules = @($recomputedAuthority.modules | Sort-Object project)
$expectedModuleIdentities = @(
    $expectedAuthorityModules |
        ForEach-Object {
            [string]$_.project + ':' + [string]$_.assemblyName + ':' +
                [string]$_.moduleMvid + ':' + [string]$_.pdbCodeViewGuid
        } |
        Sort-Object)
$expectedModuleIdentityText = $expectedModuleIdentities -join ','
$expectedAssemblyNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($module in $expectedAuthorityModules) {
    if (-not $expectedAssemblyNames.Add([string]$module.assemblyName)) {
        throw "Coverage authority has a duplicate assembly name '$($module.assemblyName)'."
    }
}
$expectedLineHits = [Collections.Generic.Dictionary[string,
    Collections.Generic.Dictionary[int, int]]]::new(
        [StringComparer]::Ordinal)
$permittedLineRanges = [Collections.Generic.Dictionary[string,
    Collections.Generic.List[object]]]::new([StringComparer]::Ordinal)
$expectedSequencePointCount = 0
foreach ($module in $expectedAuthorityModules) {
    foreach ($document in @($module.documents | Sort-Object path)) {
        $path = [string]$document.path
        if ($expectedLineHits.ContainsKey($path)) {
            $fileLines = $expectedLineHits[$path]
        }
        else {
            $fileLines = [Collections.Generic.Dictionary[int, int]]::new()
            $expectedLineHits[$path] = $fileLines
        }
        if (-not $permittedLineRanges.ContainsKey($path)) {
            $permittedLineRanges[$path] = [Collections.Generic.List[object]]::new()
        }
        $documentSequencePoints =
            [Collections.Generic.HashSet[int]]::new()
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
        foreach ($range in @($document.sequencePointRanges)) {
            $startLine = [int]$range.startLine
            $endLine = [int]$range.endLine
            if ($startLine -le 0 -or $endLine -lt $startLine) {
                throw (
                    "Coverage authority has an invalid sequence-point range " +
                    "'${path}:$startLine-$endLine'.")
            }
            $permittedLineRanges[$path].Add(
                [pscustomobject]@{
                    startLine = $startLine
                    endLine = $endLine
                    creditLine = if ($documentSequencePoints.Contains($startLine)) {
                        $startLine
                    }
                    else {
                        0
                    }
                })
        }
    }
}
if ($expectedLineHits.Count -eq 0 -or $expectedSequencePointCount -eq 0) {
    throw 'Coverage authority contains no production sequence points.'
}

$lineHits = $expectedLineHits
function Resolve-CoverageSourcePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$SourceRoots
    )

    $normalizedFileName = $FileName.Replace('\', '/')
    $candidates = [Collections.Generic.List[string]]::new()
    if ([IO.Path]::IsPathRooted($normalizedFileName)) {
        $candidates.Add([IO.Path]::GetFullPath($normalizedFileName))
    }
    else {
        $candidates.Add([IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot $normalizedFileName)))
        foreach ($sourceRoot in $SourceRoots) {
            if (-not [string]::IsNullOrWhiteSpace($sourceRoot)) {
                $normalizedSourceRoot = $sourceRoot.Replace('\', '/')
                $candidates.Add([IO.Path]::GetFullPath(
                    (Join-Path $normalizedSourceRoot $normalizedFileName)))
            }
        }
    }
    foreach ($candidate in $candidates) {
        if ($candidate.StartsWith(
                $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate.Substring($repositoryRoot.Length + 1).
                Replace('\', '/')
        }
    }
    throw (
        "Coverage report source document is foreign or missing: '$FileName'.")
}

$reportFiles = [Collections.Generic.List[object]]::new()
foreach ($report in $reports) {
    $reportFiles.Add([pscustomobject][ordered]@{
            path = [IO.Path]::GetRelativePath(
                $resolvedCoverageRoot,
                $report.FullName).Replace('\', '/')
            bytes = [int64]$report.Length
        })
    [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
    $authorityNodes = @(
        $document.SelectNodes('/coverage/sharpProofAuthority'))
    if ($authorityNodes.Count -ne 1) {
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
    $sourceRoots = @(
        $document.SelectNodes('/coverage/sources/source') |
            ForEach-Object { [string]$_.InnerText }
    )
    $classes = @($document.SelectNodes('//class'))
    if ($classes.Count -eq 0) {
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
        if (-not $expectedAssemblyNames.Contains([string]$package.name)) {
            continue
        }
        $hasProductionPackage = $true
        if (-not $class.HasAttribute('filename')) {
            throw "Coverage report class has no source filename: $($report.FullName)"
        }
        $reportedFileName = ([string]$class.filename).Replace('\', '/')
        if ($reportedFileName.Contains('/obj/', [StringComparison]::Ordinal) -or
            $reportedFileName.Contains('/bin/', [StringComparison]::Ordinal)) {
            continue
        }
        $relativePath = Resolve-CoverageSourcePath `
            -FileName $reportedFileName `
            -SourceRoots $sourceRoots
        if (-not $relativePath.EndsWith(
                '.cs',
                [StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Contains('/obj/', [StringComparison]::Ordinal) -or
            $relativePath.Contains('/bin/', [StringComparison]::Ordinal)) {
            continue
        }
        if (-not $expectedLineHits.ContainsKey($relativePath)) {
            throw (
                "Coverage report source is outside the authenticated source " +
                "universe: '$relativePath'.")
        }
        $fileHits = $lineHits[$relativePath]
        $lines = @($class.SelectNodes('.//line'))
        foreach ($line in $lines) {
            $number = 0
            $hits = 0
            if (-not $line.HasAttribute('number') -or
                -not $line.HasAttribute('hits') -or
                -not [int]::TryParse(
                    [string]$line.number,
                    [Globalization.NumberStyles]::Integer,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$number) -or
                -not [int]::TryParse(
                    [string]$line.hits,
                    [Globalization.NumberStyles]::Integer,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$hits) -or
                $number -le 0 -or
                $hits -lt 0) {
                throw (
                    "Coverage report contains a malformed sequence point: " +
                    $report.FullName)
            }
            $isPermittedLine = $false
            foreach ($range in $permittedLineRanges[$relativePath]) {
                if ($number -ge $range.startLine -and
                    $number -le $range.endLine) {
                    $isPermittedLine = $true
                    if ($range.creditLine -gt 0 -and
                        $hits -gt $fileHits[$range.creditLine]) {
                        $fileHits[$range.creditLine] = $hits
                    }
                }
            }
            if (-not $isPermittedLine) {
                throw (
                    "Coverage report sequence point is outside the authenticated " +
                    "PDB universe: '${relativePath}:$number'.")
            }
            $reportLineCount++
        }
    }
    if ($hasProductionPackage -and $reportLineCount -eq 0) {
        throw "Coverage report has no production sequence points: $($report.FullName)"
    }
}

function Measure-Coverage {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    $covered = 0
    $coverable = 0
    foreach ($path in $Paths) {
        foreach ($hits in $lineHits[$path].Values) {
            $coverable++
            if ($hits -gt 0) {
                $covered++
            }
        }
    }
    $percent = if ($coverable -eq 0) {
        100.0
    }
    else {
        100.0 * $covered / $coverable
    }
    return [pscustomobject][ordered]@{
        coveredLines = $covered
        coverableLines = $coverable
        linePercent = [Math]::Round($percent, 2)
    }
}

$projects = [Collections.Generic.List[object]]::new()
$aggregateCovered = 0
$aggregateCoverable = 0
foreach ($property in $baseline.projects.PSObject.Properties |
        Sort-Object Name) {
    $projectName = $property.Name
    $authorityProjects = $null
    $authorityProjectCount = if ($authorityProjectsByName.TryGetValue(
            $projectName,
            [ref]$authorityProjects)) {
        $authorityProjects.Count
    }
    else {
        0
    }
    if ($authorityProjectCount -ne 1) {
        throw (
            "Coverage authority expected exactly one production project " +
            "named '$projectName', but found $authorityProjectCount.")
    }
    $projectPath = [string]$authorityProjects[0].projectPath
    $projectDirectory =
        [IO.Path]::GetDirectoryName($projectPath).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($projectDirectory)) {
        throw "Coverage authority project has no directory: '$projectPath'."
    }
    $prefix = $projectDirectory.TrimEnd('/') + '/'
    $paths = @(
        $lineHits.Keys |
            Where-Object {
                $_.StartsWith(
                    $prefix,
                    [StringComparison]::Ordinal)
            }
    )
    $paths = @(ConvertTo-OrdinalSortedArray -Values $paths)
    if ($paths.Count -eq 0) {
        throw "Coverage did not contain production project '$projectName'."
    }
    $measurement = Measure-Coverage -Paths $paths
    $aggregateCovered += $measurement.coveredLines
    $aggregateCoverable += $measurement.coverableLines
    $minimum = [double]$property.Value
    $projects.Add([pscustomobject][ordered]@{
        name = $projectName
        coveredLines = $measurement.coveredLines
        coverableLines = $measurement.coverableLines
        linePercent = $measurement.linePercent
        minimumLinePercent = $minimum
        passed = $measurement.linePercent + 0.005 -ge $minimum
    })
}

$aggregatePercent = if ($aggregateCoverable -eq 0) {
    100.0
}
else {
    100.0 * $aggregateCovered / $aggregateCoverable
}
$aggregate = [pscustomobject][ordered]@{
    coveredLines = $aggregateCovered
    coverableLines = $aggregateCoverable
    linePercent = [Math]::Round($aggregatePercent, 2)
}
$aggregateMinimum = [double]$baseline.minimumAggregateLinePercent
$aggregatePassed =
    $aggregate.linePercent + 0.005 -ge $aggregateMinimum

$changedTcb = [pscustomobject][ordered]@{
    comparisonRef = $comparisonCommit
    canonicalFiles = 0
    changedFiles = 0
    coverageFiles = 0
    metadataFiles = 0
    changedMetadataFiles = @()
    coveredLines = 0
    coverableLines = 0
    linePercent = 100.0
    minimumLinePercent = [double]$baseline.minimumChangedTcbLinePercent
    declarationOnlyFiles = @()
    nonCoverableFiles = @()
    uncoveredLines = @()
    passed = $true
}
if (-not [string]::IsNullOrWhiteSpace($comparisonCommit)) {
    $contractPath = Join-Path $repositoryRoot 'eng\acceptance\contract.json'
    $contract = Get-Content -LiteralPath $contractPath -Raw |
        ConvertFrom-Json
    $canonicalTcbPaths = @(Get-SharpProofTcbPaths -Contract $contract -IncludeAcceptanceContract -ProductionInventory $recomputedAuthority)
    $coverageTcbPaths = @(
        $canonicalTcbPaths |
            Where-Object {
                $_.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)
            })
    $coverageTcbFiles = [Collections.Generic.HashSet[string]]::new(
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
    $changedTcbFiles = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $changedFileArguments = @(
        'diff',
        '--name-only',
        '-z',
        '--no-renames',
        $diffTarget,
        '--'
    ) + @($canonicalTcbPaths)
    $changedFileOutput = Invoke-SharpProofGitText `
        -RepositoryRoot $repositoryRoot `
        -Arguments $changedFileArguments `
        -FailureMessage (
            "git changed-file enumeration failed for comparison ref '$ComparisonRef'.")
    $changedFileNames = $changedFileOutput -split ([string][char]0)
    foreach ($changedFileName in $changedFileNames) {
        $normalized = ([string]$changedFileName).Replace('\', '/')
        if (-not [string]::IsNullOrWhiteSpace($normalized)) {
            [void]$changedTcbFiles.Add($normalized)
        }
    }
    $changedLines = [Collections.Generic.Dictionary[string,
        Collections.Generic.HashSet[int]]]::new(
            [StringComparer]::Ordinal)
    foreach ($changedPath in $changedTcbFiles) {
        $patch = Invoke-SharpProofGitText `
            -RepositoryRoot $repositoryRoot `
            -Arguments @(
                'diff',
                '--unified=0',
                '--no-renames',
                $diffTarget,
                '--',
                $changedPath) `
            -FailureMessage (
                "git diff failed for changed TCB path '$changedPath'.")
        foreach ($line in $patch.Split([char]10)) {
            $match = [Text.RegularExpressions.Regex]::Match(
                $line,
                '^@@ -\d+(?:,\d+)? \+(?<start>\d+)(?:,(?<count>\d+))? @@')
            if (-not $match.Success) {
                continue
            }
            $start = [int]$match.Groups['start'].Value
            $count = if ($match.Groups['count'].Success) {
                [int]$match.Groups['count'].Value
            }
            else {
                1
            }
            for ($number = $start;
                $number -lt $start + $count;
                $number++) {
                if (-not $changedLines.ContainsKey($changedPath)) {
                    $changedLines[$changedPath] =
                        [Collections.Generic.HashSet[int]]::new()
                }
                [void]$changedLines[$changedPath].Add($number)
            }
        }
    }
    $changedCovered = 0
    $changedCoverable = 0
    $changedMetadataFiles = @(ConvertTo-OrdinalSortedArray -Values @(
        $changedTcbFiles |
            Where-Object { -not $coverageTcbFiles.Contains($_) }))
    $nonCoverableChangedFiles =
        [Collections.Generic.List[string]]::new()
    $declarationOnlyChangedFiles =
        [Collections.Generic.List[string]]::new()
    $uncoveredChangedLines = [Collections.Generic.List[string]]::new()
    foreach ($changedPath in $changedTcbFiles) {
        if (-not $coverageTcbFiles.Contains($changedPath)) {
            # The canonical union also contains metadata, such as the
            # acceptance contract. Metadata changes participate in the
            # changed-TCB selection, but have no C# line
            # sequence points. Record them explicitly instead of treating
            # them as missing coverage or silently dropping them.
            continue
        }
        if (-not $changedLines.ContainsKey($changedPath) -or
            $changedLines[$changedPath].Count -eq 0 -or
            -not $lineHits.ContainsKey($changedPath)) {
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
        $fileHits = $lineHits[$changedPath]
        $sourcePath = Join-Path $repositoryRoot (
            $changedPath.Replace(
                '/',
                [string][IO.Path]::DirectorySeparatorChar))
        $sourceLines = if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            @(Get-Content -LiteralPath $sourcePath)
        }
        else {
            @()
        }
        foreach ($number in $changedLines[$changedPath]) {
            if ($number -gt 0 -and $number -le $sourceLines.Count -and
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
                $rangeHits = @(
                    $permittedLineRanges[$changedPath] |
                        Where-Object {
                            $_.creditLine -gt 0 -and
                            $number -ge $_.startLine -and
                            $number -le $_.endLine
                        } |
                        ForEach-Object { $fileHits[$_.creditLine] })
                if ($rangeHits.Count -gt 0) {
                    ($rangeHits | Measure-Object -Maximum).Maximum
                }
                else {
                    $null
                }
            }
            if ($null -eq $changedLineHits) {
                # Declarations and initializers can alter trusted execution
                # without receiving a Coverlet sequence point. Treat every
                # unmapped line not proven to be trivia as uncovered.
                $identifier = "${changedPath}:$number"
                $changedCoverable++
                $uncoveredChangedLines.Add($identifier)
                continue
            }
            $changedCoverable++
            if ($changedLineHits -gt 0) {
                $changedCovered++
            }
            else {
                $uncoveredChangedLines.Add("${changedPath}:$number")
            }
        }
    }
    $changedPercent = if ($changedCoverable -eq 0) {
        100.0
    }
    else {
        100.0 * $changedCovered / $changedCoverable
    }
    $changedPercent = [Math]::Round($changedPercent, 2)
    $changedTcb = [pscustomobject][ordered]@{
        comparisonRef = $comparisonCommit
        canonicalFiles = $canonicalTcbPaths.Count
        changedFiles = $changedTcbFiles.Count
        coverageFiles = $coverageTcbPaths.Count
        metadataFiles = $canonicalTcbPaths.Count - $coverageTcbPaths.Count
        changedMetadataFiles = $changedMetadataFiles
        coveredLines = $changedCovered
        coverableLines = $changedCoverable
        linePercent = $changedPercent
        minimumLinePercent = [double]$baseline.minimumChangedTcbLinePercent
        declarationOnlyFiles = @(ConvertTo-OrdinalSortedArray `
            -Values @($declarationOnlyChangedFiles))
        nonCoverableFiles = @(ConvertTo-OrdinalSortedArray `
            -Values @($nonCoverableChangedFiles))
        uncoveredLines = @(ConvertTo-OrdinalSortedArray `
            -Values @($uncoveredChangedLines))
        passed = $nonCoverableChangedFiles.Count -eq 0 -and
            $changedPercent + 0.005 -ge
                [double]$baseline.minimumChangedTcbLinePercent
    }
}

$summary = [pscustomobject][ordered]@{
    schemaVersion = 1
    commit = Invoke-SharpProofGitText `
        -RepositoryRoot $repositoryRoot `
        -Arguments @('rev-parse', 'HEAD') `
        -FailureMessage 'Could not resolve the current repository commit.' `
        -TrimOutput
    reportCount = $reports.Count
    reportFiles = @($reportFiles | Sort-Object path)
    authority = [pscustomobject][ordered]@{
        schemaVersion = 1
        commit = [string]$recomputedAuthority.commit
        moduleCount = $expectedAuthorityModules.Count
        sequencePointCount = $expectedSequencePointCount
    }
    aggregate = [pscustomobject][ordered]@{
        coveredLines = $aggregate.coveredLines
        coverableLines = $aggregate.coverableLines
        linePercent = $aggregate.linePercent
        minimumLinePercent = $aggregateMinimum
        passed = $aggregatePassed
    }
    projects = @($projects)
    changedTcb = $changedTcb
    passed = $aggregatePassed -and
        @($projects | Where-Object { -not $_.passed }).Count -eq 0 -and
        $changedTcb.passed
}
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
