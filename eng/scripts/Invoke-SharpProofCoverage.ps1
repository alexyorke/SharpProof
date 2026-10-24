[CmdletBinding()]
param(
    [Parameter()]
    [string]$ResultsDirectory = 'artifacts/coverage',

    [Parameter()]
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1200,

    [Parameter()]
    [string]$TestFilter = '',

    # 'all' runs every phase (default). Each other phase runs one independent
    # part of the default collection so CI can place each part on its own
    # runner: the semantic tasks are split into Gates, Worker remainder, and
    # every other project, plus the package tests. Shard runs leave the
    # coverage gate to a separate job.
    [Parameter()]
    [ValidateSet('all', 'semantic-gates-corpus', 'semantic-projects', 'semantic-gates-corpus-a', 'semantic-gates-corpus-b', 'semantic-gates-shards', 'semantic-worker-a', 'semantic-worker-a-1', 'semantic-worker-a-4', 'semantic-worker-a-4a', 'semantic-worker-a-4b', 'semantic-worker-b', 'semantic-worker-b-2', 'semantic-worker-b-3', 'semantic-analyzer', 'semantic-projects-rest', 'semantic-extras', 'package-0', 'package-1', 'package-2', 'package-3', 'package')]
    [string]$CoveragePhase = 'all'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.CoverageShardEvidence.ps1')

if ($CoveragePhase -ne 'all' -and -not [string]::IsNullOrWhiteSpace($TestFilter)) {
    throw 'CoveragePhase shards cannot be combined with TestFilter.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& (Join-Path $PSScriptRoot 'Test-SharpProofSourceDocuments.ps1')
& (Join-Path $PSScriptRoot 'Test-SharpProofCoverageEvidence.ps1')
$repositoryPrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')
Import-Module (Join-Path `
        $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
$testProjectParallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot
$resolvedResultsDirectory = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $ResultsDirectory `
    -ParameterName 'ResultsDirectory'
if (Test-Path -LiteralPath $resolvedResultsDirectory -PathType Container) {
    $existingReport = Get-ChildItem `
        -LiteralPath $resolvedResultsDirectory `
        -Recurse `
        -Filter '*.cobertura*.xml' `
        -File `
        -ErrorAction Stop |
        Where-Object { Test-SharpProofCoverageReportName -Path $_.Name } |
        Select-Object -First 1
    if ($null -ne $existingReport) {
        throw (
            'ResultsDirectory already contains coverage evidence: ' +
            $existingReport.FullName)
    }
}
New-Item `
    -ItemType Directory `
    -Force `
    -Path $resolvedResultsDirectory |
    Out-Null

# Establish the denominator and module/source authority before any collector
# can instrument or merge binaries. The validator independently recomputes the
# same portable-PDB universe; this file is evidence, not the source of truth.
$coverageAuthorityPath = Join-Path `
    $resolvedResultsDirectory `
    'coverage-authority.json'
$coverageInitialAuthorityTimer = [Diagnostics.Stopwatch]::StartNew()
try {
    & (Join-Path $PSScriptRoot 'Get-SharpProofProductionInventory.ps1') `
        -RepositoryRoot $repositoryRoot `
        -Configuration Release `
        -RequirePdb `
        -OutputPath $coverageAuthorityPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Coverage authority derivation failed before collection.'
    }
}
finally {
    $coverageInitialAuthorityTimer.Stop()
    Write-Host ('Coverage phase: name=initial-authority elapsedMilliseconds={0}' -f $coverageInitialAuthorityTimer.ElapsedMilliseconds)
}

# Export the exact uninstrumented Tool only for the authenticated CI bridge.
# Local semantic-extras collection keeps its existing Linux-only behavior.
if ($CoveragePhase -ceq 'semantic-extras' -and
    -not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ID) -and
    -not [string]::IsNullOrWhiteSpace($env:GITHUB_RUN_ATTEMPT)) {
    & (Join-Path $PSScriptRoot 'Export-SharpProofWindowsToolCoverage.ps1') `
        -AuthorityPath $coverageAuthorityPath `
        -OutputDirectory (Join-Path $repositoryRoot 'artifacts/coverage-windows-input')
    if ($LASTEXITCODE -ne 0) { throw 'Windows Tool coverage input export failed.' }
}

$dotnetWrapper = Get-SharpProofDotnetWrapperPath
$managedSettings = Join-Path `
    $repositoryRoot `
    'eng\coverage\SharpProof.Managed.runsettings'

function Save-CoverageXml {
    param(
        [Parameter(Mandatory = $true)]
        [Xml.XmlDocument]$Document,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $writerSettings = [Xml.XmlWriterSettings]::new()
    $writerSettings.Encoding = [Text.UTF8Encoding]::new($false)
    $writerSettings.Indent = $true
    $writerSettings.NewLineChars = "`n"
    $writerSettings.NewLineHandling = [Xml.NewLineHandling]::Replace
    $writer = [Xml.XmlWriter]::Create($Path, $writerSettings)
    try {
        $Document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function New-CoverageSettings {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ModulePath
    )

    [xml]$settings = Get-Content -LiteralPath $managedSettings -Raw
    $modulePaths = $settings.SelectSingleNode('//ModulePaths')
    $modulePaths.RemoveAll()
    $selector = $settings.CreateElement('Include')
    $module = $settings.CreateElement('ModulePath')
    $module.InnerText = $ModulePath
    [void]$selector.AppendChild($module)
    [void]$modulePaths.AppendChild($selector)
    $staticManaged = $settings.SelectSingleNode(
        '//EnableStaticManagedInstrumentation')
    $staticManaged.InnerText = 'True'

    $path = Join-Path `
        $resolvedResultsDirectory `
    ("coverage-$Name.runsettings")
    Save-CoverageXml -Document $settings -Path $path
    return $path
}

# The Microsoft collector is used instead of Coverlet. Static managed
# instrumentation rewrites assemblies on disk, so concurrent shards receive
# private managed binaries while immutable dependencies remain hard-linked.
# Child collection remains disabled so packaged consumer builds cannot merge a
# second build of the same assembly and PDB into the project coverage universe.
$broadFilter = 'TestCategory!=Performance&TestCategory!=Coverage'
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    $broadFilter = "($broadFilter)&($TestFilter)"
}
if ([string]::IsNullOrWhiteSpace($TestFilter)) {
    if ($CoveragePhase -notmatch '^package(?:-[0-3])?$') {
        $semanticResults = Join-Path $resolvedResultsDirectory 'semantic'
        $coverageTaskGroup = switch ($CoveragePhase) {
            'semantic-gates-corpus' { 'GatesCorpus' }
            'semantic-gates-corpus-a' { 'GatesCorpusA' }
            'semantic-gates-corpus-b' { 'GatesCorpusB' }
            'semantic-gates-shards' { 'GatesShards' }
            'semantic-worker-a' { 'WorkerA' }
            'semantic-worker-a-1' { 'WorkerA1' }
            'semantic-worker-a-4' { 'WorkerA4' }
            'semantic-worker-a-4a' { 'WorkerA4A' }
            'semantic-worker-a-4b' { 'WorkerA4B' }
            'semantic-worker-b' { 'WorkerB' }
            'semantic-worker-b-2' { 'WorkerB2' }
            'semantic-worker-b-3' { 'WorkerB3' }
            'semantic-projects' { 'Projects' }
            'semantic-analyzer' { 'Analyzer' }
            'semantic-projects-rest' { 'ProjectsRest' }
            'semantic-extras' { 'Extras' }
            default { 'All' }
        }
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofSemanticTests.ps1') `
            -Configuration Release `
            -NoBuild `
            -TimeoutSeconds $TimeoutSeconds `
            -TestFilter $broadFilter `
            -CoverageSettings $managedSettings `
            -CoverageResultsDirectory $semanticResults `
            -TaskGroup $coverageTaskGroup `
            -AttributesCollectedSeparately
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Sharded semantic coverage failed with exit code ' +
                "$LASTEXITCODE.")
        }
    }

    if ($CoveragePhase -eq 'all' -or $CoveragePhase -match '^package(?:-[0-3])?$') {
        $packageResults = Join-Path $resolvedResultsDirectory 'package'
        & (Join-Path $PSScriptRoot 'Invoke-SharpProofPackageTests.ps1') `
            -Configuration Release `
            -NoBuild `
            -TimeoutSeconds $TimeoutSeconds `
            -CoverageSettings $managedSettings `
            -CoverageResultsDirectory $packageResults `
            -PackagePartitionCount $(if ($CoveragePhase -match '^package-[0-3]$') { 4 } else { 1 }) `
            -PackagePartition $(if ($CoveragePhase -match '^package-([0-3])$') { [int]$Matches[1] } else { 0 })
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Sharded package coverage failed with exit code ' +
                "$LASTEXITCODE.")
        }
    }
}
else {
    # Preserve a single-invocation targeted probe for arbitrary user filters.
    & $dotnetWrapper `
        -TimeoutSeconds $TimeoutSeconds `
        test (Join-Path $repositoryRoot 'SharpProof.slnx') `
        -c Release `
        --no-build `
        "/m:$testProjectParallelism" `
        --filter $broadFilter `
        --settings $managedSettings `
        --collect 'Code Coverage;Format=Cobertura' `
        --results-directory $resolvedResultsDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Coverage collection failed with exit code $LASTEXITCODE."
    }
}

# Keep Attributes out of broad Worker instrumentation and collect its behavior
# in an isolated testhost, where consumer compilations cannot use its rewritten
# assembly.
$coverageAttributesTimer = [Diagnostics.Stopwatch]::StartNew()
try {
    # The isolated run belongs to the semantic projects shard (or to 'all').
    if ($CoveragePhase -in @('all', 'semantic-projects', 'semantic-extras')) {
        $attributesSettings = New-CoverageSettings `
            -Name 'attributes' `
            -ModulePath '.*SharpProof\.Attributes\.dll$'
        $attributesResults = Join-Path $resolvedResultsDirectory 'attributes'
        & $dotnetWrapper `
            -TimeoutSeconds $TimeoutSeconds `
            test (Join-Path `
                $repositoryRoot `
                'src\SharpProof.Attributes.Tests\SharpProof.Attributes.Tests.csproj') `
            -c Release `
            --no-build `
            --settings $attributesSettings `
            --collect 'Code Coverage;Format=Cobertura' `
            --logger 'trx;LogFilePrefix=attributes' `
            --results-directory $attributesResults
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Isolated Attributes coverage failed with exit code ' +
                "$LASTEXITCODE.")
        }
        Save-SharpProofCoverageCohort -Root $resolvedResultsDirectory `
            -Expected @([pscustomobject]@{ Name = 'attributes'; Target = Join-Path $repositoryRoot 'src/SharpProof.Attributes.Tests/SharpProof.Attributes.Tests.csproj' }) `
            -Completed @([pscustomobject]@{ Test = [pscustomobject]@{ Name = 'attributes' }; ExitCode = 0 })
    }
}
finally {
    $coverageAttributesTimer.Stop()
    Write-Host ('Coverage phase: name=attributes elapsedMilliseconds={0}' -f $coverageAttributesTimer.ElapsedMilliseconds)
}

# Exercise the real Console interrupt and default campaign in one isolated
# testhost. The broad suites exclude Coverage-tagged tests, and this private
# output prevents static instrumentation from changing another collector's
# already-built binaries.
# Merged reports keep maximum hits, so one shard collecting this run is
# enough; it belongs to the semantic projects shard (or to 'all').
if ($CoveragePhase -in @('all', 'semantic-projects', 'semantic-extras')) {
    $fuzzConsoleSettings = New-CoverageSettings `
        -Name 'fuzz-console-cancellation' `
        -ModulePath '.*SharpProof\.Fuzz\.dll$'
    $fuzzConsoleOutputRoot = Join-Path ([IO.Path]::GetTempPath()) (
        'sharpproof-fuzz-console-' + [Guid]::NewGuid().ToString('N'))
    $fuzzConsoleResults = Join-Path $resolvedResultsDirectory 'fuzz-console-cancellation'
    $fuzzConsoleOriginalIsolation = [Environment]::GetEnvironmentVariable(
        'SHARPPROOF_FUZZ_CONSOLE_ISOLATED')
    $fuzzConsoleClock = [Diagnostics.Stopwatch]::StartNew()
    try {
        $fuzzConsoleOutput = New-SharpProofIsolatedTestOutput `
            -SourceDirectory (Join-Path $repositoryRoot (
                'artifacts/bin/SharpProof.Fuzz.Tests/Release/net10.0')) `
            -DestinationDirectory (Join-Path $fuzzConsoleOutputRoot 'net10.0')
        [Environment]::SetEnvironmentVariable(
            'SHARPPROOF_FUZZ_CONSOLE_ISOLATED', '1')
        & $dotnetWrapper `
            -TimeoutSeconds $TimeoutSeconds `
            test (Join-Path $repositoryRoot 'src/SharpProof.Fuzz.Tests/SharpProof.Fuzz.Tests.csproj') `
            -c Release `
            --no-build `
            --no-restore `
            "-p:OutDir=$fuzzConsoleOutput/" `
            --filter 'FullyQualifiedName~FuzzConsoleCancellationTests|FullyQualifiedName~FuzzDefaultCliCoverageTests' `
            --settings $fuzzConsoleSettings `
            --collect 'Code Coverage;Format=Cobertura' `
            --logger 'trx;LogFilePrefix=fuzz-console-cancellation' `
            --results-directory $fuzzConsoleResults
        if ($LASTEXITCODE -ne 0) {
            throw "Isolated Fuzz console coverage failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $fuzzConsoleClock.Stop()
        [Environment]::SetEnvironmentVariable(
            'SHARPPROOF_FUZZ_CONSOLE_ISOLATED', $fuzzConsoleOriginalIsolation)
        [pscustomobject]@{
            # Retain the legacy label and filename; this timing covers both fixtures.
            fixture             = 'FuzzConsoleCancellationTests'
            fixtures            = @('FuzzConsoleCancellationTests', 'FuzzDefaultCliCoverageTests')
            elapsedMilliseconds = [long]$fuzzConsoleClock.Elapsed.TotalMilliseconds
        } | ConvertTo-Json | Set-Content `
            -LiteralPath (Join-Path $resolvedResultsDirectory 'fuzz-console-timing.json') `
            -Encoding utf8NoBOM
        Remove-SharpProofOwnedDirectory -Directory $fuzzConsoleOutputRoot
    }
}

if ($CoveragePhase -in @('all', 'semantic-projects', 'semantic-extras')) {
    Save-SharpProofCoverageCohort -Root $resolvedResultsDirectory `
        -Expected @(
        [pscustomobject]@{ Name = 'attributes'; Target = Join-Path $repositoryRoot 'src/SharpProof.Attributes.Tests/SharpProof.Attributes.Tests.csproj' },
        [pscustomobject]@{ Name = 'fuzz-console-cancellation'; Target = Join-Path $repositoryRoot 'src/SharpProof.Fuzz.Tests/SharpProof.Fuzz.Tests.csproj' }) `
        -Completed @(
        [pscustomobject]@{ Test = [pscustomobject]@{ Name = 'attributes' }; ExitCode = 0 },
        [pscustomobject]@{ Test = [pscustomobject]@{ Name = 'fuzz-console-cancellation' }; ExitCode = 0 })
}

# The container entrypoint builds each task in a disposable clone while the
# coverage artifacts are persisted through the shared artifacts directory.
# The Microsoft collector writes absolute clone paths into Cobertura. Rewrite
# only paths proven to be inside this checkout to repository-relative names so
# the persisted report remains valid after the disposable clone is removed and
# the policy gate evaluates the same canonical source identity everywhere.
$coverageReports = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
        Get-ChildItem `
            -LiteralPath $resolvedResultsDirectory `
            -Recurse `
            -Filter '*.cobertura*.xml' `
            -File `
            -ErrorAction Stop |
            Where-Object { Test-SharpProofCoverageReportName -Path $_.Name }))
$coverageAuthority = Get-Content `
    -LiteralPath $coverageAuthorityPath `
    -Raw | ConvertFrom-Json
$coverageModuleIdentities = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
        $coverageAuthority.modules |
            ForEach-Object {
                [string]$_.project + ':' + [string]$_.assemblyName + ':' +
                [string]$_.moduleMvid + ':' + [string]$_.pdbCodeViewGuid
            } |
            Sort-Object))
$normalizer = Join-Path $PSScriptRoot 'Normalize-SharpProofCoverageReport.ps1'
$authorityCommit = [string]$coverageAuthority.commit
$moduleIdentities = $coverageModuleIdentities -join ','
# Each collector writes an independent XML file. Rewrite all reports,
# including child collector evidence, while using at most four CPU lanes.
$normalizationParallelism = [Math]::Min(4, (Get-SharpProofSemanticTestParallelism -RepositoryRoot $repositoryRoot))
$coverageNormalizationBytes = [long](@(
        $coverageReports | Measure-Object -Property Length -Sum).Sum)
$coverageNormalizationTimer = [Diagnostics.Stopwatch]::StartNew()
try {
    $normalizationResults = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($coverageReports | ForEach-Object -Parallel {
                $reportPath = $_.FullName
                try {
                    $scriptPath = $using:normalizer
                    & $scriptPath `
                        -Path $reportPath `
                        -RepositoryPrefix $using:repositoryPrefix `
                        -AuthorityCommit $using:authorityCommit `
                        -ModuleIdentities $using:moduleIdentities
                    [pscustomobject]@{ Path = $reportPath; Error = '' }
                }
                catch {
                    [pscustomobject]@{
                        Path  = $reportPath
                        Error = $_.Exception.Message
                    }
                }
            } -ThrottleLimit $normalizationParallelism))
    if ($normalizationResults.Length -ne $coverageReports.Length) {
        throw 'Coverage normalization did not return a result for every report.'
    }
    foreach ($result in $normalizationResults) {
        if (-not [string]::IsNullOrEmpty([string]$result.Error)) {
            throw "Coverage normalization failed for '$($result.Path)': $($result.Error)"
        }
    }
}
finally {
    $coverageNormalizationTimer.Stop()
    Write-Host ('Coverage phase: name=normalization elapsedMilliseconds={0} reportCount={1} reportBytes={2} parallelism={3}' -f $coverageNormalizationTimer.ElapsedMilliseconds, $coverageReports.Length, $coverageNormalizationBytes, $normalizationParallelism)
}
if ($CoveragePhase -ne 'all') {
    Save-SharpProofCoverageShardManifest -Root $resolvedResultsDirectory -Phase $CoveragePhase
}
