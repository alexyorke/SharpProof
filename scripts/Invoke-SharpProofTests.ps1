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
    [ValidateSet('All', 'Main', 'Tooling')]
    [string]$TestLane = 'Main',

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
        [ValidateSet('All', 'Main', 'Tooling')]
        [string]$RequestedLane,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Filter
    )

    $mainProject = [ordered]@{
        Name = 'Main'
        ProjectPath = 'SharpProof.Test\SharpProof.Test.csproj'
    }
    $toolingProject = [ordered]@{
        Name = 'Tooling'
        ProjectPath = 'SharpProof.ToolingTest\SharpProof.ToolingTest.csproj'
    }

    switch ($RequestedLane)
    {
        'Main' { return @($mainProject) }
        'Tooling' { return @($toolingProject) }
    }

    if ([string]::IsNullOrWhiteSpace($Filter))
    {
        return @($mainProject, $toolingProject)
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

    $matchedFixtures = @([regex]::Matches($Filter, 'SharpProof\.Test\.([A-Za-z_][A-Za-z0-9_]*)') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique)

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
        return @($mainProject)
    }

    return @($mainProject, $toolingProject)
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRunStartedAt = Get-Date
$initialTestWorkerIds = @(Get-SharpProofTestWorkerProcesses -RepoRoot $repoRoot | ForEach-Object { [int]$_.ProcessId })
$settingsPath = ''
$effectiveWorkers = if ($PSBoundParameters.ContainsKey('Workers'))
{
    $Workers
}
else
{
    [Math]::Max(1, [Math]::Min([Environment]::ProcessorCount, 20))
}
$useGeneratedRunSettings = $effectiveWorkers -gt 0 -or $FailFast
if ($useGeneratedRunSettings)
{
    $settingsPath = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpproof-test-' + [guid]::NewGuid().ToString('N') + '.runsettings')

    $runConfigurationLines = New-Object System.Collections.Generic.List[string]
    if ($effectiveWorkers -gt 0)
    {
        $runConfigurationLines.Add("    <MaxCpuCount>$effectiveWorkers</MaxCpuCount>")
    }

    $nunitLines = New-Object System.Collections.Generic.List[string]
    if ($effectiveWorkers -gt 0)
    {
        $nunitLines.Add("    <NumberOfTestWorkers>$effectiveWorkers</NumberOfTestWorkers>")
    }

    if ($FailFast)
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
}

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
elseif ([string]::IsNullOrWhiteSpace($Filter))
{
    'Main'
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
    $projectCount = $selectedProjects.Count
    foreach ($project in $selectedProjects)
    {
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

        if ($useGeneratedRunSettings)
        {
            $testArgs.Add('--settings')
            $testArgs.Add($settingsPath)
        }

        if ($NoBuild)
        {
            $testArgs.Add('--no-build')
        }

        if (-not [string]::IsNullOrWhiteSpace($Filter))
        {
            $testArgs.Add('--filter')
            $testArgs.Add($Filter)
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

        & (Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1') -MemoryLimitMb $MemoryLimitMb -TimeoutSeconds $TimeoutSeconds @testArgs
        $projectExitCode = $LASTEXITCODE
        if ($projectExitCode -ne 0)
        {
            $exitCode = $projectExitCode
            break
        }

        if ($Profile)
        {
            $trxPath = Join-Path $projectResultsDirectory 'profile.trx'
            if (Test-Path -LiteralPath $trxPath)
            {
                Write-Host ''
                Write-Host "Slowest test cases from $trxPath ($($project.Name))"
                Write-SlowestTestsFromTrx -TrxPath $trxPath -Top $Top
            }
            else
            {
                Write-Warning "TRX profile was requested, but no profile.trx file was produced in $projectResultsDirectory."
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
    if (-not [string]::IsNullOrWhiteSpace($settingsPath))
    {
        Remove-Item -LiteralPath $settingsPath -Force -ErrorAction SilentlyContinue
    }
}

exit $exitCode
