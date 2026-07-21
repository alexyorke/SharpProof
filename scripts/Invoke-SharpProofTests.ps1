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
    [ValidateSet('All', 'Main', 'MainSmt', 'MainGeneral', 'Tooling')]
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

function Get-SharpProofTrxTestCount
{
    <#
    .SYNOPSIS
    Number of test results recorded in a TRX file, or -1 when it cannot be read.

    .DESCRIPTION
    A lane's own output cannot be inspected from here: Invoke-ProcessUnderJobObject
    launches dotnet through CreateProcess with inherited console handles, so the child
    writes straight to the console and never enters this pipeline. Counting the TRX is
    both reliable and independent of message wording.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$TrxPath
    )

    if ([string]::IsNullOrWhiteSpace($TrxPath) -or -not (Test-Path -LiteralPath $TrxPath))
    {
        return -1
    }

    try
    {
        [xml]$trx = Get-Content -LiteralPath $TrxPath
        $namespaceManager = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
        $namespaceManager.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        return @($trx.SelectNodes('//t:UnitTestResult', $namespaceManager)).Count
    }
    catch
    {
        return -1
    }
}

function Test-SharpProofLaneRanTests
{
    <#
    .SYNOPSIS
    Returns $false when a lane ran no tests because its own filter selected nothing.

    .DESCRIPTION
    `dotnet test` treats a filter matching nothing as success and exits 0, which is how
    four fixture-name lanes here rotted into running nothing while still reporting green.
    Only the lane's own filter is judged. A user-supplied -Filter that misses a lane is
    legitimate — filtering to one fixture necessarily empties the other lanes — so the
    caller suppresses this check whenever the user passed a filter.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$TrxPath
    )

    return (Get-SharpProofTrxTestCount -TrxPath $TrxPath) -ne 0
}

function Write-SharpProofEmptyLaneError
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$LaneName,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$LaneFilter
    )

    Write-Host ''
    Write-Host "The $LaneName lane ran no tests. Its filter no longer selects anything:" -ForegroundColor Red
    Write-Host "  $LaneFilter" -ForegroundColor Red
    Write-Host 'Update the lane so the partition covers the fixtures that exist.' -ForegroundColor Red
}

function Resolve-SharpProofTestProjects
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('All', 'Main', 'MainSmt', 'MainGeneral', 'Tooling')]
        [string]$RequestedLane,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Filter
    )

    $mainPath = 'SharpProof.Test\SharpProof.Test.csproj'
    # MainSmt and MainGeneral are exact complements over the SmtHeavy category, so this
    # partition stays correct as fixtures are added and removed. Do not shard by fixture
    # name again: four such lanes previously decayed into matching nothing at all when
    # the fixtures they listed were deleted, and the run still reported success.
    $lanes = [ordered]@{
        Main = [ordered]@{ Name = 'Main'; ProjectPath = $mainPath; LaneFilter = '' }
        MainSmt = [ordered]@{ Name = 'MainSmt'; ProjectPath = $mainPath; LaneFilter = 'TestCategory=SmtHeavy' }
        MainGeneral = [ordered]@{ Name = 'MainGeneral'; ProjectPath = $mainPath; LaneFilter = 'TestCategory!=SmtHeavy' }
        Tooling = [ordered]@{ Name = 'Tooling'; ProjectPath = 'SharpProof.ToolingTest\SharpProof.ToolingTest.csproj'; LaneFilter = '' }
    }
    $mainShards = @($lanes.MainSmt, $lanes.MainGeneral)

    if ([string]::IsNullOrWhiteSpace($Filter))
    {
        if ($RequestedLane -eq 'All') { return $mainShards + @($lanes.Tooling) }
        if ($RequestedLane -eq 'Main') { return $mainShards }
        if ($RequestedLane -eq 'MainSmt') { return @($lanes.MainSmt) }
    }
    if ($RequestedLane -notin @('All', 'Main', 'MainSmt')) { return @($lanes[$RequestedLane]) }

    if ([string]::IsNullOrWhiteSpace($Filter))
    {
        return @($lanes.MainSmt, $lanes.MainGeneral, $lanes.Tooling)
    }

    switch ($RequestedLane)
    {
        'Main' { return @($lanes.Main) }
        'MainSmt' { return @($lanes.MainSmt) }
        default { return @($lanes.Main, $lanes.Tooling) }
    }
}

function Get-SharpProofDefaultWorkerCount
{
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Main', 'MainSmt', 'MainGeneral', 'Tooling')]
        [string]$LaneName
    )

    $cap = if ($LaneName -eq 'Tooling') { 20 }
        elseif ($LaneName.StartsWith('MainSmt', [StringComparison]::Ordinal)) { 4 }
        else { 8 }
    return [Math]::Max(1, [Math]::Min([Environment]::ProcessorCount, $cap))
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

    if ($WorkerCount -le 0 -and -not $EnableFailFast) { return '' }

    $settingsPath = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpproof-test-' + [guid]::NewGuid().ToString('N') + '.runsettings')
    $runConfigurationXml = if ($WorkerCount -gt 0) { "<RunConfiguration><MaxCpuCount>$WorkerCount</MaxCpuCount></RunConfiguration>" } else { '<RunConfiguration />' }
    $workerXml = if ($WorkerCount -gt 0) { "<NumberOfTestWorkers>$WorkerCount</NumberOfTestWorkers>" } else { '' }
    $failFastXml = if ($EnableFailFast) { '<StopOnError>true</StopOnError>' } else { '' }

    $settingsXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  $runConfigurationXml
  <NUnit>$workerXml$failFastXml</NUnit>
</RunSettings>
"@

    [System.IO.File]::WriteAllText($settingsPath, $settingsXml + "`n", [System.Text.UTF8Encoding]::new($false))
    return $settingsPath
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$laneSettingsPaths = New-Object System.Collections.Generic.List[string]

# Every lane writes a TRX so its test count can be checked; a lane whose filter selects
# nothing must fail rather than pass silently. When the caller did not ask to keep
# results, the directory is temporary and removed on the way out.
$effectiveResultsDirectory = $ResultsDirectory
$temporaryResultsDirectory = ''
if ([string]::IsNullOrWhiteSpace($effectiveResultsDirectory))
{
    $temporaryResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpproof-test-results-' + [guid]::NewGuid().ToString('N'))
    $effectiveResultsDirectory = $temporaryResultsDirectory
}

# A user-supplied filter legitimately empties lanes it does not select, so the
# empty-lane check only judges the lane filters the script itself owns.
$enforceNonEmptyLanes = [string]::IsNullOrWhiteSpace($Filter)

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
                ($project.Name -eq 'MainSmt' -or $project.Name -eq 'Tooling'))
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

        # One TRX serves both the empty-lane check and -Profile.
        $testArgs.Add('--logger')
        $testArgs.Add('trx;LogFileName=lane.trx')

        foreach ($argument in $DotnetTestArgs)
        {
            $testArgs.Add($argument)
        }

        [pscustomobject]@{
            Name = [string]$project.Name
            ProjectPath = [string]$project.ProjectPath
            WorkerCount = $projectWorkers
            TestArgs = $testArgs
            Filter = $project.LaneFilter
            ResultsDirectory = $projectResultsDirectory
            TrxPath = (Join-Path $projectResultsDirectory 'lane.trx')
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
        $foregroundExitCode = $LASTEXITCODE
        if ($foregroundExitCode -eq 0 -and
            $enforceNonEmptyLanes -and
            -not (Test-SharpProofLaneRanTests -TrxPath $foregroundSpec.TrxPath))
        {
            Write-SharpProofEmptyLaneError -LaneName $foregroundSpec.Name -LaneFilter $foregroundSpec.Filter
            $foregroundExitCode = 1
        }

        $laneExitCodes = @{ ($foregroundSpec.Name) = $foregroundExitCode }

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

            if ($laneExitCodes[$backgroundLane.Spec.Name] -eq 0 -and
                $enforceNonEmptyLanes -and
                -not (Test-SharpProofLaneRanTests -TrxPath $backgroundLane.Spec.TrxPath))
            {
                Write-SharpProofEmptyLaneError -LaneName $backgroundLane.Spec.Name -LaneFilter $backgroundLane.Spec.Filter
                $laneExitCodes[$backgroundLane.Spec.Name] = 1
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
                $trxPath = $spec.TrxPath
                if (Test-Path -LiteralPath $trxPath)
                {
                    Write-Host ''
                    Write-Host "Slowest test cases from $trxPath ($($spec.Name))"
                    Write-SlowestTestsFromTrx -TrxPath $trxPath -Top $Top
                }
                else
                {
                    Write-Warning "TRX profile was requested, but no lane.trx file was produced in $($spec.ResultsDirectory)."
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
            if ($projectExitCode -eq 0 -and
                $enforceNonEmptyLanes -and
                -not (Test-SharpProofLaneRanTests -TrxPath $spec.TrxPath))
            {
                Write-SharpProofEmptyLaneError -LaneName $spec.Name -LaneFilter $spec.Filter
                $projectExitCode = 1
            }

            if ($projectExitCode -ne 0)
            {
                $exitCode = $projectExitCode
                break
            }

            if ($Profile)
            {
                $trxPath = $spec.TrxPath
                if (Test-Path -LiteralPath $trxPath)
                {
                    Write-Host ''
                    Write-Host "Slowest test cases from $trxPath ($($spec.Name))"
                    Write-SlowestTestsFromTrx -TrxPath $trxPath -Top $Top
                }
                else
                {
                    Write-Warning "TRX profile was requested, but no lane.trx file was produced in $($spec.ResultsDirectory)."
                }
            }
        }
    }
}
finally
{
    Pop-Location
    foreach ($laneSettingsPath in $laneSettingsPaths)
    {
        if (-not [string]::IsNullOrWhiteSpace($laneSettingsPath))
        {
            Remove-Item -LiteralPath $laneSettingsPath -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($temporaryResultsDirectory))
    {
        Remove-Item -LiteralPath $temporaryResultsDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit $exitCode
