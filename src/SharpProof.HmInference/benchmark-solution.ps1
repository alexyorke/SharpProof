param(
    [Parameter(Mandatory)][string] $Cli,
    [Parameter(Mandatory)][string] $SourceCommit,
    [Parameter(Mandatory)][string] $Evidence,
    [int] $Projects = 12,
    [int] $Replicas = 4,
    [int] $Samples = 3,
    [string] $Label = 'solution-baseline',
    [string] $Compare,
    [string] $EnvironmentId = 'unspecified'
)
$ErrorActionPreference = 'Stop'
if ($Projects -le 0 -or $Replicas -le 0 -or $Samples -le 0) { throw 'Projects, Replicas and Samples must be positive.' }
if ($Label -cnotmatch '^[a-zA-Z0-9_-]+$') { throw 'Label must contain only letters, digits, underscores and hyphens.' }
$cliPath = (Resolve-Path -LiteralPath $Cli).ProviderPath
$evidencePath = [IO.Path]::GetFullPath($Evidence)
[IO.Directory]::CreateDirectory($evidencePath) | Out-Null
$destination = Join-Path $evidencePath "$Label.json"
if ($Compare -and [IO.Path]::GetFullPath($Compare) -eq $destination) { throw 'Use a different Label to preserve the comparison baseline.' }

function Get-Sha256([byte[]] $Bytes) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Invoke-Measurement([string[]] $Command, [string] $Directory, [string] $Log) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Command[0]
    $start.WorkingDirectory = $Directory
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    for ($index = 1; $index -lt $Command.Length; $index++) { $start.ArgumentList.Add($Command[$index]) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw 'Could not start benchmark process.' }
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $text = $output.GetAwaiter().GetResult() + $errors.GetAwaiter().GetResult()
        $watch.Stop()
        [IO.File]::WriteAllText($Log, $text)
        if ($process.ExitCode -ne 0) {
            $excerpt = ($text -split "`n" | Select-Object -Last 35) -join "`n"
            throw "Command failed ($($process.ExitCode)): $($Command[0])`n$excerpt"
        }
        return $watch.Elapsed.TotalSeconds
    }
    finally { $process.Dispose() }
}

function New-BenchmarkFixture([string] $Root, [string] $Repository, [int] $ProjectCount, [int] $ReplicaCount) {
    [IO.File]::Copy((Join-Path $Repository 'global.json'), (Join-Path $Root 'global.json'))
    [IO.File]::WriteAllText((Join-Path $Root 'NuGet.Config'), "<configuration><packageSources><clear/></packageSources></configuration>`n")
    [IO.File]::WriteAllText((Join-Path $Root '.editorconfig'), "root=true`n[*.cs]`ndotnet_diagnostic.HM0001.severity=warning`n")
    $expected = [Collections.Generic.List[string]]::new()
    $entries = [Collections.Generic.List[string]]::new()
    for ($project = 0; $project -lt $ProjectCount; $project++) {
        $directory = Join-Path $Root "Node$project"
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $dependencies = if ($project) { @($project - 1; [int][Math]::Floor(($project - 1) / 2)) | Sort-Object -Unique } else { @() }
        $references = ($dependencies | ForEach-Object { '<ProjectReference Include="../Node{0}/Node{0}.csproj"/>' -f $_ }) -join ''
        $projectSource = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>' + $references + "</ItemGroup></Project>`n"
        [IO.File]::WriteAllText((Join-Path $directory "Node$project.csproj"), $projectSource)
        $entries.Add(('<Project Path="Node{0}/Node{0}.csproj"/>' -f $project))
        [IO.File]::WriteAllText((Join-Path $directory 'Contract.cs'), "namespace Node$project;public static class Contract{public static T Echo<T>(T value)=>value;public static int Concrete()=>42;public static void Touch(){}}`n")
        $dependency = if ($project) { $project - 1 } else { 0 }
        $families = @(
            @('Identity', 'object', 'object value', '=>value;', 'T1', 'T1 value', ''),
            @('Nullable', 'string?', 'string? value', '=>value;', 'T1?', 'T1? value', ' where T1 : class'),
            @('Async', 'System.Threading.Tasks.Task<object>', 'object value', '{await System.Threading.Tasks.Task.Yield();return value;}', 'global::System.Threading.Tasks.Task<T1>', 'T1 value', ''),
            @('ValueAsync', 'System.Threading.Tasks.ValueTask<object>', 'object value', '{await System.Threading.Tasks.Task.Yield();return value;}', 'global::System.Threading.Tasks.ValueTask<T1>', 'T1 value', ''),
            @('Array', 'object[]', 'object[] values', '=>values;', 'T1[]', 'T1[] values', ''),
            @('List', 'System.Collections.Generic.List<object>', 'System.Collections.Generic.List<object> values', '=>values;', 'global::System.Collections.Generic.List<T1>', 'global::System.Collections.Generic.List<T1> values', ''),
            @('Callback', 'object', 'System.Func<object> callback', '=>callback();', 'T1', 'global::System.Func<T1> callback', ''),
            @('Iterator', 'System.Collections.Generic.IEnumerable<object>', 'object value', '{yield return value;}', 'global::System.Collections.Generic.IEnumerable<T1>', 'T1 value', ''),
            @('Dependency', 'object', 'object value', "=>Node$dependency.Contract.Echo(value);", 'T1', 'T1 value', '')
        )
        foreach ($family in $families) {
            $methods = [Collections.Generic.List[string]]::new()
            for ($replica = 0; $replica -lt $ReplicaCount; $replica++) {
                $name = "$($family[0])${project}_$replica"
                $modifier = if ($family[0] -in @('Async', 'ValueAsync')) { 'async ' } else { '' }
                $methods.Add("public static $modifier$($family[1]) $name($($family[2]))$($family[3])")
                $expected.Add("$($family[4]) ${name}<T1>($($family[5]))$($family[6])")
            }
            [IO.File]::WriteAllText((Join-Path $directory "$($family[0]).cs"), "namespace Node$project;public static partial class Methods{" + ($methods -join "`n") + "}`n")
        }
    }
    [IO.File]::WriteAllText((Join-Path $Root 'Benchmark.slnx'), '<Solution>' + ($entries -join '') + "</Solution>`n")
    $material = [IO.MemoryStream]::new()
    try {
        foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName) {
            $relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
            $prefix = [Text.Encoding]::UTF8.GetBytes($relative + [char]0)
            $material.Write($prefix)
            $material.Write([IO.File]::ReadAllBytes($file.FullName))
            $material.WriteByte(0)
        }
        $sorted = $expected.ToArray()
        [Array]::Sort($sorted, [StringComparer]::Ordinal)
        return [pscustomobject]@{ Expected = $sorted; FixtureHash = Get-Sha256 $material.ToArray() }
    }
    finally { $material.Dispose() }
}

$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not determine the .NET SDK version.' }
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('hm-solution-benchmark.' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
try {
    $fixture = New-BenchmarkFixture $fixtureRoot $PWD.ProviderPath $Projects $Replicas
    $quality = Get-Sha256 ([Text.Encoding]::UTF8.GetBytes(($fixture.Expected -join "`n")))
    $identity = [ordered]@{
        schema = 1; environment_id = $EnvironmentId; sdk = $sdk; platform = [Environment]::OSVersion.Platform.ToString()
        architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        fixture_sha256 = $fixture.FixtureHash; quality_sha256 = $quality; projects = $Projects; replicas = $Replicas
    }
    $baseline = $null
    if ($Compare) {
        if ($EnvironmentId -eq 'unspecified') { throw 'Comparison requires an explicit EnvironmentId.' }
        $baseline = Get-Content -LiteralPath $Compare -Raw | ConvertFrom-Json
        foreach ($field in $identity.Keys) {
            if ($baseline.$field -cne $identity[$field]) { throw "Incomparable baseline: $field" }
        }
        if (-not [double]::IsFinite($baseline.median_seconds) -or $baseline.median_seconds -le 0) { throw 'Invalid baseline median.' }
    }
    $setupSeconds = Invoke-Measurement @('dotnet', 'build', 'Benchmark.slnx', '--configuration', 'Release') $fixtureRoot (Join-Path $evidencePath "$Label-setup.log")
    $measurements = [Collections.Generic.List[object]]::new()
    for ($sample = 1; $sample -le $Samples; $sample++) {
        $log = Join-Path $evidencePath "$Label-sample-$sample.log"
        $elapsed = Invoke-Measurement @('dotnet', $cliPath, '--solution', (Join-Path $fixtureRoot 'Benchmark.slnx'), '--configuration', 'Release', '--no-restore', '--no-build') $fixtureRoot $log
        $output = [IO.File]::ReadAllText($log)
        [string[]] $actual = @([regex]::Matches($output, "Body admits '([^']+)'\. Review as an API change\.") | ForEach-Object { $_.Groups[1].Value })
        [Array]::Sort($actual, [StringComparer]::Ordinal)
        if (($actual -join "`n") -cne ($fixture.Expected -join "`n")) {
            $difference = Compare-Object $fixture.Expected $actual -CaseSensitive | Select-Object -First 8 | Out-String
            throw "Quality mismatch: expected $($fixture.Expected.Count), got $($actual.Count).`n$difference"
        }
        $counts = @([regex]::Matches($output, 'C# project compilations analyzed: (\d+)') | ForEach-Object { $_.Groups[1].Value })
        if ($counts.Count -ne 1 -or $counts[0] -cne [string]$Projects) { throw "Project coverage mismatch: $counts" }
        $measurements.Add([ordered]@{ ordinal = $sample; seconds = $elapsed; suggestions = $actual.Count; project_compilations = $Projects; quality_sha256 = $quality })
        Write-Output ('PASS SolutionBenchmark sample={0} projects={1} suggestions={2} seconds={3:F3}' -f $sample, $Projects, $actual.Count, $elapsed)
    }
    [double[]] $times = @($measurements | ForEach-Object { $_.seconds } | Sort-Object)
    $middle = [int][Math]::Floor($times.Length / 2)
    $median = if ($times.Length % 2) { $times[$middle] } else { ($times[$middle - 1] + $times[$middle]) / 2 }
    $report = [ordered]@{
        schema = 1; source_commit = $SourceCommit; cli_sha256 = Get-Sha256 ([IO.File]::ReadAllBytes($cliPath))
        environment_id = $EnvironmentId; sdk = $sdk; platform = $identity.platform; architecture = $identity.architecture
        projects = $Projects; replicas = $Replicas; families = 9; source_methods = $Projects * (9 * $Replicas + 3)
        expected_suggestions = $fixture.Expected.Count; fixture_sha256 = $fixture.FixtureHash; quality_sha256 = $quality
        setup_seconds = $setupSeconds
        measurement   = 'Fresh CLI processes; solution loading, inference, validation and diagnostic pipe capture included; restore/native build excluded and recorded separately; first sample is not claimed to be a cold-cache run.'
        samples = $measurements.ToArray(); median_seconds = $median; minimum_seconds = $times[0]; maximum_seconds = $times[-1]
    }
    if ($baseline) {
        $report.baseline_source_commit = $baseline.source_commit
        $report.baseline_median_seconds = $baseline.median_seconds
        $report.baseline_over_current_ratio = $baseline.median_seconds / $median
    }
    [IO.File]::WriteAllText($destination, ($report | ConvertTo-Json -Depth 8) + "`n")
    Write-Output ('Solution benchmark median: {0:F3}s; evidence: {1}' -f $median, $destination)
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    if ([IO.Path]::GetDirectoryName($resolvedFixture) -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) -or -not [IO.Path]::GetFileName($resolvedFixture).StartsWith('hm-solution-benchmark.', [StringComparison]::Ordinal)) {
        throw 'Refusing to remove an unexpected fixture directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
