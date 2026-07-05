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
        $partialPath = Join-Path $_.FullName "summary.partial.json"

        if (Test-Path -Path $summaryPath)
        {
            [pscustomobject]@{
                Phase = $_.Name
                File = $summaryPath
                Summary = Get-Content -Path $summaryPath | ConvertFrom-Json
            }
        }
        elseif (Test-Path -Path $partialPath)
        {
            [pscustomobject]@{
                Phase = $_.Name
                File = $partialPath
                Summary = Get-Content -Path $partialPath | ConvertFrom-Json
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

$latestSchemaVersion = ($phaseSummaries | Select-Object -Last 1).Summary.SchemaVersion
$totalCases = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.CasesAnalyzed })
$totalElapsedSeconds = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.ElapsedSeconds })
$totalFindings = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.FindingCount })
$totalUniqueFindings = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.UniqueFindingCount })
$totalSp0002 = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.Sp0002Count })
$totalSp0004 = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.Sp0004Count })
$totalSp0010 = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.Sp0010Count })
$totalCompilationErrors = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.CompilationErrorCount })
$totalAnalyzerExceptions = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.AnalyzerExceptionCount })
$interestingCasesSaved = Get-Sum ($phaseSummaries | ForEach-Object { [double]$_.Summary.InterestingCasesSaved })
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
        Sp0002 = $_.Summary.Sp0002Count
        Sp0004 = $_.Summary.Sp0004Count
        Sp0010 = $_.Summary.Sp0010Count
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
    Sp0002Count = $totalSp0002
    Sp0004Count = $totalSp0004
    Sp0010Count = $totalSp0010
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
