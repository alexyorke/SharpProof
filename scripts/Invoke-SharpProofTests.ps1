[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateRange(0, 256)]
    [int]$Workers = 0,

    [Parameter()]
    [switch]$FailFast,

    [Parameter()]
    [switch]$NoBuild,

    [Parameter()]
    [string]$Filter = '',

    [Parameter()]
    [ValidateSet('All', 'Main', 'MainSmt', 'MainSmtOracle', 'MainSmtAnalyzer', 'MainSmtFlow', 'MainSmtCore', 'MainGeneral', 'Tooling')]
    [string]$TestLane = 'All',

    [Parameter()]
    [string]$ResultsDirectory = '',

    [Parameter()]
    [switch]$Profile,

    [Parameter()]
    [ValidateRange(1, 200)]
    [int]$Top = 30,

    [Parameter()]
    [ValidateRange(0, 1048576)]
    [int]$MemoryLimitMb = 0,

    [Parameter()]
    [ValidateRange(0, 86400)]
    [int]$TimeoutSeconds = 0,

    [Parameter()]
    [switch]$SequentialLanes,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DotnetTestArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-SlowestTestsFromTrx
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$TrxPath,

        [Parameter(Mandatory = $true)]
        [int]$Top
    )

    [xml]$trx = Get-Content -LiteralPath $TrxPath
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
    $namespaceManager.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    $unitTests = @{}
    foreach ($test in $trx.SelectNodes('//t:UnitTest', $namespaceManager))
    {
        $className = $test.TestMethod.className
        if ([string]::IsNullOrWhiteSpace($className))
        {
            $className = '<unknown>'
        }

        $unitTests[$test.id] = $className
    }

    $items = foreach ($node in $trx.SelectNodes('//t:UnitTestResult', $namespaceManager))
    {
        if ([string]::IsNullOrWhiteSpace($node.duration))
        {
            continue
        }

        $duration = [TimeSpan]::Parse($node.duration)
        [pscustomobject]@{
            Seconds = [math]::Round($duration.TotalSeconds, 3)
            Outcome = $node.outcome
            ClassName = $unitTests[$node.testId]
            Test = $node.testName
        }
    }

    $slowestTests = $items |
        Sort-Object Seconds -Descending |
        Select-Object -First $Top |
        Format-Table -AutoSize |
        Out-String
    Write-Host $slowestTests

    Write-Host ''
    Write-Host 'Slowest fixture totals'
    $slowestFixtures = $items |
        Group-Object ClassName |
        ForEach-Object {
            [pscustomobject]@{
                Seconds = [math]::Round(($_.Group | Measure-Object Seconds -Sum).Sum, 2)
                Count = $_.Count
                ClassName = $_.Name
            }
        } |
        Sort-Object Seconds -Descending |
        Select-Object -First $Top |
        Format-Table -AutoSize |
        Out-String
    Write-Host $slowestFixtures
}

function Get-SharpProofTestWorkerProcesses
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $processes = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'testhost.exe' OR Name = 'vstest.console.exe' OR Name = 'MSBuild.exe' OR Name = 'VBCSCompiler.exe'" -ErrorAction SilentlyContinue
    foreach ($process in $processes)
    {
        $commandLine = [string]$process.CommandLine
        if ($process.Name -eq 'testhost.exe' -or
            $commandLine.IndexOf($RepoRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('SharpProof.Test', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('SharpProof.ToolingTest', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('MSBuild.dll', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('VBCSCompiler.dll', [StringComparison]::OrdinalIgnoreCase) -ge 0)
        {
            $process
        }
    }
}

function Stop-NewSharpProofTestWorkerProcesses
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [int[]]$InitialProcessIds,

        [Parameter(Mandatory = $true)]
        [datetime]$StartedAfter,

        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $initialProcessIdSet = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($processId in $InitialProcessIds)
    {
        [void]$initialProcessIdSet.Add($processId)
    }

    $stoppedCount = 0
    $stoppedProcessIds = New-Object System.Collections.Generic.List[int]
    $processes = Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'testhost.exe' OR Name = 'vstest.console.exe' OR Name = 'MSBuild.exe' OR Name = 'VBCSCompiler.exe'" -ErrorAction SilentlyContinue
    foreach ($process in $processes)
    {
        $processId = [int]$process.ProcessId
        if ($initialProcessIdSet.Contains($processId))
        {
            continue
        }

        $creationDate = $null
        if (-not [string]::IsNullOrWhiteSpace([string]$process.CreationDate))
        {
            try
            {
                $creationDate = [System.Management.ManagementDateTimeConverter]::ToDateTime($process.CreationDate)
            }
            catch
            {
                $creationDate = $null
            }
        }

        if ($null -ne $creationDate -and $creationDate -lt $StartedAfter.AddSeconds(-2))
        {
            continue
        }

        $commandLine = [string]$process.CommandLine
        $isKnownTestWorker = $process.Name -eq 'testhost.exe' -or
            $process.Name -eq 'vstest.console.exe' -or
            [string]::IsNullOrWhiteSpace($commandLine) -or
            $commandLine.IndexOf($RepoRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('SharpProof.Test', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('SharpProof.ToolingTest', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('testhost.dll', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('Microsoft.TestPlatform', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('MSBuild.dll', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('VBCSCompiler.dll', [StringComparison]::OrdinalIgnoreCase) -ge 0
        if (-not $isKnownTestWorker)
        {
            continue
        }

        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        $stoppedProcessIds.Add($processId)
        $stoppedCount++
    }

    if ($stoppedCount -gt 0)
    {
        Wait-Process -Id $stoppedProcessIds.ToArray() -Timeout 5 -ErrorAction SilentlyContinue
        Write-Host "Stopped $stoppedCount orphaned test worker process(es)."
    }
}

function Resolve-SharpProofTestProjects
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('All', 'Main', 'MainSmt', 'MainSmtOracle', 'MainSmtAnalyzer', 'MainSmtFlow', 'MainSmtCore', 'MainGeneral', 'Tooling')]
        [string]$RequestedLane,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Filter
    )

    $mainProject = [ordered]@{
        Name = 'Main'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
        LaneFilter = ''
    }
    $mainSmtProject = [ordered]@{
        Name = 'MainSmt'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
        LaneFilter = 'TestCategory=SmtHeavy'
    }
    $mainSmtOracleProject = [ordered]@{
        Name = 'MainSmtOracle'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
        LaneFilter = '(FullyQualifiedName~SemanticOracleSmtTests|FullyQualifiedName~PatternSmtInvariantTests|FullyQualifiedName~ExceptionReachabilitySmtTests)'
    }
    $mainSmtAnalyzerProject = [ordered]@{
        Name = 'MainSmtAnalyzer'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
        LaneFilter = '(FullyQualifiedName~SemanticOracleAnalyzerSmtTests|FullyQualifiedName~DiagnosticEvidenceTests)'
    }
    $mainSmtFlowProject = [ordered]@{
        Name = 'MainSmtFlow'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
        LaneFilter = '(FullyQualifiedName~ExceptionFlowPathFactStressTests|FullyQualifiedName~SemanticOracleRuntimeHazardAnalyzerSmtTests)'
    }
    $mainSmtCoreProject = [ordered]@{
        Name = 'MainSmtCore'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
        LaneFilter = '(FullyQualifiedName~PathSensitiveSmtInvariantTests|FullyQualifiedName~SmtAnalysisServiceTests|FullyQualifiedName~ExpressionAtomSmtTests|FullyQualifiedName~StringLengthSmtTests|FullyQualifiedName~ForeachSmtInvariantTests|FullyQualifiedName~ElementAccessSmtTests|FullyQualifiedName~LoopExitSmtInvariantTests|FullyQualifiedName~ReferenceReachabilitySmtTests)'
    }
    $mainGeneralProject = [ordered]@{
        Name = 'MainGeneral'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
        LaneFilter = 'TestCategory!=SmtHeavy'
    }
    $toolingProject = [ordered]@{
        Name = 'Tooling'
        ProjectPath = 'SharpProof.ToolingTest\SharpProof.ToolingTest.csproj'
        LaneFilter = ''
    }

    if ($RequestedLane -eq 'Main' -and [string]::IsNullOrWhiteSpace($Filter))
    {
        return @($mainSmtOracleProject, $mainSmtAnalyzerProject, $mainSmtFlowProject, $mainSmtCoreProject, $mainGeneralProject)
    }

    if ($RequestedLane -eq 'MainSmt' -and [string]::IsNullOrWhiteSpace($Filter))
    {
        return @($mainSmtOracleProject, $mainSmtAnalyzerProject, $mainSmtFlowProject, $mainSmtCoreProject)
    }

    switch ($RequestedLane)
    {
        'Main' { }
        'MainSmt' { }
        'MainSmtOracle' { return @($mainSmtOracleProject) }
        'MainSmtAnalyzer' { return @($mainSmtAnalyzerProject) }
        'MainSmtFlow' { return @($mainSmtFlowProject) }
        'MainSmtCore' { return @($mainSmtCoreProject) }
        'MainGeneral' { return @($mainGeneralProject) }
        'Tooling' { return @($toolingProject) }
    }

    if ([string]::IsNullOrWhiteSpace($Filter))
    {
        if ($RequestedLane -eq 'All')
        {
            return @($mainSmtOracleProject, $mainSmtAnalyzerProject, $mainSmtFlowProject, $mainSmtCoreProject, $mainGeneralProject, $toolingProject)
        }

        return @($mainSmtProject, $mainGeneralProject, $toolingProject)
    }

    $toolingFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fixture in @(
        'AnalyzerPackagingTests',
        'CorpusReportTests',
        'EffectSummaryToolTests',
        'ExceptionSummaryCatalogValidationTests',
        'FuzzToolTests',
        'ImpactedTestSelectionScriptTests',
        'SharpProofCodeFixTests',
        'RoslynConstructCoverageTests',
        'RoslynShapeManifestCoverageTests',
        'SymbolicRuntimeHazardQueryTests',
        'SymbolicSourceQueryLineTests'))
    {
        [void]$toolingFixtures.Add($fixture)
    }

    $smtHeavyFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fixture in @(
        'DiagnosticEvidenceTests',
        'ElementAccessSmtTests',
        'ExceptionFlowPathFactStressTests',
        'ExceptionReachabilitySmtTests',
        'ExpressionAtomSmtTests',
        'ForeachSmtInvariantTests',
        'LoopExitSmtInvariantTests',
        'PathSensitiveSmtInvariantTests',
        'PatternSmtInvariantTests',
        'ReferenceReachabilitySmtTests',
        'SemanticOracleAnalyzerSmtTests',
        'SemanticOracleRuntimeHazardAnalyzerSmtTests',
        'SemanticOracleSmtTests',
        'SmtAnalysisServiceTests',
        'StringLengthSmtTests'))
    {
        [void]$smtHeavyFixtures.Add($fixture)
    }

    $smtOracleFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fixture in @(
        'SemanticOracleSmtTests',
        'PatternSmtInvariantTests',
        'ExceptionReachabilitySmtTests'))
    {
        [void]$smtOracleFixtures.Add($fixture)
    }

    $smtAnalyzerFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fixture in @(
        'SemanticOracleAnalyzerSmtTests',
        'DiagnosticEvidenceTests'))
    {
        [void]$smtAnalyzerFixtures.Add($fixture)
    }

    $smtFlowFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fixture in @(
        'ExceptionFlowPathFactStressTests',
        'SemanticOracleRuntimeHazardAnalyzerSmtTests'))
    {
        [void]$smtFlowFixtures.Add($fixture)
    }

    $smtCoreFixtures = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fixture in @(
        'ElementAccessSmtTests',
        'ExpressionAtomSmtTests',
        'ForeachSmtInvariantTests',
        'LoopExitSmtInvariantTests',
        'PathSensitiveSmtInvariantTests',
        'ReferenceReachabilitySmtTests',
        'SmtAnalysisServiceTests',
        'StringLengthSmtTests'))
    {
        [void]$smtCoreFixtures.Add($fixture)
    }

    $matchedFixtureNames = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($Filter, 'SharpProof\.Test\.([A-Za-z_][A-Za-z0-9_]*)'))
    {
        [void]$matchedFixtureNames.Add($match.Groups[1].Value)
    }

    foreach ($match in [regex]::Matches($Filter, 'FullyQualifiedName~([A-Za-z_][A-Za-z0-9_]*)'))
    {
        [void]$matchedFixtureNames.Add($match.Groups[1].Value)
    }

    $matchedFixtures = @($matchedFixtureNames | Sort-Object)

    if ($matchedFixtures.Count -eq 0)
    {
        return @($mainProject, $toolingProject)
    }

    $hasToolingFixture = $false
    $hasMainFixture = $false
    foreach ($fixture in $matchedFixtures)
    {
        if ($toolingFixtures.Contains($fixture))
        {
            $hasToolingFixture = $true
        }
        else
        {
            $hasMainFixture = $true
        }
    }

    if ($hasToolingFixture -and -not $hasMainFixture)
    {
        return @($toolingProject)
    }

    if ($hasMainFixture -and -not $hasToolingFixture)
    {
        if ($RequestedLane -eq 'Main' -or $RequestedLane -eq 'MainSmt')
        {
            $hasSmtHeavyFixture = $false
            $hasMainGeneralFixture = $false
            foreach ($fixture in $matchedFixtures)
            {
                if ($toolingFixtures.Contains($fixture))
                {
                    continue
                }

                if ($smtHeavyFixtures.Contains($fixture))
                {
                    $hasSmtHeavyFixture = $true
                }
                else
                {
                    $hasMainGeneralFixture = $true
                }
            }

            if ($hasSmtHeavyFixture -and -not $hasMainGeneralFixture)
            {
                $needsOracle = $false
                $needsAnalyzer = $false
                $needsFlow = $false
                $needsCore = $false
                foreach ($fixture in $matchedFixtures)
                {
                    if ($smtOracleFixtures.Contains($fixture))
                    {
                        $needsOracle = $true
                    }
                    elseif ($smtAnalyzerFixtures.Contains($fixture))
                    {
                        $needsAnalyzer = $true
                    }
                    elseif ($smtFlowFixtures.Contains($fixture))
                    {
                        $needsFlow = $true
                    }
                    elseif ($smtCoreFixtures.Contains($fixture))
                    {
                        $needsCore = $true
                    }
                }

                $selectedSmtProjects = New-Object System.Collections.Generic.List[object]
                if ($needsOracle)
                {
                    $selectedSmtProjects.Add($mainSmtOracleProject)
                }

                if ($needsAnalyzer)
                {
                    $selectedSmtProjects.Add($mainSmtAnalyzerProject)
                }

                if ($needsFlow)
                {
                    $selectedSmtProjects.Add($mainSmtFlowProject)
                }

                if ($needsCore)
                {
                    $selectedSmtProjects.Add($mainSmtCoreProject)
                }

                if ($selectedSmtProjects.Count -gt 0)
                {
                    return @($selectedSmtProjects.ToArray())
                }

                return @($mainSmtProject)
            }

            if ($RequestedLane -eq 'MainSmt')
            {
                return @($mainSmtProject)
            }

            if ($hasMainGeneralFixture -and -not $hasSmtHeavyFixture)
            {
                return @($mainGeneralProject)
            }

            return @($mainSmtProject, $mainGeneralProject)
        }

        return @($mainProject)
    }

    return @($mainProject, $toolingProject)
}

function Get-SharpProofDefaultWorkerCount
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Main', 'MainSmt', 'MainSmtOracle', 'MainSmtAnalyzer', 'MainSmtFlow', 'MainSmtCore', 'MainGeneral', 'Tooling')]
        [string]$LaneName
    )

    $processorCount = [Environment]::ProcessorCount
    switch ($LaneName)
    {
        'Main' { return [Math]::Max(1, [Math]::Min($processorCount, 8)) }
        'MainSmt' { return [Math]::Max(1, [Math]::Min($processorCount, 8)) }
        'MainSmtOracle' { return [Math]::Max(1, [Math]::Min($processorCount, 4)) }
        'MainSmtAnalyzer' { return [Math]::Max(1, [Math]::Min($processorCount, 4)) }
        'MainSmtFlow' { return [Math]::Max(1, [Math]::Min($processorCount, 4)) }
        'MainSmtCore' { return [Math]::Max(1, [Math]::Min($processorCount, 4)) }
        'MainGeneral' { return [Math]::Max(1, [Math]::Min($processorCount, 8)) }
        'Tooling'
        {
            $defaultWorkerCount = 20
            return [Math]::Max(1, [Math]::Min($processorCount, $defaultWorkerCount))
        }
    }
}

function Join-SharpProofTestFilter
{
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$UserFilter,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$LaneFilter
    )

    if ([string]::IsNullOrWhiteSpace($UserFilter))
    {
        return $LaneFilter
    }

    if ([string]::IsNullOrWhiteSpace($LaneFilter))
    {
        return $UserFilter
    }

    return "($UserFilter)&($LaneFilter)"
}

function New-SharpProofRunSettings
{
    param(
        [Parameter(Mandatory = $true)]
        [int]$WorkerCount,

        [Parameter(Mandatory = $true)]
        [bool]$EnableFailFast
    )

    if ($WorkerCount -le 0 -and -not $EnableFailFast)
    {
        return ''
    }

    $settingsPath = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpproof-test-' + [guid]::NewGuid().ToString('N') + '.runsettings')

    $runConfigurationLines = New-Object System.Collections.Generic.List[string]
    if ($WorkerCount -gt 0)
    {
        $runConfigurationLines.Add("    <MaxCpuCount>$WorkerCount</MaxCpuCount>")
    }

    $nunitLines = New-Object System.Collections.Generic.List[string]
    if ($WorkerCount -gt 0)
    {
        $nunitLines.Add("    <NumberOfTestWorkers>$WorkerCount</NumberOfTestWorkers>")
    }

    if ($EnableFailFast)
    {
        $nunitLines.Add('    <StopOnError>true</StopOnError>')
    }

    $runConfigurationXml = if ($runConfigurationLines.Count -gt 0)
    {
        "<RunConfiguration>`n$($runConfigurationLines -join "`n")`n  </RunConfiguration>"
    }
    else
    {
        '<RunConfiguration />'
    }

    $nunitXml = if ($nunitLines.Count -gt 0)
    {
        "<NUnit>`n$($nunitLines -join "`n")`n  </NUnit>"
    }
    else
    {
        '<NUnit />'
    }

    $settingsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  $runConfigurationXml
  $nunitXml
</RunSettings>
"@

    Set-Content -LiteralPath $settingsPath -Value $settingsXml -Encoding utf8
    return $settingsPath
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRunStartedAt = Get-Date
$initialTestWorkerIds = @(Get-SharpProofTestWorkerProcesses -RepoRoot $repoRoot | ForEach-Object { [int]$_.ProcessId })
$laneSettingsPaths = New-Object System.Collections.Generic.List[string]

$effectiveResultsDirectory = $ResultsDirectory
if ($Profile -and [string]::IsNullOrWhiteSpace($effectiveResultsDirectory))
{
    $effectiveResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpproof-test-profile-' + [guid]::NewGuid().ToString('N'))
}

if (-not [string]::IsNullOrWhiteSpace($effectiveResultsDirectory))
{
    New-Item -ItemType Directory -Path $effectiveResultsDirectory -Force | Out-Null
}

$requestedLane = if ($PSBoundParameters.ContainsKey('TestLane'))
{
    $TestLane
}
else
{
    'All'
}

$selectedProjects = @(Resolve-SharpProofTestProjects -RequestedLane $requestedLane -Filter $Filter)

Push-Location $repoRoot
$exitCode = 0
try
{
    $wrapperPath = Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1'
    $projectCount = $selectedProjects.Count
    $laneSpecs = @(foreach ($project in $selectedProjects)
    {
        $effectiveFilter = Join-SharpProofTestFilter -UserFilter $Filter -LaneFilter $project.LaneFilter
        $projectWorkers = if ($PSBoundParameters.ContainsKey('Workers'))
        {
            $Workers
        }
        else
        {
            if ($requestedLane -eq 'All' -and
                ($project.Name -eq 'MainSmtCore' -or $project.Name -eq 'Tooling'))
            {
                [Math]::Max(1, [Math]::Min([Environment]::ProcessorCount, 2))
            }
            else
            {
                Get-SharpProofDefaultWorkerCount -LaneName $project.Name
            }
        }
        $projectSettingsPath = New-SharpProofRunSettings -WorkerCount $projectWorkers -EnableFailFast $FailFast.IsPresent
        if (-not [string]::IsNullOrWhiteSpace($projectSettingsPath))
        {
            $laneSettingsPaths.Add($projectSettingsPath)
        }

        $projectResultsDirectory = $effectiveResultsDirectory
        if (-not [string]::IsNullOrWhiteSpace($effectiveResultsDirectory) -and $projectCount -gt 1)
        {
            $projectResultsDirectory = Join-Path $effectiveResultsDirectory $project.Name
            New-Item -ItemType Directory -Path $projectResultsDirectory -Force | Out-Null
        }

        $testArgs = New-Object System.Collections.Generic.List[string]
        $testArgs.Add('test')
        $testArgs.Add([string]$project.ProjectPath)
        $testArgs.Add('--configuration')
        $testArgs.Add($Configuration)
        $testArgs.Add('--verbosity')
        $testArgs.Add('minimal')
        $testArgs.Add('/nodeReuse:false')
        $testArgs.Add('-p:UseSharedCompilation=false')

        if (-not [string]::IsNullOrWhiteSpace($projectSettingsPath))
        {
            $testArgs.Add('--settings')
            $testArgs.Add($projectSettingsPath)
        }

        if ($NoBuild)
        {
            $testArgs.Add('--no-build')
            if (-not $testArgs.Contains('--no-restore'))
            {
                $testArgs.Add('--no-restore')
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($effectiveFilter))
        {
            $testArgs.Add('--filter')
            $testArgs.Add($effectiveFilter)
        }

        if (-not [string]::IsNullOrWhiteSpace($projectResultsDirectory))
        {
            $testArgs.Add('--results-directory')
            $testArgs.Add($projectResultsDirectory)
        }

        if ($Profile)
        {
            $testArgs.Add('--logger')
            $testArgs.Add('trx;LogFileName=profile.trx')
        }

        foreach ($argument in $DotnetTestArgs)
        {
            $testArgs.Add($argument)
        }

        [pscustomobject]@{
            Name = [string]$project.Name
            ProjectPath = [string]$project.ProjectPath
            WorkerCount = $projectWorkers
            TestArgs = $testArgs
            ResultsDirectory = $projectResultsDirectory
            SettingsPath = $projectSettingsPath
        }
    })

    $runLanesConcurrently = $laneSpecs.Count -gt 1 -and -not $SequentialLanes

    if ($runLanesConcurrently -and -not $NoBuild)
    {
        # Concurrent lanes must not build concurrently: they share most of the project
        # graph and MSBuild does not coordinate simultaneous writes to the same outputs.
        $builtProjects = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
        foreach ($spec in $laneSpecs)
        {
            if ($builtProjects.Contains($spec.ProjectPath))
            {
                continue
            }

            & $wrapperPath -MemoryLimitMb $MemoryLimitMb -TimeoutSeconds $TimeoutSeconds build $spec.ProjectPath --configuration $Configuration --verbosity minimal
            if ($LASTEXITCODE -ne 0)
            {
                $exitCode = $LASTEXITCODE
                break
            }

            $builtProjects.Add($spec.ProjectPath) | Out-Null
        }

        if ($exitCode -eq 0)
        {
            foreach ($spec in $laneSpecs)
            {
                if (-not $spec.TestArgs.Contains('--no-build'))
                {
                    $spec.TestArgs.Add('--no-build')
                }

                if (-not $spec.TestArgs.Contains('--no-restore'))
                {
                    $spec.TestArgs.Add('--no-restore')
                }
            }
        }
    }

    if ($exitCode -eq 0 -and $runLanesConcurrently)
    {
        # Each background lane runs in its own PowerShell process with output redirected
        # to files. Start-Job is unsuitable here: the dotnet child inherits the job's
        # stdout pipe, and its raw output corrupts the job's CliXML transport.
        $powerShellPath = (Get-Process -Id $PID).Path
        $backgroundLanes = @(foreach ($spec in ($laneSpecs | Select-Object -Skip 1))
        {
            $laneId = [guid]::NewGuid().ToString('N')
            $laneScriptPath = Join-Path ([System.IO.Path]::GetTempPath()) "sharpproof-lane-$laneId.ps1"
            $laneStdoutPath = Join-Path ([System.IO.Path]::GetTempPath()) "sharpproof-lane-$laneId.out.log"
            $laneStderrPath = Join-Path ([System.IO.Path]::GetTempPath()) "sharpproof-lane-$laneId.err.log"

            $quotedTestArgs = @($spec.TestArgs | ForEach-Object { "'" + ($_ -replace "'", "''") + "'" }) -join ', '
            $laneScriptLines = @(
                "Set-Location -LiteralPath '$($repoRoot -replace "'", "''")'",
                "`$laneArgs = @($quotedTestArgs)",
                "& '$($wrapperPath -replace "'", "''")' -MemoryLimitMb $MemoryLimitMb -TimeoutSeconds $TimeoutSeconds @laneArgs",
                'exit $LASTEXITCODE'
            )
            Set-Content -LiteralPath $laneScriptPath -Value ($laneScriptLines -join [Environment]::NewLine) -Encoding utf8

            $laneProcess = Start-Process -FilePath $powerShellPath `
                -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $laneScriptPath + '"')) `
                -RedirectStandardOutput $laneStdoutPath `
                -RedirectStandardError $laneStderrPath `
                -NoNewWindow `
                -PassThru
            # Cache the process handle now; without this, ExitCode is $null after the
            # process exits and the lane failure would be silently reported as success.
            $null = $laneProcess.Handle

            [pscustomobject]@{
                Spec = $spec
                Process = $laneProcess
                ScriptPath = $laneScriptPath
                StdoutPath = $laneStdoutPath
                StderrPath = $laneStderrPath
            }
        })

        $foregroundSpec = $laneSpecs[0]
        $backgroundLaneSummary = ($backgroundLanes | ForEach-Object { "$($_.Spec.Name) [$($_.Spec.WorkerCount)]" }) -join ', '
        Write-Host "Running the $($foregroundSpec.Name) lane ($($foregroundSpec.WorkerCount) workers) with $($backgroundLanes.Count) lane(s) in the background: $backgroundLaneSummary"
        $foregroundArgs = $foregroundSpec.TestArgs
        & $wrapperPath -MemoryLimitMb $MemoryLimitMb -TimeoutSeconds $TimeoutSeconds @foregroundArgs
        $laneExitCodes = @{ ($foregroundSpec.Name) = $LASTEXITCODE }

        foreach ($backgroundLane in $backgroundLanes)
        {
            $backgroundLane.Process.WaitForExit()
            $laneRawExitCode = $backgroundLane.Process.ExitCode
            $laneExitCodes[$backgroundLane.Spec.Name] = if ($null -eq $laneRawExitCode) { 1 } else { [int]$laneRawExitCode }

            Write-Host ''
            Write-Host "=== $($backgroundLane.Spec.Name) lane output ==="
            foreach ($outputPath in @($backgroundLane.StdoutPath, $backgroundLane.StderrPath))
            {
                if ((Test-Path -LiteralPath $outputPath) -and (Get-Item -LiteralPath $outputPath).Length -gt 0)
                {
                    Get-Content -LiteralPath $outputPath | Write-Host
                }
            }

            Remove-Item -LiteralPath $backgroundLane.ScriptPath, $backgroundLane.StdoutPath, $backgroundLane.StderrPath -Force -ErrorAction SilentlyContinue
        }

        foreach ($spec in $laneSpecs)
        {
            if ($laneExitCodes[$spec.Name] -ne 0)
            {
                $exitCode = $laneExitCodes[$spec.Name]
                break
            }
        }

        if ($Profile)
        {
            foreach ($spec in $laneSpecs)
            {
                $trxPath = Join-Path $spec.ResultsDirectory 'profile.trx'
                if (Test-Path -LiteralPath $trxPath)
                {
                    Write-Host ''
                    Write-Host "Slowest test cases from $trxPath ($($spec.Name))"
                    Write-SlowestTestsFromTrx -TrxPath $trxPath -Top $Top
                }
                else
                {
                    Write-Warning "TRX profile was requested, but no profile.trx file was produced in $($spec.ResultsDirectory)."
                }
            }
        }
    }
    elseif ($exitCode -eq 0)
    {
        foreach ($spec in $laneSpecs)
        {
            $laneArgs = $spec.TestArgs
            Write-Host "Running the $($spec.Name) lane with $($spec.WorkerCount) workers"
            & $wrapperPath -MemoryLimitMb $MemoryLimitMb -TimeoutSeconds $TimeoutSeconds @laneArgs
            $projectExitCode = $LASTEXITCODE
            if ($projectExitCode -ne 0)
            {
                $exitCode = $projectExitCode
                break
            }

            if ($Profile)
            {
                $trxPath = Join-Path $spec.ResultsDirectory 'profile.trx'
                if (Test-Path -LiteralPath $trxPath)
                {
                    Write-Host ''
                    Write-Host "Slowest test cases from $trxPath ($($spec.Name))"
                    Write-SlowestTestsFromTrx -TrxPath $trxPath -Top $Top
                }
                else
                {
                    Write-Warning "TRX profile was requested, but no profile.trx file was produced in $($spec.ResultsDirectory)."
                }
            }
        }
    }
}
finally
{
    Stop-NewSharpProofTestWorkerProcesses `
        -InitialProcessIds $initialTestWorkerIds `
        -StartedAfter $testRunStartedAt `
        -RepoRoot $repoRoot

    Pop-Location
    foreach ($laneSettingsPath in $laneSettingsPaths)
    {
        if (-not [string]::IsNullOrWhiteSpace($laneSettingsPath))
        {
            Remove-Item -LiteralPath $laneSettingsPath -Force -ErrorAction SilentlyContinue
        }
    }
}

exit $exitCode
