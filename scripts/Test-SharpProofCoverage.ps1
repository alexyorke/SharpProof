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
$reports = @(
    Get-ChildItem `
        -LiteralPath $resolvedCoverageRoot `
        -Recurse `
        -Filter 'coverage.cobertura.xml' `
        -File |
        Sort-Object FullName
)
if ($reports.Count -eq 0) {
    throw "No coverage.cobertura.xml reports were found under $resolvedCoverageRoot."
}

$lineHits = [Collections.Generic.Dictionary[string,
    Collections.Generic.Dictionary[int, int]]]::new(
        [StringComparer]::OrdinalIgnoreCase)

function Resolve-CoverageSourcePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,

        [Parameter(Mandatory = $true)]
        [string[]]$SourceRoots
    )

    $candidates = [Collections.Generic.List[string]]::new()
    if ([IO.Path]::IsPathRooted($FileName)) {
        $candidates.Add([IO.Path]::GetFullPath($FileName))
    }
    else {
        $candidates.Add([IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot $FileName)))
        foreach ($sourceRoot in $SourceRoots) {
            if (-not [string]::IsNullOrWhiteSpace($sourceRoot)) {
                $candidates.Add([IO.Path]::GetFullPath(
                    (Join-Path $sourceRoot $FileName)))
            }
        }
    }
    foreach ($candidate in $candidates) {
        if ($candidate.StartsWith(
                $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate.Substring($repositoryRoot.Length + 1).
                Replace('\', '/')
        }
    }
    return $null
}

foreach ($report in $reports) {
    [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
    $sourceRoots = @(
        $document.SelectNodes('/coverage/sources/source') |
            ForEach-Object { [string]$_.InnerText }
    )
    foreach ($class in $document.SelectNodes('//class[@filename]')) {
        $relativePath = Resolve-CoverageSourcePath `
            -FileName ([string]$class.filename) `
            -SourceRoots $sourceRoots
        if ($null -eq $relativePath -or
            -not $relativePath.EndsWith(
                '.cs',
                [StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Contains('/obj/', [StringComparison]::OrdinalIgnoreCase) -or
            $relativePath.Contains('/bin/', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if (-not $lineHits.ContainsKey($relativePath)) {
            $lineHits[$relativePath] =
                [Collections.Generic.Dictionary[int, int]]::new()
        }
        $fileHits = $lineHits[$relativePath]
        foreach ($line in $class.SelectNodes('.//line[@number][@hits]')) {
            $number = [int]$line.number
            $hits = [int]$line.hits
            if (-not $fileHits.ContainsKey($number) -or
                $hits -gt $fileHits[$number]) {
                $fileHits[$number] = $hits
            }
        }
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
foreach ($property in $baseline.projects.PSObject.Properties |
        Sort-Object Name) {
    $projectName = $property.Name
    $prefix = $projectName + '/'
    $paths = @(
        $lineHits.Keys |
            Where-Object {
                $_.StartsWith(
                    $prefix,
                    [StringComparison]::OrdinalIgnoreCase)
            } |
            Sort-Object
    )
    if ($paths.Count -eq 0) {
        throw "Coverage did not contain production project '$projectName'."
    }
    $measurement = Measure-Coverage -Paths $paths
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

$productionPaths = @(
    $projects |
        ForEach-Object {
            $prefix = $_.name + '/'
            @(
                $lineHits.Keys |
                    Where-Object {
                        $_.StartsWith(
                            $prefix,
                            [StringComparison]::OrdinalIgnoreCase)
                    }
            )
        } |
        Sort-Object -Unique
)
$aggregate = Measure-Coverage -Paths $productionPaths
$aggregateMinimum = [double]$baseline.minimumAggregateLinePercent
$aggregatePassed =
    $aggregate.linePercent + 0.005 -ge $aggregateMinimum

$changedTcb = [pscustomobject][ordered]@{
    comparisonRef = $ComparisonRef
    changedFiles = 0
    coveredLines = 0
    coverableLines = 0
    linePercent = 100.0
    minimumLinePercent = [double]$baseline.minimumChangedTcbLinePercent
    uncoveredLines = @()
    passed = $true
}
if (-not [string]::IsNullOrWhiteSpace($ComparisonRef)) {
    $contractPath = Join-Path $repositoryRoot 'eng\acceptance\contract.json'
    $contract = Get-Content -LiteralPath $contractPath -Raw |
        ConvertFrom-Json
    $tcbPaths = @(
        $contract.trustedComputingBase.components |
            ForEach-Object { @($_.paths) } |
            ForEach-Object { ([string]$_).Replace('\', '/') } |
            Sort-Object -Unique
    )
    $diffTarget = if ($IncludeWorkingTree) {
        $ComparisonRef
    }
    else {
        "$ComparisonRef...HEAD"
    }
    $diff = & git -C $repositoryRoot diff `
        --unified=0 `
        --no-renames `
        $diffTarget `
        -- `
        @tcbPaths
    if ($LASTEXITCODE -ne 0) {
        throw "git diff failed for comparison ref '$ComparisonRef'."
    }
    $changedLines = [Collections.Generic.Dictionary[string,
        Collections.Generic.HashSet[int]]]::new(
            [StringComparer]::OrdinalIgnoreCase)
    $currentPath = $null
    foreach ($line in $diff) {
        if ($line.StartsWith('+++ b/', [StringComparison]::Ordinal)) {
            $currentPath = $line.Substring(6).Replace('\', '/')
            if (-not $changedLines.ContainsKey($currentPath)) {
                $changedLines[$currentPath] =
                    [Collections.Generic.HashSet[int]]::new()
            }
            continue
        }
        if ($null -eq $currentPath) {
            continue
        }
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
        for ($number = $start; $number -lt $start + $count; $number++) {
            [void]$changedLines[$currentPath].Add($number)
        }
    }
    $changedCovered = 0
    $changedCoverable = 0
    $uncoveredChangedLines = [Collections.Generic.List[string]]::new()
    foreach ($entry in $changedLines.GetEnumerator()) {
        if ($entry.Value.Count -eq 0) {
            continue
        }
        if (-not $lineHits.ContainsKey($entry.Key)) {
            throw "Changed TCB file has no coverage data: $($entry.Key)"
        }
        $fileHits = $lineHits[$entry.Key]
        foreach ($number in $entry.Value) {
            if (-not $fileHits.ContainsKey($number)) {
                continue
            }
            $changedCoverable++
            if ($fileHits[$number] -gt 0) {
                $changedCovered++
            }
            else {
                $uncoveredChangedLines.Add("$($entry.Key):$number")
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
        comparisonRef = $ComparisonRef
        changedFiles = @(
            $changedLines.GetEnumerator() |
                Where-Object { $_.Value.Count -gt 0 }
        ).Count
        coveredLines = $changedCovered
        coverableLines = $changedCoverable
        linePercent = $changedPercent
        minimumLinePercent = [double]$baseline.minimumChangedTcbLinePercent
        uncoveredLines = @($uncoveredChangedLines | Sort-Object)
        passed = $changedPercent + 0.005 -ge
            [double]$baseline.minimumChangedTcbLinePercent
    }
}

$summary = [pscustomobject][ordered]@{
    schemaVersion = 1
    reportCount = $reports.Count
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
