param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [string]$OutPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $Root))
{
    throw "Root path does not exist: $Root"
}

$phaseSummaries = @(Get-ChildItem -Path $Root -Directory |
    Sort-Object Name |
    ForEach-Object {
        $summaryPath = Join-Path $_.FullName "summary.json"
        if (Test-Path -Path $summaryPath)
        {
            [pscustomobject]@{
                Phase = $_.Name
                File = $summaryPath
                Summary = Get-Content -Path $summaryPath | ConvertFrom-Json
            }
        }
    })

if ($phaseSummaries.Count -eq 0)
{
    throw "No phase summaries were found under: $Root"
}

function Get-Sum([object[]]$Values)
{
    return ($Values | Measure-Object -Sum).Sum
}

function Get-SummarySum([string]$PropertyName)
{
    return Get-Sum ($phaseSummaries | ForEach-Object {
        [double]$_.Summary.PSObject.Properties[$PropertyName].Value
    })
}

$schemaVersions = @($phaseSummaries | ForEach-Object { [string]$_.Summary.SchemaVersion } | Sort-Object -Unique)
if ($schemaVersions.Count -ne 1)
{
    throw "Phase summaries use incompatible schema versions: $($schemaVersions -join ', ')"
}

$latestSchemaVersion = $schemaVersions[0]
$totalCases = Get-SummarySum "CasesAnalyzed"
$totalElapsedSeconds = Get-SummarySum "ElapsedSeconds"
$totalFindings = Get-SummarySum "FindingCount"
$totalUniqueFindings = Get-SummarySum "UniqueFindingCount"
$totalEnforcePureFailures = Get-SummarySum "EnforcePureFailureCount"
$totalCompilationErrors = Get-SummarySum "CompilationErrorCount"
$totalAnalyzerExceptions = Get-SummarySum "AnalyzerExceptionCount"
$interestingCasesSaved = Get-SummarySum "InterestingCasesSaved"
$throughput = if ($totalElapsedSeconds -gt 0) { [math]::Round($totalCases / $totalElapsedSeconds, 2) } else { 0.0 }

$unobservedOperationKinds = $phaseSummaries |
    ForEach-Object { $_.Summary.UnobservedOperationKinds } |
    Where-Object { $_ } |
    Sort-Object -Unique

$actionableUnobservedOperationKinds = $phaseSummaries |
    ForEach-Object { $_.Summary.ActionableUnobservedOperationKinds } |
    Where-Object { $_ } |
    Sort-Object -Unique

$unobservedGeneratorBackedShapes = $phaseSummaries |
    ForEach-Object { $_.Summary.UnobservedGeneratorBackedShapes } |
    Where-Object { $_ } |
    Sort-Object -Unique

$phaseBreakdown = $phaseSummaries | ForEach-Object {
    [pscustomobject]@{
        Phase = $_.Phase
        File = Split-Path -Leaf $_.File
        CasesAnalyzed = $_.Summary.CasesAnalyzed
        ElapsedSeconds = [math]::Round([double]$_.Summary.ElapsedSeconds, 1)
        Findings = $_.Summary.FindingCount
        UniqueFindings = $_.Summary.UniqueFindingCount
        EnforcePureFailures = $_.Summary.EnforcePureFailureCount
        CompilationErrors = $_.Summary.CompilationErrorCount
        AnalyzerExceptions = $_.Summary.AnalyzerExceptionCount
        InterestingCasesSaved = $_.Summary.InterestingCasesSaved
    }
}

$aggregate = [pscustomobject]@{
    SchemaVersion = $latestSchemaVersion
    Root = (Resolve-Path -Path $Root).Path
    PhaseCount = $phaseSummaries.Count
    CasesAnalyzed = $totalCases
    ElapsedSeconds = [math]::Round($totalElapsedSeconds, 1)
    ThroughputCasesPerSecond = $throughput
    FindingCount = $totalFindings
    UniqueFindingCount = $totalUniqueFindings
    EnforcePureFailureCount = $totalEnforcePureFailures
    CompilationErrorCount = $totalCompilationErrors
    AnalyzerExceptionCount = $totalAnalyzerExceptions
    InterestingCasesSaved = $interestingCasesSaved
    UnobservedOperationKinds = @($unobservedOperationKinds)
    ActionableUnobservedOperationKinds = @($actionableUnobservedOperationKinds)
    UnobservedGeneratorBackedShapes = @($unobservedGeneratorBackedShapes)
    Phases = @($phaseBreakdown)
}

$json = $aggregate | ConvertTo-Json -Depth 6

if ($OutPath)
{
    Set-Content -Path $OutPath -Value $json -Encoding UTF8
}

$json
