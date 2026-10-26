[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResultsDirectory = 'artifacts/coverage',

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1200,

    [Parameter()]
    [string]$TestFilter = '',

    # Optional aggregate line-coverage floor; 0 only reports.
    [Parameter()]
    [ValidateRange(0, 100)]
    [double]$MinimumLinePercent = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Collects Cobertura line coverage of the C# sources with the Microsoft
# collector over the already-built Release solution, then reports one merged
# summary. A line counts as covered when any report hit it.
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$results = Resolve-SharpProofContainedPath -Root $repositoryRoot -Path $ResultsDirectory -ParameterName 'ResultsDirectory'
if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force }
[void][IO.Directory]::CreateDirectory($results)
$dotnetWrapper = Get-SharpProofDotnetWrapperPath
$managedSettings = Join-Path $repositoryRoot 'eng/coverage/SharpProof.Managed.runsettings'
$broadFilter = 'TestCategory!=Performance&TestCategory!=Coverage'
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    $broadFilter = "($broadFilter)&($TestFilter)"
}

& (Join-Path $PSScriptRoot 'Invoke-SharpProofSemanticTests.ps1') `
    -Configuration Release -NoBuild -TimeoutSeconds $TimeoutSeconds `
    -TestFilter $broadFilter `
    -CoverageSettings $managedSettings `
    -CoverageResultsDirectory (Join-Path $results 'semantic') `
    -AttributesCollectedSeparately
if ($LASTEXITCODE -ne 0) { throw "Semantic coverage failed with exit code $LASTEXITCODE." }

if ([string]::IsNullOrWhiteSpace($TestFilter)) {
    & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
        -Configuration Release -NoBuild -TimeoutSeconds $TimeoutSeconds `
        -CoverageSettings $managedSettings `
        -CoverageResultsDirectory (Join-Path $results 'package')
    if ($LASTEXITCODE -ne 0) { throw "Package coverage failed with exit code $LASTEXITCODE." }
}

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

# Merge every report by repository-relative file and line.
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
