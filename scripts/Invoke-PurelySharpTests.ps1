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

function Get-PurelySharpTestWorkerProcesses
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
            $commandLine.IndexOf('PurelySharp.Test', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('MSBuild.dll', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $commandLine.IndexOf('VBCSCompiler.dll', [StringComparison]::OrdinalIgnoreCase) -ge 0)
        {
            $process
        }
    }
}

function Stop-NewPurelySharpTestWorkerProcesses
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
            $commandLine.IndexOf('PurelySharp.Test', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$testRunStartedAt = Get-Date
$initialTestWorkerIds = @(Get-PurelySharpTestWorkerProcesses -RepoRoot $repoRoot | ForEach-Object { [int]$_.ProcessId })
$settingsPath = ''
$useGeneratedRunSettings = $Workers -gt 0 -or $FailFast
if ($useGeneratedRunSettings)
{
    $settingsPath = Join-Path ([System.IO.Path]::GetTempPath()) ('purelysharp-test-' + [guid]::NewGuid().ToString('N') + '.runsettings')

    $runConfigurationLines = New-Object System.Collections.Generic.List[string]
    if ($Workers -gt 0)
    {
        $runConfigurationLines.Add("    <MaxCpuCount>$Workers</MaxCpuCount>")
    }

    $nunitLines = New-Object System.Collections.Generic.List[string]
    if ($Workers -gt 0)
    {
        $nunitLines.Add("    <NumberOfTestWorkers>$Workers</NumberOfTestWorkers>")
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
    $effectiveResultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('purelysharp-test-profile-' + [guid]::NewGuid().ToString('N'))
}

if (-not [string]::IsNullOrWhiteSpace($effectiveResultsDirectory))
{
    New-Item -ItemType Directory -Path $effectiveResultsDirectory -Force | Out-Null
}

$testArgs = New-Object System.Collections.Generic.List[string]
$testArgs.Add('test')
$testArgs.Add('PurelySharp.Test\PurelySharp.Test.csproj')
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

if (-not [string]::IsNullOrWhiteSpace($effectiveResultsDirectory))
{
    $testArgs.Add('--results-directory')
    $testArgs.Add($effectiveResultsDirectory)
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

Push-Location $repoRoot
$exitCode = 0
try
{
    & (Join-Path $PSScriptRoot 'Invoke-PurelySharpDotnet.ps1') -MemoryLimitMb $MemoryLimitMb -TimeoutSeconds $TimeoutSeconds @testArgs
    $exitCode = $LASTEXITCODE

    if ($Profile)
    {
        $trxPath = Join-Path $effectiveResultsDirectory 'profile.trx'
        if (Test-Path -LiteralPath $trxPath)
        {
            Write-Host ''
            Write-Host "Slowest test cases from $trxPath"
            Write-SlowestTestsFromTrx -TrxPath $trxPath -Top $Top
        }
        else
        {
            Write-Warning "TRX profile was requested, but no profile.trx file was produced in $effectiveResultsDirectory."
        }
    }
}
finally
{
    Stop-NewPurelySharpTestWorkerProcesses `
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
