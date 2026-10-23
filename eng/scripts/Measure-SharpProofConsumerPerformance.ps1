[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageSource,
    [ValidateRange(1, 10)]
    [int]$Iterations = 3,
    [ValidateSet('contracts', 'resources')]
    [string]$Workload = 'contracts',
    [ValidatePattern('^[0-9]+(,[0-9]+)+$')]
    [string]$InputSizes = '0,16,128,1000',
    [ValidateRange(1, 10000)]
    [int]$RuntimeRepetitions = 100,
    [string]$OutputDirectory = 'artifacts/consumer-performance',
    [ValidateRange(0, 3600000)]
    [int]$MaximumBuildMilliseconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scales = @($InputSizes.Split(',') | ForEach-Object { [int]::Parse($_, [Globalization.CultureInfo]::InvariantCulture) })
if (@($scales | Where-Object { $_ -lt 0 -or $_ -gt 1000 }).Count -gt 0 -or @($scales | Select-Object -Unique).Count -ne $scales.Count) {
    throw 'Input sizes must be distinct integers from 0 through 1000.'
}
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.PackageIdentity.psm1') -Force
Assert-SharpProofContainer 'Run consumer performance measurements in the canonical Linux amd64 container.'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$feed = (Resolve-Path -LiteralPath $PackageSource).Path
$packageFiles = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(Get-ChildItem -LiteralPath $feed -File | Where-Object { $_.Extension -in @('.nupkg', '.snupkg') }))
$identity = Get-SharpProofPackageIdentitySet -Files ([object[]]@($packageFiles)) -RequireRepository
$sourceCommit = (& git -C $repositoryRoot rev-parse --verify HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the benchmark repository commit.' }
$trackedChanges = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(& git -C $repositoryRoot status --porcelain --untracked-files=no))
if ($LASTEXITCODE -ne 0) { throw 'Could not identify benchmark working tree changes.' }
$harnessHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
$output = [IO.Path]::GetFullPath($OutputDirectory, $repositoryRoot)
[IO.Directory]::CreateDirectory($output) | Out-Null
$runDirectory = Join-Path $output ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
$compilerServerId = 'sharpproof-performance-' + [Guid]::NewGuid().ToString('N')
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$workspace = Join-Path $temporaryParent ('SharpProof.ConsumerPerformance.' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($workspace) | Out-Null
$measurements = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$runtimeMeasurements = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$runtimeComparisons = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
$ordinal = 0
$originalLocation = Get-Location
$completed = $false
$runtimeSourceHash = $null

function Invoke-MeasuredDotnet {
    param([string]$Label, [string[]]$Arguments)
    $script:ordinal++
    $log = Join-Path $runDirectory ($script:ordinal.ToString('D3') + '-' + $Label + '.log')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1') -TimeoutSeconds 600 -OutputPath $log @Arguments
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    if ($exitCode -ne 0) {
        Get-Content -LiteralPath $log -Tail 25 | Write-Host
        throw "Consumer performance invocation '$Label' failed ($exitCode). See $log"
    }
    return [long]$timer.ElapsedMilliseconds
}

function New-SubjectSource {
    param([int]$Increment)
    $methodBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    for ($index = 1; $index -le 8; $index++) {
        if ($Workload -eq 'resources') {
            $methodBuilder.Add("    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] [ResourceUpperBound(ResourceMetric.AllocatedElements, 0, ""n"", $Increment)] public static int[] Allocate$index(int n) { Contract.Requires(n >= 0 && n <= 1000); var result = new int[0]; for (int i = 0; i < n; i++) { result = new int[$Increment]; } return result; }")
        }
        else {
            $methodBuilder.Add("    public static int Increment$index(int value) { Contract.Requires(value >= 0 && value <= 1000); Contract.Ensures(Contract.Result<int>() == Contract.Old(value) + $Increment); return value + $Increment; }")
        }
    }
    return "using SharpProof.Attributes;`npublic static class Subject {`n" + ($methodBuilder.ToImmutable() -join "`n") + "`n}`n"
}

function New-RuntimeSource {
    return @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
internal static class Program {
    private static int Execute(int size) {
        int checksum = 0;
        var a = Subject.Allocate1(size); checksum += a.Length; GC.KeepAlive(a);
        var b = Subject.Allocate2(size); checksum += b.Length; GC.KeepAlive(b);
        var c = Subject.Allocate3(size); checksum += c.Length; GC.KeepAlive(c);
        var d = Subject.Allocate4(size); checksum += d.Length; GC.KeepAlive(d);
        var e = Subject.Allocate5(size); checksum += e.Length; GC.KeepAlive(e);
        var f = Subject.Allocate6(size); checksum += f.Length; GC.KeepAlive(f);
        var g = Subject.Allocate7(size); checksum += g.Length; GC.KeepAlive(g);
        var h = Subject.Allocate8(size); checksum += h.Length; GC.KeepAlive(h);
        return checksum;
    }
    private static void Main(string[] args) {
        int repetitions = int.Parse(args[1], CultureInfo.InvariantCulture);
        var measurements = new List<object>();
        long frequency = Stopwatch.Frequency;
        foreach (string input in args[2].Split(',')) {
            int size = int.Parse(input, CultureInfo.InvariantCulture);
            for (int warmup = 0; warmup < 10; warmup++) { Execute(size); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            long checksum = 0;
            for (int repeat = 0; repeat < repetitions; repeat++) { checksum += Execute(size); }
            long elapsed = Stopwatch.GetTimestamp() - start;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            measurements.Add(new { inputSize = size, repetitions, kernelElapsedTicks = elapsed,
                stopwatchFrequency = frequency, allocatedBytes = allocated, checksum });
        }
        File.WriteAllText(args[0], JsonSerializer.Serialize(new { runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), os = RuntimeInformation.OSDescription,
            gcServer = System.Runtime.GCSettings.IsServerGC,
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), measurements }));
    }
}
'@
}

try {
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the measured SDK.' }
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    $version = [Security.SecurityElement]::Escape([string]$identity.Version)
    # One isolated package cache shared by the three variants makes restore
    # dependencies comparable. Restore is measured separately, never hidden in build.
    $cache = Join-Path $workspace 'packages'
    $runtimeSource = if ($Workload -eq 'resources') { New-RuntimeSource } else { '' }
    $runtimeSourceHash = if ($Workload -eq 'resources') {
        [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($runtimeSource))).ToLowerInvariant()
    }
    else { $null }
    foreach ($iteration in 1..$Iterations) {
        # Rotate the order to reduce systematic first-run/compiler warmup bias.
        $variants = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@('baseline', 'portable', 'verified'))
        for ($offset = 0; $offset -lt 3; $offset++) {
            $variant = $variants[($offset + $iteration - 1) % 3]
            $consumer = Join-Path $workspace "$iteration-$variant"
            [IO.Directory]::CreateDirectory($consumer) | Out-Null
            $resultFile = Join-Path $consumer 'result.json'
            $references = '<PackageReference Include="SharpProof.Attributes" Version="' + $version + '" />'
            if ($variant -ne 'baseline') {
                $references = '<PackageReference Include="SharpProof" Version="' + $version + '" />'
            }
            $verificationProfile = if ($variant -eq 'verified') { 'strict' } else { 'advisory' }
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>$(if ($Workload -eq 'resources') { 'Exe' } else { 'Library' })</OutputType>
    <LangVersion>12.0</LangVersion>
    <SharedCompilationId>$compilerServerId</SharedCompilationId>
    <NuGetAudit>false</NuGetAudit>
    <SharpProofProfile>$verificationProfile</SharpProofProfile>
    <SharpProofVerifyResultFile>$resultFile</SharpProofVerifyResultFile>
    <WarningsAsErrors>AD0001;CS8032;CS8034;CS8785</WarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>$references</ItemGroup>
</Project>
"@
            [IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), $project, [Text.UTF8Encoding]::new($false))
            $config = @"
<configuration><packageSources><clear/><add key="product" value="$escapedFeed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="product"><package pattern="SharpProof*"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
"@
            [IO.File]::WriteAllText((Join-Path $consumer 'NuGet.config'), $config, [Text.UTF8Encoding]::new($false))
            $sourcePath = Join-Path $consumer 'Subject.cs'
            [IO.File]::WriteAllText($sourcePath, (New-SubjectSource 1), [Text.UTF8Encoding]::new($false))
            if ($Workload -eq 'resources') {
                [IO.File]::WriteAllText((Join-Path $consumer 'Program.cs'), $runtimeSource, [Text.UTF8Encoding]::new($false))
            }
            Set-Location $consumer
            $restoreMilliseconds = Invoke-MeasuredDotnet "$iteration-$variant-restore" @('restore', 'Consumer.csproj', '--packages', $cache)
            $measurements.Add(([ordered]@{ iteration = $iteration; variant = $variant; scenario = 'restore'; elapsedMilliseconds = $restoreMilliseconds; claims = 0; proven = 0; incomplete = 0; cacheStatus = $null }).AsReadOnly())
            foreach ($scenario in [Collections.Immutable.ImmutableArray]::Create[string]('first-build', 'no-op-build', 'warm-rebuild', 'small-edit')) {
                if ($scenario -eq 'small-edit') {
                    [IO.File]::WriteAllText($sourcePath, (New-SubjectSource 2), [Text.UTF8Encoding]::new($false))
                }
                $argumentBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
                [void]$argumentBuilder.AddRange([string[]]@('build', 'Consumer.csproj', '--configuration', 'Release', '--no-restore', '--nologo'))
                if ($scenario -eq 'warm-rebuild') { [void]$argumentBuilder.Add('-t:Rebuild') }
                $arguments = [string[]]@($argumentBuilder.ToImmutable())
                $elapsed = Invoke-MeasuredDotnet "$iteration-$variant-$scenario" $arguments
                $claims = 0
                $proven = 0
                $incomplete = 0
                $cacheStatus = $null
                if ($variant -eq 'verified') {
                    if (-not (Test-Path -LiteralPath $resultFile -PathType Leaf)) { throw "No verification evidence for $scenario." }
                    $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
                    $claims = @($result.claimResults).Count
                    $proven = @($result.claimResults | Where-Object { $_.outcome -ceq 'Proven' -and $_.vacuity -ceq 'None' }).Count
                    $incomplete = @($result.callableResults | Where-Object coverage -CNE 'Complete').Count
                    if ($result.runStatus -cne 'Complete' -or $result.failureReason -cne 'None' -or $claims -ne 8 -or $proven -ne 8 -or $incomplete -ne 0) {
                        throw "The $scenario measurement changed proof quality; expected eight nonvacuous Proven claims with complete coverage."
                    }
                    $cacheStatus = [string]$result.summary.cacheStatus
                }
                $measurements.Add(([ordered]@{ iteration = $iteration; variant = $variant; scenario = $scenario; elapsedMilliseconds = $elapsed; claims = $claims; proven = $proven; incomplete = $incomplete; cacheStatus = $cacheStatus }).AsReadOnly())
            }
            if ($Workload -eq 'resources') {
                $runtimeResultPath = Join-Path $consumer 'runtime.json'
                $runtimeArguments = @((Join-Path $consumer 'bin/Release/net8.0/Consumer.dll'), $runtimeResultPath,
                    $RuntimeRepetitions.ToString([Globalization.CultureInfo]::InvariantCulture), $InputSizes)
                $processMilliseconds = Invoke-MeasuredDotnet "$iteration-$variant-runtime" $runtimeArguments
                $runtimeResult = Get-Content -LiteralPath $runtimeResultPath -Raw | ConvertFrom-Json
                if (@($runtimeResult.measurements).Count -ne $scales.Count) { throw 'Runtime input coverage changed.' }
                $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
                foreach ($sample in $runtimeResult.measurements) {
                    $expectedChecksum = if ($sample.inputSize -eq 0) { 0 } else { 16L * $RuntimeRepetitions }
                    if ($sample.repetitions -ne $RuntimeRepetitions -or $sample.inputSize -notin $scales -or
                        $sample.checksum -ne $expectedChecksum -or $sample.allocatedBytes -lt 0 -or
                        $sample.kernelElapsedTicks -lt 0 -or $sample.stopwatchFrequency -le 0) {
                        throw 'Runtime output or measurement integrity changed.'
                    }
                }
                $runtimeMeasurements.Add([ordered]@{ iteration = $iteration; variant = $variant;
                        processElapsedMilliseconds = $processMilliseconds; sourceSha256 = $sourceHash;
                        runtime = $runtimeResult.runtime; architecture = $runtimeResult.architecture;
                        os = $runtimeResult.os; gcServer = $runtimeResult.gcServer;
                        tieredCompilation = $runtimeResult.tieredCompilation;
                        samples           = @($runtimeResult.measurements)
                    })
            }
        }
    }
    foreach ($measurement in $runtimeMeasurements) {
        $baseline = @($runtimeMeasurements | Where-Object { $_.iteration -eq $measurement.iteration -and $_.variant -eq 'baseline' })
        if ($baseline.Count -ne 1 -or $baseline[0].sourceSha256 -cne $measurement.sourceSha256 -or
            $baseline[0].runtime -cne $measurement.runtime -or $baseline[0].architecture -cne $measurement.architecture -or
            $baseline[0].os -cne $measurement.os -or $baseline[0].gcServer -ne $measurement.gcServer -or
            $baseline[0].tieredCompilation -cne $measurement.tieredCompilation) {
            throw 'Runtime baseline identity or coverage changed.'
        }
        foreach ($sample in $measurement.samples) {
            $reference = @($baseline[0].samples | Where-Object inputSize -EQ $sample.inputSize)
            if ($reference.Count -ne 1 -or $reference[0].repetitions -ne $sample.repetitions -or
                $reference[0].checksum -ne $sample.checksum) { throw 'Runtime baseline output changed.' }
            $elapsedMilliseconds = 1000.0 * $sample.kernelElapsedTicks / $sample.stopwatchFrequency
            $baselineMilliseconds = 1000.0 * $reference[0].kernelElapsedTicks / $reference[0].stopwatchFrequency
            $runtimeComparisons.Add([ordered]@{ iteration = $measurement.iteration; variant = $measurement.variant;
                    inputSize = $sample.inputSize; repetitions = $sample.repetitions;
                    baselineKernelMilliseconds = $baselineMilliseconds; kernelMilliseconds = $elapsedMilliseconds;
                    kernelDeltaMilliseconds = $elapsedMilliseconds - $baselineMilliseconds;
                    baselineAllocatedBytes = $reference[0].allocatedBytes; allocatedBytes = $sample.allocatedBytes;
                    allocatedDeltaBytes     = $sample.allocatedBytes - $reference[0].allocatedBytes
                })
        }
    }
    $completed = $true
}
finally {
    Set-Location $originalLocation
    $measurementSnapshot = $measurements.ToImmutable()
    $report = ([ordered]@{
            schemaVersion            = 3
            completed                = $completed
            measuredAtUtc            = [DateTime]::UtcNow.ToString('O')
            source                   = ([ordered]@{
                    commit            = $sourceCommit
                    hasTrackedChanges = $trackedChanges.Length -gt 0
                    harnessSha256     = $harnessHash
                }).AsReadOnly()
            packageVersion           = [string]$identity.Version
            packageRepositoryCommits = $identity.RepositoryCommits
            sdk                      = if (Get-Variable sdk -ErrorAction SilentlyContinue) { $sdk } else { $null }
            platform                 = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
            processorCount           = [Environment]::ProcessorCount
            compilerServerScope      = 'One compiler server identity per run, shared by all variants and iterations; independent runs do not share analyzer loading state.'
            iterations               = $Iterations
            workload                 = $Workload
            runtimeSourceSha256      = $runtimeSourceHash
            runtimeRepetitions       = $RuntimeRepetitions
            inputSizes               = $scales
            scope                    = 'Synthetic eight-method consumer; restore separated; baseline has Attributes only; portable uses one SharpProof reference with explicit advisory; verified uses strict verification. First build has clean outputs, not a cold OS cache. Warm rebuild reuses package/compiler caches. No-op evidence can be reused and is not a fresh proof. Resource runtime samples execute the final small-edit source with ten warmups per input. Kernel allocation is cumulative bytes on the current thread, not peak or retained memory. Process time includes startup and serialization. Measurements do not prove asymptotic, tight, expected or amortized bounds.'
            packages                 = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]](@($packageFiles | Sort-Object Name | ForEach-Object { ([ordered]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }).AsReadOnly() })))
            measurements             = $measurementSnapshot
            runtimeMeasurements      = $runtimeMeasurements.ToImmutable()
            runtimeComparisons       = $runtimeComparisons.ToImmutable()
        }).AsReadOnly()
    $reportPath = Join-Path $runDirectory 'measurements.json'
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    Write-Host "Consumer performance report: $reportPath"
    $resolvedWorkspace = Resolve-SharpProofContainedPath -Root $temporaryParent -Path $workspace -ParameterName 'Consumer performance workspace'
    Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
}
if ($MaximumBuildMilliseconds -gt 0) {
    $overBudget = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($measurementSnapshot | Where-Object { $_.scenario -ne 'restore' -and $_.elapsedMilliseconds -gt $MaximumBuildMilliseconds }))
    if ($overBudget.Length -gt 0) { throw "$($overBudget.Length) consumer measurements exceeded the explicit $MaximumBuildMilliseconds ms budget. See $reportPath" }
}
foreach ($scenario in [Collections.Immutable.ImmutableArray]::Create[string]('first-build', 'no-op-build', 'warm-rebuild', 'small-edit')) {
    $medianBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::OrdinalIgnoreCase)
    foreach ($variant in [Collections.Immutable.ImmutableArray]::Create[string]('baseline', 'portable', 'verified')) {
        $times = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($measurementSnapshot | Where-Object { $_.scenario -eq $scenario -and $_.variant -eq $variant } | ForEach-Object elapsedMilliseconds | Sort-Object))
        $middle = [int][Math]::Floor($times.Length / 2)
        $median = if ($times.Length % 2 -eq 1) { $times[$middle] } else { ($times[$middle - 1] + $times[$middle]) / 2 }
        $medianBuilder[$variant] = $median
    }
    $medians = $medianBuilder.ToImmutable()
    Write-Host ("{0}: baseline={1}ms portable={2}ms ({3:+0.##;-0.##;0}ms) verified={4}ms ({5:+0.##;-0.##;0}ms)" -f $scenario, $medians['baseline'], $medians['portable'], ($medians['portable'] - $medians['baseline']), $medians['verified'], ($medians['verified'] - $medians['baseline']))
}
