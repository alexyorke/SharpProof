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

function Invoke-GitText {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add('-C')
    $startInfo.ArgumentList.Add($repositoryRoot)
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw $FailureMessage
        }
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $text = $output.GetAwaiter().GetResult()
        $errorText = $errorOutput.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            if ([string]::IsNullOrWhiteSpace($errorText)) {
                throw $FailureMessage
            }
            throw "$FailureMessage $($errorText.Trim())"
        }
        return $text
    }
    finally {
        $process.Dispose()
    }
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
        Sort-Object FullName
)
if ($reports.Count -eq 0) {
    throw "No Cobertura XML reports were found under $resolvedCoverageRoot."
}
if ([string]::IsNullOrWhiteSpace($ComparisonRef) -and -not $ReportOnly) {
    throw 'ComparisonRef is required for changed-TCB coverage enforcement.'
}

$lineHits = [Collections.Generic.Dictionary[string,
    Collections.Generic.Dictionary[int, int]]]::new(
        [StringComparer]::Ordinal)

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
            $relativePath.Contains('/obj/', [StringComparison]::Ordinal) -or
            $relativePath.Contains('/bin/', [StringComparison]::Ordinal)) {
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
                    [StringComparison]::Ordinal)
            }
    )
    $paths = @(ConvertTo-OrdinalSortedArray -Values $paths)
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

$productionPathSet = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($project in $projects) {
    $prefix = $project.name + '/'
    foreach ($path in $lineHits.Keys) {
        if ($path.StartsWith(
                $prefix,
                [StringComparison]::Ordinal)) {
            [void]$productionPathSet.Add($path)
        }
    }
}
$productionPaths = @(ConvertTo-OrdinalSortedArray `
    -Values @($productionPathSet))
$aggregate = Measure-Coverage -Paths $productionPaths
$aggregateMinimum = [double]$baseline.minimumAggregateLinePercent
$aggregatePassed =
    $aggregate.linePercent + 0.005 -ge $aggregateMinimum

$changedTcb = [pscustomobject][ordered]@{
    comparisonRef = $ComparisonRef
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
if (-not [string]::IsNullOrWhiteSpace($ComparisonRef)) {
    $contractPath = Join-Path $repositoryRoot 'eng\acceptance\contract.json'
    $contract = Get-Content -LiteralPath $contractPath -Raw |
        ConvertFrom-Json
    $canonicalTcbPaths = @(Get-SharpProofTcbPaths `
        -Contract $contract `
        -IncludeAcceptanceContract)
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
    $diffTarget = "$ComparisonRef...HEAD"
    if ($IncludeWorkingTree) {
        $mergeBaseOutput = Invoke-GitText `
            -Arguments @('merge-base', $ComparisonRef, 'HEAD') `
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
    $changedFileOutput = Invoke-GitText `
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
        $patch = Invoke-GitText `
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
            # changed-TCB selection and release digest, but have no C# line
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
            if ($number -gt 0 -and
                $number -le $sourceLines.Count -and
                $sourceLines[$number - 1].Trim() -in @('{', '}')) {
                # Coverlet may attach a sequence point to a brace-only line
                # for generated cleanup code. Braces are not executable
                # source and do not participate in the changed-TCB ratchet.
                continue
            }
            if (-not $fileHits.ContainsKey($number)) {
                # A changed source line without a sequence point is
                # non-executable syntax such as a declaration or brace.
                # Coverlet emits sequence points for executable lines; only
                # those lines participate in the changed-TCB ratchet.
                continue
            }
            $changedCoverable++
            if ($fileHits[$number] -gt 0) {
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
        comparisonRef = $ComparisonRef
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
