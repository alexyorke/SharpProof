[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResultsDirectory = 'artifacts/coverage',

    # Instrumented semantic runs take about 20 minutes on hosted runners.
    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 3600,

    [Parameter()]
    [string]$TestFilter = '',

    # Optional aggregate line-coverage floor; 0 only reports.
    [Parameter()]
    [ValidateRange(0, 100)]
    [double]$MinimumLinePercent = 0,

    # All collects and merges everything over the already-built Release
    # solution. CI shards the collection: Semantic (one task group) and
    # Package (one partition) build their own closures, Isolated runs the
    # separate Attributes and Fuzz-console hosts, and Merge combines every
    # shard's lines.tsv under ResultsDirectory into one summary.
    [Parameter()]
    [ValidateSet('All', 'Semantic', 'Package', 'Isolated', 'Merge')]
    [string]$Part = 'All',

    [Parameter()]
    [ValidateSet('All', 'WorkerA', 'WorkerA1', 'WorkerA4', 'WorkerB', 'Projects', 'ProjectsA', 'ProjectsB', 'GatesCorpus', 'GatesCorpusA', 'GatesCorpusB', 'GatesShards')]
    [string]$TaskGroup = 'All',

    [Parameter()]
    [ValidateRange(0, 5)]
    [int]$PackagePartition = 0,

    [Parameter()]
    [ValidateSet(1, 6)]
    [int]$PackagePartitionCount = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Collects Cobertura line coverage of the C# sources with the Microsoft
# collector, reduces each run to repository-relative line hits, then reports
# one merged summary. A line counts as covered when any report hit it.
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
$results = Resolve-SharpProofContainedPath -Root $repositoryRoot -Path $ResultsDirectory -ParameterName 'ResultsDirectory'
if ($TaskGroup -ne 'All' -and $Part -ne 'Semantic') { throw '-TaskGroup is supported only for the Semantic part.' }
if ($PackagePartitionCount -ne 1 -and $Part -ne 'Package') { throw 'Package partitions are supported only for the Package part.' }
if ($Part -eq 'Semantic' -and $TaskGroup -eq 'All') { throw 'The Semantic part requires a task group.' }

if ($Part -ne 'Merge') {
    Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
    # Each shard owns a subdirectory, so shards run in one workspace merge too.
    $mergeRoot = $results
    $results = switch ($Part) {
        'All' { $mergeRoot }
        'Semantic' { Join-Path $mergeRoot ('semantic-' + $TaskGroup.ToLowerInvariant()) }
        'Package' { Join-Path $mergeRoot ('package-' + $PackagePartition) }
        'Isolated' { Join-Path $mergeRoot 'isolated' }
    }
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }
    [void][IO.Directory]::CreateDirectory($results)
    $dotnetWrapper = Get-SharpProofDotnetWrapperPath
    $managedSettings = Join-Path $repositoryRoot 'eng/coverage/SharpProof.Managed.runsettings'
    $broadFilter = 'TestCategory!=Performance&TestCategory!=Coverage'
    if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
        $broadFilter = "($broadFilter)&($TestFilter)"
    }
    # All runs over the prebuilt solution; shards build only what they run.
    $buildSwitch = if ($Part -eq 'All') { @{ NoBuild = $true } } else { @{ Fast = $true } }

    if ($Part -in @('All', 'Semantic')) {
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofSemanticTests.ps1') `
            -Configuration Release @buildSwitch -TimeoutSeconds $TimeoutSeconds `
            -TestFilter $broadFilter -TaskGroup $TaskGroup `
            -CoverageSettings $managedSettings `
            -CoverageResultsDirectory (Join-Path $results 'semantic') `
            -AttributesCollectedSeparately
        if ($LASTEXITCODE -ne 0) { throw "Semantic coverage failed with exit code $LASTEXITCODE." }
    }

    if ($Part -eq 'Package' -or ($Part -eq 'All' -and [string]::IsNullOrWhiteSpace($TestFilter))) {
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
            -Configuration Release @buildSwitch -TimeoutSeconds $TimeoutSeconds `
            -PackagePartition $PackagePartition -PackagePartitionCount $PackagePartitionCount `
            -CoverageSettings $managedSettings `
            -CoverageResultsDirectory (Join-Path $results 'package')
        if ($LASTEXITCODE -ne 0) { throw "Package coverage failed with exit code $LASTEXITCODE." }
    }

    if ($Part -in @('All', 'Isolated')) {
        # Consumer compilations load SharpProof.Attributes, so the broad runs exclude
        # it; collect it in its own test host.
        [xml]$attributesSettings = Get-Content -LiteralPath $managedSettings -Raw
        $attributesSettings.SelectSingleNode('//ModulePaths').InnerXml =
        '<Include><ModulePath>.*SharpProof\.Attributes\.dll$</ModulePath></Include>'
        $attributesSettingsPath = Join-Path $results 'coverage-attributes.runsettings'
        $attributesSettings.Save($attributesSettingsPath)
        & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds `
            test (Join-Path $repositoryRoot 'src/SharpProof.Attributes.Tests/SharpProof.Attributes.Tests.csproj') `
            -c Release --no-build `
            --settings $attributesSettingsPath `
            --collect 'Code Coverage;Format=Cobertura' `
            --results-directory (Join-Path $results 'attributes')
        if ($LASTEXITCODE -ne 0) { throw "Attributes coverage failed with exit code $LASTEXITCODE." }

        # The real Console interrupt and default campaign need their own testhost,
        # outside the broad suites (Coverage category), over a private output copy.
        [xml]$fuzzSettings = Get-Content -LiteralPath $managedSettings -Raw
        $fuzzSettings.SelectSingleNode('//ModulePaths').InnerXml =
        '<Include><ModulePath>.*SharpProof\.Fuzz\.dll$</ModulePath></Include>'
        $fuzzSettingsPath = Join-Path $results 'coverage-fuzz-console.runsettings'
        $fuzzSettings.Save($fuzzSettingsPath)
        $fuzzOutputRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-fuzz-console-' + [Guid]::NewGuid().ToString('N'))
        $previousIsolation = [Environment]::GetEnvironmentVariable('SHARPPROOF_FUZZ_CONSOLE_ISOLATED')
        try {
            $fuzzOutput = New-SharpProofIsolatedTestOutput `
                -SourceDirectory (Join-Path $repositoryRoot 'artifacts/bin/SharpProof.Fuzz.Tests/Release/net10.0') `
                -DestinationDirectory (Join-Path $fuzzOutputRoot 'net10.0')
            [Environment]::SetEnvironmentVariable('SHARPPROOF_FUZZ_CONSOLE_ISOLATED', '1')
            & $dotnetWrapper -TimeoutSeconds $TimeoutSeconds `
                test (Join-Path $repositoryRoot 'tests/SharpProof.Fuzz.Tests/SharpProof.Fuzz.Tests.csproj') `
                -c Release --no-build --no-restore "-p:OutDir=$fuzzOutput/" `
                --filter 'FullyQualifiedName~FuzzConsoleCancellationTests|FullyQualifiedName~FuzzDefaultCliCoverageTests' `
                --settings $fuzzSettingsPath `
                --collect 'Code Coverage;Format=Cobertura' `
                --results-directory (Join-Path $results 'fuzz-console')
            if ($LASTEXITCODE -ne 0) { throw "Isolated Fuzz console coverage failed with exit code $LASTEXITCODE." }
        }
        finally {
            [Environment]::SetEnvironmentVariable('SHARPPROOF_FUZZ_CONSOLE_ISOLATED', $previousIsolation)
            Remove-SharpProofOwnedDirectory -Directory $fuzzOutputRoot
        }
    }

    # Reduce this run's reports to repository-relative line hits, so shards
    # collected in different task workspaces merge by file and line.
    $covered = [Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::Ordinal)
    $sourcePrefix = $repositoryRoot.Replace('\', '/').TrimEnd('/') + '/'
    foreach ($report in Get-ChildItem -LiteralPath $results -Recurse -Filter '*.cobertura.xml' -File) {
        [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
        $sources = @($document.SelectNodes('//sources/source') |
                ForEach-Object { ([string]$_.InnerText).Replace('\', '/').TrimEnd('/') + '/' })
        foreach ($class in $document.SelectNodes('//class')) {
            $file = ([string]$class.filename).Replace('\', '/')
            if (-not $file.StartsWith('/') -and $sources.Count -gt 0) { $file = $sources[0] + $file }
            if (-not $file.StartsWith($sourcePrefix + 'src/', [StringComparison]::Ordinal)) { continue }
            $file = $file.Substring($sourcePrefix.Length)
            foreach ($line in $class.SelectNodes('lines/line')) {
                $key = $file + ':' + [string]$line.number
                $hit = [long]$line.hits -gt 0
                $covered[$key] = $hit -or ($covered.ContainsKey($key) -and $covered[$key])
            }
        }
    }
    if ($covered.Count -eq 0) { throw 'Coverage collection produced no source lines.' }
    $lines = @($covered.GetEnumerator() | Sort-Object Key | ForEach-Object { $_.Key + "`t" + [int]$_.Value })
    [IO.File]::WriteAllLines((Join-Path $results 'lines.tsv'), [string[]]$lines, [Text.UTF8Encoding]::new($false))
}

if ($Part -notin @('All', 'Merge')) { return }

# Merge every shard's line hits by repository-relative file and line.
$covered = [Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::Ordinal)
$shards = @(Get-ChildItem -LiteralPath $results -Recurse -Filter 'lines.tsv' -File)
if ($shards.Count -eq 0) { throw "No coverage shards (lines.tsv) were found under $results." }
foreach ($shard in $shards) {
    foreach ($entry in [IO.File]::ReadLines($shard.FullName)) {
        $key, $hit = $entry.Split("`t")
        $covered[$key] = $hit -eq '1' -or ($covered.ContainsKey($key) -and $covered[$key])
    }
}

$summary = [Text.StringBuilder]::new()
[void]$summary.Append("# Line coverage`n`n| Project | Covered | Lines | Percent |`n| --- | ---: | ---: | ---: |`n")
$projects = $covered.GetEnumerator() | Group-Object { $_.Key.Split('/')[1] } | Sort-Object Name
foreach ($project in $projects) {
    $hits = @($project.Group | Where-Object { $_.Value }).Count
    [void]$summary.AppendFormat([Globalization.CultureInfo]::InvariantCulture,
        "| {0} | {1} | {2} | {3:N2} |`n", $project.Name, $hits, $project.Count, 100.0 * $hits / $project.Count)
}
$totalHits = @($covered.Values | Where-Object { $_ }).Count
$totalPercent = 100.0 * $totalHits / $covered.Count
[void]$summary.AppendFormat([Globalization.CultureInfo]::InvariantCulture,
    "| **Total** | {0} | {1} | {2:N2} |`n", $totalHits, $covered.Count, $totalPercent)
[IO.File]::WriteAllText((Join-Path $results 'summary.md'), $summary.ToString(), [Text.UTF8Encoding]::new($false))
Write-Host $summary.ToString()
if ($totalPercent -lt $MinimumLinePercent) {
    throw ('Line coverage {0:N2}% is below the {1}% floor.' -f $totalPercent, $MinimumLinePercent)
}
