[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipBuild,

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$acceptanceRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $acceptanceRoot '..\..')).Path
$contractPath = Join-Path $acceptanceRoot 'contract.json'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
. (Join-Path $repositoryRoot 'scripts\SharpProof.FuzzEvidenceLifecycle.ps1')
$pullRequestCases = Assert-SharpProofFuzzCaseBudget `
    -Value $contract.fuzz.pullRequestCases `
    -Name 'contract.fuzz.pullRequestCases'

# BEGIN ACCEPTANCE TIMELINE AUTHORITY
function Test-AcceptanceTimingTimeline {
    param(
        [Parameter(Mandatory = $true)][DateTime]$StartedUtc,
        [Parameter(Mandatory = $true)][DateTime]$CompletedUtc,
        [Parameter(Mandatory = $true)][long]$TotalElapsedMilliseconds,
        [Parameter(Mandatory = $true)][object[]]$Phases,
        [Parameter(Mandatory = $true)][string[]]$ExpectedPhaseNames,
        [Parameter(Mandatory = $true)][bool]$RequireComplete
    )

    $outerTicks = ($CompletedUtc - $StartedUtc).Ticks
    if ($StartedUtc.Kind -ne [DateTimeKind]::Utc -or
        $CompletedUtc.Kind -ne [DateTimeKind]::Utc -or
        $outerTicks -lt 0 -or
        $outerTicks % [TimeSpan]::TicksPerMillisecond -ne 0 -or
        $TotalElapsedMilliseconds -ne
            [long]($outerTicks / [TimeSpan]::TicksPerMillisecond) -or
        ($RequireComplete -and $Phases.Count -ne $ExpectedPhaseNames.Count) -or
        (-not $RequireComplete -and
            ($Phases.Count -lt 1 -or
             $Phases.Count -gt $ExpectedPhaseNames.Count))) {
        throw 'Acceptance outer timing interval is invalid.'
    }
    $previousCompleted = $StartedUtc
    for ($index = 0; $index -lt $Phases.Count; $index++) {
        $phase = $Phases[$index]
        $phaseStart = [DateTime]::Parse(
            [string]$phase.startedUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
        $phaseCompleted = [DateTime]::Parse(
            [string]$phase.completedUtc,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
        $phaseTicks = ($phaseCompleted - $phaseStart).Ticks
        if ([string]$phase.name -cne $ExpectedPhaseNames[$index] -or
            [string]$phase.status -cnotin @('passed','failed','skipped') -or
            $phaseStart.Kind -ne [DateTimeKind]::Utc -or
            $phaseCompleted.Kind -ne [DateTimeKind]::Utc -or
            $phaseStart -lt $StartedUtc -or
            $phaseCompleted -gt $CompletedUtc -or
            $phaseStart -lt $previousCompleted -or
            $phaseTicks -lt 0 -or
            $phaseTicks % [TimeSpan]::TicksPerMillisecond -ne 0 -or
            [long]$phase.elapsedMilliseconds -ne
                [long]($phaseTicks / [TimeSpan]::TicksPerMillisecond)) {
            throw "Acceptance timing phase '$index' is invalid."
        }
        $previousCompleted = $phaseCompleted
    }
}
# END ACCEPTANCE TIMELINE AUTHORITY

$timingDirectory = Join-Path $repositoryRoot 'artifacts\timings'
$timingOutput = Join-Path $timingDirectory (
    'acceptance-' + $Configuration.ToLowerInvariant() + '.json')
$timingStartedUtc = [DateTime]::UtcNow
$timingStopwatch = [Diagnostics.Stopwatch]::StartNew()
$timingPhases = [Collections.Generic.List[object]]::new()
$activeTimingName = $null
$activeTimingStopwatch = $null
$activeTimingStartedMilliseconds = $null
$timingWritten = $false

function Add-AcceptanceTimingPhase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [long]$ElapsedMilliseconds,

        [Parameter(Mandatory = $true)]
        [ValidateSet('passed', 'failed', 'skipped')]
        [string]$Status,

        [long]$StartedMilliseconds = -1,

        [long]$CompletedMilliseconds = -1
    )

    if ($CompletedMilliseconds -lt 0) {
        $CompletedMilliseconds =
            [long]$timingStopwatch.Elapsed.TotalMilliseconds
    }
    if ($StartedMilliseconds -lt 0) {
        $StartedMilliseconds =
            $CompletedMilliseconds - $ElapsedMilliseconds
    }
    if ($ElapsedMilliseconds -lt 0 -or
        $StartedMilliseconds -lt 0 -or
        $CompletedMilliseconds -lt $StartedMilliseconds -or
        $CompletedMilliseconds - $StartedMilliseconds -ne
            $ElapsedMilliseconds) {
        throw "Acceptance timing phase '$Name' has an invalid duration."
    }
    $timingPhases.Add([pscustomobject][ordered]@{
        name = $Name
        startedUtc = $timingStartedUtc.AddMilliseconds(
            $StartedMilliseconds).ToString('o')
        completedUtc = $timingStartedUtc.AddMilliseconds(
            $CompletedMilliseconds).ToString('o')
        elapsedMilliseconds = $ElapsedMilliseconds
        status = $Status
    })
}
function Start-AcceptanceTimingPhase {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ($null -ne $script:activeTimingStopwatch) {
        throw "Acceptance timing phase '$script:activeTimingName' is still active."
    }
    $script:activeTimingName = $Name
    $script:activeTimingStartedMilliseconds =
        [long]$timingStopwatch.Elapsed.TotalMilliseconds
    $script:activeTimingStopwatch = [Diagnostics.Stopwatch]::StartNew()
}

function Complete-AcceptanceTimingPhase {
    param(
        [ValidateSet('passed', 'failed')]
        [string]$Status = 'passed'
    )

    if ($null -eq $script:activeTimingStopwatch) {
        throw 'No acceptance timing phase is active.'
    }
    $script:activeTimingStopwatch.Stop()
    $completedMilliseconds = [long]$timingStopwatch.Elapsed.TotalMilliseconds
    $elapsedMilliseconds =
        $completedMilliseconds - $activeTimingStartedMilliseconds
    Add-AcceptanceTimingPhase `
        -Name $activeTimingName `
        -ElapsedMilliseconds $elapsedMilliseconds `
        -Status $Status `
        -StartedMilliseconds $activeTimingStartedMilliseconds `
        -CompletedMilliseconds $completedMilliseconds
    $script:activeTimingName = $null
    $script:activeTimingStopwatch = $null
    $script:activeTimingStartedMilliseconds = $null
}

function Write-AcceptanceTimingEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('passed', 'failed', 'incomplete')]
        [string]$Status,

        [string]$Failure = ''
    )

    if ($timingWritten) {
        return
    }
    if ($Status -eq 'passed') {
        $expectedPhases = @(
            $contract.automation.acceptanceTimingPhases |
                ForEach-Object { [string]$_ })
        $actualPhases = @($timingPhases | ForEach-Object name)
        if (($actualPhases -join ',') -cne ($expectedPhases -join ',')) {
            throw (
                'Acceptance timing phases do not match the acceptance ' +
                "contract. Expected '$($expectedPhases -join ',')'; " +
                "actual '$($actualPhases -join ',')'.")
        }
    }
    $script:timingWritten = $true
    [IO.Directory]::CreateDirectory($timingDirectory) | Out-Null
    $temporary = $timingOutput + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $totalMilliseconds = [long]$timingStopwatch.Elapsed.TotalMilliseconds
    $timingCompletedUtc = $timingStartedUtc.AddMilliseconds(
        $totalMilliseconds)
    Test-AcceptanceTimingTimeline `
        -StartedUtc $timingStartedUtc `
        -CompletedUtc $timingCompletedUtc `
        -TotalElapsedMilliseconds $totalMilliseconds `
        -Phases @($timingPhases) `
        -ExpectedPhaseNames @($contract.automation.acceptanceTimingPhases) `
        -RequireComplete ($Status -in @('passed','incomplete'))
    [pscustomobject]@{
        schemaVersion = 1
        command = 'acceptance'
        configuration = $Configuration.ToLowerInvariant()
        commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        startedUtc = $timingStartedUtc.ToString('o')
        completedUtc = $timingCompletedUtc.ToString('o')
        status = $Status
        failure = $Failure
        totalElapsedMilliseconds = $totalMilliseconds
        phases = @($timingPhases)
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $temporary -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporary -Destination $timingOutput -Force
}

trap {
    $activePhase = Get-Variable `
        -Name activeTimingStopwatch `
        -Scope Script `
        -ErrorAction SilentlyContinue
    if ($null -ne $activePhase -and $null -ne $activePhase.Value) {
        Complete-AcceptanceTimingPhase -Status failed
    }
    Write-AcceptanceTimingEvidence `
        -Status failed `
        -Failure $_.Exception.Message
    throw $_.Exception.Message
}

Import-Module (Join-Path $repositoryRoot 'scripts\SharpProof.ContainerExecution.psm1') -Force

Start-AcceptanceTimingPhase -Name 'restore'
Invoke-SharpProofRequiredDotnet -Arguments @(
    'restore', 'SharpProof.sln', '--locked-mode') `
    -TimeoutSeconds 300
Complete-AcceptanceTimingPhase

Start-AcceptanceTimingPhase -Name 'static-validation'
. (Join-Path $repositoryRoot 'scripts\Get-SharpProofTcbPaths.ps1')
. (Join-Path $repositoryRoot 'scripts\Resolve-SharpProofContainedPath.ps1')
. (Join-Path $repositoryRoot 'scripts\CSharpSourceMetrics.ps1')
$productionInventoryJson = & (Join-Path $repositoryRoot 'scripts\Get-SharpProofProductionInventory.ps1') -RepositoryRoot $repositoryRoot -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw 'Production inventory authority derivation failed during static validation.'
}
$productionInventory = ($productionInventoryJson -join [Environment]::NewLine) | ConvertFrom-Json

& (Join-Path $repositoryRoot 'scripts\Test-SharpProofContainerContract.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofReadme.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofGeneratedOutputs.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-CompilerArtifactModelGenerator.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofMutationEvidence.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofMutationScheduling.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofMutationBaselines.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofReleaseConfigurationFixtures.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofReleaseAuthorityClosure.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofReleaseAuthorityClosureFixtures.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofPilotAuthorityFixtures.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofContainedPathFixtures.ps1')
$previewEvidence = Get-Content -LiteralPath (
    Join-Path $acceptanceRoot 'preview-evidence.v1.json') -Raw |
    ConvertFrom-Json
$directoryBuildPropsPath = Join-Path `
    $repositoryRoot `
    'Directory.Build.props'
[xml]$directoryBuildProps = Get-Content `
    -LiteralPath $directoryBuildPropsPath `
    -Raw
$packageMetadataPath = Join-Path `
    $repositoryRoot `
    'SharpProof.PackageMetadata.props'
[xml]$packageMetadata = Get-Content `
    -LiteralPath $packageMetadataPath `
    -Raw
$portablePropsPath = Join-Path `
    $repositoryRoot `
    'SharpProof.Package\buildTransitive\SharpProof.props'
[xml]$portableProps = Get-Content -LiteralPath $portablePropsPath -Raw
$consumerContractPath = Join-Path `
    $repositoryRoot `
    'SharpProof.Package\buildTransitive\SharpProof.ConsumerContract.props'
[xml]$consumerContract = Get-Content -LiteralPath $consumerContractPath -Raw
$verifierPropsPath = Join-Path `
    $repositoryRoot `
    'SharpProof.Verifier\buildTransitive\SharpProof.Verifier.props'
[xml]$verifierProps = Get-Content -LiteralPath $verifierPropsPath -Raw
$verifierTargetsPath = Join-Path `
    $repositoryRoot `
    'SharpProof.Verifier\buildTransitive\SharpProof.Verifier.targets'
[xml]$verifierTargets = Get-Content -LiteralPath $verifierTargetsPath -Raw
$packageManifestPath = Join-Path $repositoryRoot 'scripts\package-projects.json'
$packageManifest = Get-Content -LiteralPath $packageManifestPath -Raw |
    ConvertFrom-Json

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        $Actual,

        [Parameter(Mandatory = $true)]
        $Expected,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Actual -ne $Expected) {
        throw "$Name must be '$Expected'; found '$Actual'."
    }
}

function Get-MsBuildProperty {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$Document,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $node = $Document.SelectSingleNode(
        "/Project/PropertyGroup/$Name")
    if ($null -eq $node) {
        throw "Required $Owner property '$Name' is missing."
    }
    return $node.InnerText
}

function Get-MsBuildDefault {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$Document,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $apostrophe = [char]39
    $condition = $apostrophe + '$(' + $Name + ')' + $apostrophe +
        ' == ' + $apostrophe + $apostrophe
    $nodes = @(
        @($Document.SelectNodes(
            "/Project/PropertyGroup/$Name")) |
            Where-Object { $_.GetAttribute('Condition') -eq $condition }
    )
    if ($nodes.Count -ne 1) {
        throw "Required $Owner default '$Name' is missing."
    }
    return $nodes[0].InnerText
}

function Assert-RepositoryPaths {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Paths,

        [Parameter(Mandatory = $true)]
        [string]$Scope
    )

    if ($Paths.Count -eq 0) {
        throw "$Scope must declare at least one path."
    }
    $seenPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $resolvedPaths = [Collections.Generic.List[string]]::new()
    foreach ($untypedRelativePath in $Paths) {
        $relativePath = [string]$untypedRelativePath
        if ([string]::IsNullOrWhiteSpace($relativePath) -or
            -not $seenPaths.Add($relativePath)) {
            throw "$Scope contains a blank or duplicate path: $relativePath"
        }
        try {
            $fullPath = Resolve-SharpProofContainedPath `
                -Root $repositoryRoot -Path $relativePath `
                -ParameterName "$Scope path"
        }
        catch {
            throw "Invalid $Scope path: $relativePath"
        }
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Invalid $Scope path: $relativePath"
        }
        [void]$resolvedPaths.Add($fullPath)
    }
    return @($resolvedPaths)
}

function Measure-RepositoryCSharpSyntax {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Paths,

        [Parameter(Mandatory = $true)]
        [string]$Scope,

        [Parameter()]
        $ProductionInventory
    )

    $resolvedPaths = @(Assert-RepositoryPaths -Paths $Paths -Scope $Scope)
    $compileOwnersByPath = [Collections.Generic.Dictionary[string,
        Collections.Generic.List[object]]]::new([StringComparer]::Ordinal)
    if ($null -ne $ProductionInventory) {
        foreach ($project in @($ProductionInventory.projects)) {
            $projectCompilePaths = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            foreach ($compile in @($project.compile)) {
                $compilePath = [string]$compile.path
                if ([string]::IsNullOrWhiteSpace($compilePath) -or
                    -not $projectCompilePaths.Add($compilePath)) {
                    continue
                }
                $owners = $null
                if (-not $compileOwnersByPath.TryGetValue(
                        $compilePath,
                        [ref]$owners)) {
                    $owners = [Collections.Generic.List[object]]::new()
                    $compileOwnersByPath.Add($compilePath, $owners)
                }
                $owners.Add($project)
            }
        }
    }
    $expressionNodes = 0
    $decisionPoints = 0
    for ($index = 0; $index -lt $Paths.Count; $index++) {
        $relativePath = [string]$Paths[$index]
        if (-not $relativePath.EndsWith(
                '.cs',
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Scope contains a non-C# source path: $relativePath"
        }
        $fullPath = $resolvedPaths[$index]
        $parseOptions = $null
        if ($null -ne $ProductionInventory) {
            $optionMatches = $null
            $optionMatchCount = if ($compileOwnersByPath.TryGetValue(
                    $relativePath,
                    [ref]$optionMatches)) {
                $optionMatches.Count
            }
            else {
                0
            }
            if ($optionMatchCount -ne 1) {
                throw ("$Scope source '$relativePath' is not uniquely owned by the production inventory.")
            }
            $parseOptions = New-SharpProofCSharpParseOptions -LanguageVersion ([string]$optionMatches[0].parseOptions.languageVersion) -PreprocessorSymbols @($optionMatches[0].parseOptions.preprocessorSymbols | ForEach-Object { [string]$_ })
        }
        $metrics = Measure-CSharpSourceText -Source (Get-Content -LiteralPath $fullPath -Raw) -Path $relativePath -ParseOptions $parseOptions
        $expressionNodes += $metrics.expressionNodes
        $decisionPoints += $metrics.decisionPoints
    }
    return [pscustomobject]@{
        expressionNodes = $expressionNodes
        decisionPoints = $decisionPoints
    }
}

foreach ($assertion in @(
        @{ Actual = $contract.schemaVersion; Expected = 4; Name = 'schemaVersion' },
        @{ Actual = $contract.releaseLine; Expected = '1.0.0-preview'; Name = 'releaseLine' },
        @{ Actual = $contract.flagship; Expected = 'effects'; Name = 'flagship' },
        @{ Actual = $contract.analyzer.defaultProfile; Expected = 'advisory'; Name = 'analyzer.defaultProfile' },
        @{ Actual = $contract.analyzer.defaultFeatures; Expected = 'all'; Name = 'analyzer.defaultFeatures' },
        @{ Actual = $contract.analyzer.defaultVerifyPolicy; Expected = 'advisory'; Name = 'analyzer.defaultVerifyPolicy' },
        @{ Actual = $contract.analyzer.defaultAssumptionPolicy; Expected = 'allow'; Name = 'analyzer.defaultAssumptionPolicy' },
        @{ Actual = $contract.analyzer.defaultDiagnosticSeverity; Expected = 'Info'; Name = 'analyzer.defaultDiagnosticSeverity' },
        @{ Actual = $contract.analyzer.diagnosticsEnabledByDefault; Expected = $true; Name = 'analyzer.diagnosticsEnabledByDefault' },
        @{ Actual = $contract.analyzer.unsupportedUnannotatedCallableBehavior; Expected = 'silent'; Name = 'analyzer.unsupportedUnannotatedCallableBehavior' },
        @{ Actual = $contract.analyzer.unsupportedSelectedCallableDiagnostic; Expected = 'SP0047'; Name = 'analyzer.unsupportedSelectedCallableDiagnostic' },
        @{ Actual = $contract.automation.solutionBuildWallSeconds; Expected = 600; Name = 'automation.solutionBuildWallSeconds' },
        @{ Actual = $contract.automation.packageTestCpuPercent; Expected = 75; Name = 'automation.packageTestCpuPercent' },
        @{ Actual = $contract.mutationEvidence.schemaVersion; Expected = 1; Name = 'mutationEvidence.schemaVersion' })) {
    Assert-Equal $assertion.Actual $assertion.Expected $assertion.Name
}
if ([int]$contract.mutationEvidence.expectedCatalogCount -le 0) {
    throw 'mutationEvidence.expectedCatalogCount must be positive.'
}
foreach ($assertion in @(
        @{ Actual = $previewEvidence.schemaVersion; Expected = 1; Name = 'previewEvidence.schemaVersion' },
        @{ Actual = $previewEvidence.requiredHumanApprovals; Expected = 0; Name = 'previewEvidence.requiredHumanApprovals' },
        @{ Actual = @($previewEvidence.requiredEvidence) -join ','; Expected = 'executable-regression,mutation-evidence,soundness-note-when-semantics-change,exact-commit-release-artifacts,debug-solution-gate,release-acceptance-gate'; Name = 'previewEvidence.requiredEvidence' },
        @{ Actual = $contract.supportedTargetFrameworks -join ','; Expected = 'netstandard2.0,net8.0,net472'; Name = 'supportedTargetFrameworks' },
        @{ Actual = $contract.worker.protocolVersion; Expected = 11; Name = 'worker.protocolVersion' },
        @{ Actual = $contract.worker.manifestSchemaVersion; Expected = 4; Name = 'worker.manifestSchemaVersion' },
        @{ Actual = $contract.worker.compilerArtifactSchemaVersion; Expected = 18; Name = 'worker.compilerArtifactSchemaVersion' },
        @{ Actual = $contract.worker.maximumCompilerReferenceModuleBytes; Expected = 268435456; Name = 'worker.maximumCompilerReferenceModuleBytes' },
        @{ Actual = $contract.worker.maximumCompilerReferenceClosureBytes; Expected = 1073741824; Name = 'worker.maximumCompilerReferenceClosureBytes' },
        @{ Actual = $contract.worker.maximumCompilerReferenceModules; Expected = 4096; Name = 'worker.maximumCompilerReferenceModules' },
        @{ Actual = $contract.worker.relationalSummarySchemaVersion; Expected = 2; Name = 'worker.relationalSummarySchemaVersion' },
        @{ Actual = $contract.worker.specificationPackSchemaVersion; Expected = 1; Name = 'worker.specificationPackSchemaVersion' },
        @{ Actual = $contract.worker.maximumParallelism; Expected = 4; Name = 'worker.maximumParallelism' },
        @{ Actual = $contract.worker.maximumExpressionDepth; Expected = 64; Name = 'worker.maximumExpressionDepth' },
        @{ Actual = $contract.worker.queryRlimit; Expected = 3000000; Name = 'worker.queryRlimit' },
        @{ Actual = $contract.worker.methodRlimit; Expected = 20000000; Name = 'worker.methodRlimit' },
        @{ Actual = $contract.worker.maximumMethodWallSeconds; Expected = 10; Name = 'worker.maximumMethodWallSeconds' },
        @{ Actual = $contract.worker.maximumProjectWallSeconds; Expected = 300; Name = 'worker.maximumProjectWallSeconds' },
        @{ Actual = $contract.worker.forcedTerminationMilliseconds; Expected = 1000; Name = 'worker.forcedTerminationMilliseconds' },
        @{ Actual = $contract.cache.schemaVersion; Expected = 13; Name = 'cache.schemaVersion' },
        @{ Actual = $contract.cache.enabledByDefault; Expected = $true; Name = 'cache.enabledByDefault' },
        @{ Actual = $contract.cache.maximumMiB; Expected = 512; Name = 'cache.maximumMiB' },
        @{ Actual = $contract.cache.cacheableOutcomes -join ','; Expected = 'Refuted'; Name = 'cache.cacheableOutcomes' },
        @{ Actual = $packageManifest.schemaVersion; Expected = 1; Name = 'package manifest schemaVersion' })) {
    Assert-Equal $assertion.Actual $assertion.Expected $assertion.Name
}

foreach ($default in @(
        @{ Document = $consumerContract; Property = 'SharpProofProfile'; Owner = 'consumer contract'; Expected = $contract.analyzer.defaultProfile },
        @{ Document = $consumerContract; Property = 'SharpProofFeatures'; Owner = 'consumer contract'; Expected = $contract.analyzer.defaultFeatures },
        @{ Document = $verifierTargets; Property = 'SharpProofVerifyPolicy'; Owner = 'verifier package'; Expected = $contract.analyzer.defaultVerifyPolicy },
        @{ Document = $verifierTargets; Property = 'SharpProofAssumptionPolicy'; Owner = 'verifier package'; Expected = $contract.analyzer.defaultAssumptionPolicy })) {
    Assert-Equal `
        (Get-MsBuildDefault $default.Document $default.Property $default.Owner) `
        $default.Expected `
        $default.Property
}

$msBuildAssertions = @(
    @{ Document = $portableProps; Property = '_SharpProofPortablePackagePresent'; Owner = 'portable package'; Expected = 'true' },
    @{ Document = $verifierProps; Property = '_SharpProofVerifierPackagePresent'; Owner = 'verifier package'; Expected = 'true' },
    @{ Document = $directoryBuildProps; Property = 'Deterministic'; Owner = 'repository build'; Expected = 'true' },
    @{ Document = $directoryBuildProps; Property = 'DebugSymbols'; Owner = 'repository build'; Expected = 'true' },
    @{ Document = $directoryBuildProps; Property = 'DebugType'; Owner = 'repository build'; Expected = 'portable' },
    @{ Document = $directoryBuildProps; Property = 'EmbedUntrackedSources'; Owner = 'repository build'; Expected = 'true' },
    @{ Document = $packageMetadata; Property = 'PublishRepositoryUrl'; Owner = 'package metadata'; Expected = 'true' },
    @{ Document = $packageMetadata; Property = 'IncludeSymbols'; Owner = 'package metadata'; Expected = 'true' },
    @{ Document = $packageMetadata; Property = 'SymbolPackageFormat'; Owner = 'package metadata'; Expected = 'snupkg' },
    @{ Document = $packageMetadata; Property = 'EnablePackageValidation'; Owner = 'package metadata'; Expected = 'true' },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyQueryRlimit'; Owner = 'verifier package'; Expected = [string]$contract.worker.queryRlimit },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyMethodRlimit'; Owner = 'verifier package'; Expected = [string]$contract.worker.methodRlimit },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyMethodWallTimeMilliseconds'; Owner = 'verifier package'; Expected = [string]([int]$contract.worker.maximumMethodWallSeconds * 1000) },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyProjectWallTimeMilliseconds'; Owner = 'verifier package'; Expected = [string]([int]$contract.worker.maximumProjectWallSeconds * 1000) },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyMaxParallelism'; Owner = 'verifier package'; Expected = [string]$contract.worker.maximumParallelism },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyMaximumExpressionDepth'; Owner = 'verifier package'; Expected = [string]$contract.worker.maximumExpressionDepth },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyTerminationGraceMilliseconds'; Owner = 'verifier package'; Expected = [string]$contract.worker.forcedTerminationMilliseconds },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyCacheMaximumBytes'; Owner = 'verifier package'; Expected = [string]([int64]$contract.cache.maximumMiB * 1024 * 1024) },
    @{ Document = $verifierProps; Property = 'SharpProofVerifyCacheEnabled'; Owner = 'verifier package'; Expected = ([string]$contract.cache.enabledByDefault).ToLowerInvariant() })
foreach ($assertion in $msBuildAssertions) {
    Assert-Equal `
        (Get-MsBuildProperty $assertion.Document $assertion.Property $assertion.Owner) `
        $assertion.Expected `
        $assertion.Property
}
$expectedPackageProjects = @(
    'SharpProof.Attributes/SharpProof.Attributes.csproj',
    'SharpProof.Package/SharpProof.Package.csproj',
    'SharpProof.Verifier/SharpProof.Verifier.csproj'
)
Assert-Equal `
    (@($packageManifest.projects) -join '|') `
    ($expectedPackageProjects -join '|') `
    'package manifest projects'
foreach ($packageProject in $expectedPackageProjects) {
    $packageProjectPath = Join-Path $repositoryRoot $packageProject
    if (-not (Test-Path -LiteralPath $packageProjectPath -PathType Leaf)) {
        throw "Required package project is missing: $packageProject"
    }
}
Push-Location $repositoryRoot
try {
    $kernelPaths = @($contract.trustedKernel.paths)
    if ($kernelPaths.Count -eq 0) {
        throw 'The trusted-kernel contract must declare paths.'
    }
    $null = Assert-RepositoryPaths `
        -Paths $kernelPaths `
        -Scope 'trusted-kernel'
    Write-Host "Trusted-kernel paths: $($kernelPaths.Count)"

    # contract.json owns path classification and the acceptance checks below
    # make path additions, removals, and moves explicit.
    $tcbComponents = @($contract.trustedComputingBase.components)
    if ($tcbComponents.Count -eq 0) {
        throw 'The trusted-computing-base contract must declare components.'
    }
    $seenTcbComponents = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($component in $tcbComponents) {
        $name = [string]$component.name
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw 'Every trusted-computing-base component must be named.'
        }
        if (-not $seenTcbComponents.Add($name)) {
            throw "Trusted-computing-base component '$name' is declared twice."
        }
    }
    $canonicalTcbPaths = @(Get-SharpProofTcbPaths -Contract $contract -ProductionInventory $productionInventory)
    foreach ($component in $tcbComponents) {
        $name = [string]$component.name
        $paths = @($component.paths)
        $null = Assert-RepositoryPaths -Paths $paths -Scope "trusted-computing-base component '$name'"
        Write-Host "Trusted-computing-base $name paths: $($paths.Count)"
    }
    Write-Host "Trusted-computing-base union paths: $($canonicalTcbPaths.Count)"

    $coordinatorComplexity = $contract.productionCoordinatorComplexity
    $layers = @($coordinatorComplexity.layers)
    if ($layers.Count -eq 0) {
        throw 'The production-coordinator complexity contract is invalid.'
    }
    $layerNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $layerPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($layer in $layers) {
        $name = [string]$layer.name
        $path = [string]$layer.path
        if ([string]::IsNullOrWhiteSpace($name) -or
            -not $layerNames.Add($name) -or
            -not $layerPaths.Add($path)) {
            throw "Invalid or duplicate production coordinator: '$name'."
        }
        $maximumExpressionNodes = [int]$layer.maximumExpressionNodes
        $maximumDecisionPoints = [int]$layer.maximumDecisionPoints
        if ($maximumExpressionNodes -le 0 -or
            $maximumDecisionPoints -le 0) {
            throw "Production coordinator '$name' must have positive limits."
        }
        $currentMetrics = Measure-RepositoryCSharpSyntax -Paths @($path) -Scope "production coordinator '$name'" -ProductionInventory $productionInventory
        $currentExpressionNodes = [int]$currentMetrics.expressionNodes
        $currentDecisionPoints = [int]$currentMetrics.decisionPoints
        if ($currentExpressionNodes -gt $maximumExpressionNodes -or
            $currentDecisionPoints -gt $maximumDecisionPoints) {
            throw "Production layer '$name' has $currentExpressionNodes " +
                "expression nodes; " +
                "$currentDecisionPoints decision points. Limits are " +
                "$maximumExpressionNodes and $maximumDecisionPoints."
        }
        Write-Host (
            "Production-layer $name expression nodes: " +
            "$currentExpressionNodes (maximum $maximumExpressionNodes); " +
            "decision points: " +
            "$currentDecisionPoints (maximum $maximumDecisionPoints)")
    }

    $complexityArguments = @{}
    if ($Configuration -ceq 'Release') {
        $complexityArguments.ProductionInventory = $productionInventory
    }
    & (Join-Path $repositoryRoot 'scripts\Test-ProductionCSharpComplexity.ps1') `
        @complexityArguments

    Complete-AcceptanceTimingPhase

    if (-not $SkipBuild) {
        Start-AcceptanceTimingPhase -Name 'build'
        Invoke-SharpProofRequiredDotnet `
            -Arguments @('build', 'SharpProof.sln', '-c', $Configuration, '--no-restore') `
            -TimeoutSeconds ([int]$contract.automation.solutionBuildWallSeconds)
        Complete-AcceptanceTimingPhase
    }
    else {
        Add-AcceptanceTimingPhase `
            -Name 'build' -ElapsedMilliseconds 0 -Status skipped
    }

    if (-not $SkipTests) {
        Start-AcceptanceTimingPhase -Name 'semantic-tests'
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofSemanticTests.ps1') `
            -Configuration $Configuration `
            -NoBuild `
            -TimeoutSeconds ([int]$contract.automation.solutionTestWallSeconds)
        Complete-AcceptanceTimingPhase

        Start-AcceptanceTimingPhase -Name 'package-tests'
        $packageTestArguments = @{
            Configuration = $Configuration
            TimeoutSeconds = [int]$contract.automation.solutionTestWallSeconds
        }
        if ($Configuration -eq 'Release') {
            $packageTestArguments.NoBuild = $true
        }
        else {
            $packageTestArguments.ReuseTestHarness = $true
        }
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofPackageTests.ps1') `
            @packageTestArguments
        Complete-AcceptanceTimingPhase
    }
    else {
        Add-AcceptanceTimingPhase `
            -Name 'semantic-tests' -ElapsedMilliseconds 0 -Status skipped
        Add-AcceptanceTimingPhase `
            -Name 'package-tests' -ElapsedMilliseconds 0 -Status skipped
    }

    if (-not $SkipTests) {
        Start-AcceptanceTimingPhase -Name 'fuzz'
        Invoke-SharpProofRequiredDotnet -Arguments @(
            'run',
            '--project',
            'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj',
            '-c',
            $Configuration,
            '--no-build',
            '--',
            '--cases',
            [string]$pullRequestCases,
            '--seed',
            '23063',
            '--max-parallelism',
            [string]$contract.fuzz.maximumParallelism
        ) `
            -TimeoutSeconds ([int]$contract.automation.solutionTestWallSeconds)
        Complete-AcceptanceTimingPhase

        Start-AcceptanceTimingPhase -Name 'corpus-and-performance'
        Invoke-SharpProofRequiredDotnet -Arguments @(
            'run',
            '--project',
            'SharpProof.Gates\SharpProof.Gates.csproj',
            '-c',
            $Configuration,
            '--no-build',
            '--',
            'all'
        ) `
            -TimeoutSeconds ([int]$contract.automation.solutionTestWallSeconds)
        Complete-AcceptanceTimingPhase
    }
    else {
        Add-AcceptanceTimingPhase `
            -Name 'fuzz' -ElapsedMilliseconds 0 -Status skipped
        Add-AcceptanceTimingPhase `
            -Name 'corpus-and-performance' `
            -ElapsedMilliseconds 0 `
            -Status skipped
    }
}
finally {
    Pop-Location
}

$acceptanceStatus = if ($SkipBuild -or $SkipTests) {
    'incomplete'
}
else {
    'passed'
}
Write-AcceptanceTimingEvidence -Status $acceptanceStatus
Write-Host "Acceptance timing evidence: $timingOutput"
if ($acceptanceStatus -ceq 'passed') {
    Write-Host 'SharpProof acceptance checks passed.'
}
else {
    Write-Host (
        'SharpProof acceptance checks completed in non-qualifying ' +
        'partial mode.')
}
