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
$wrapperPath = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json

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
        [string]$Status
    )

    $completedMilliseconds = [long]$timingStopwatch.Elapsed.TotalMilliseconds
    $startedMilliseconds = $completedMilliseconds - $ElapsedMilliseconds
    if ($ElapsedMilliseconds -lt 0 -or $startedMilliseconds -lt 0) {
        throw "Acceptance timing phase '$Name' has an invalid duration."
    }
    $timingPhases.Add([pscustomobject][ordered]@{
        name = $Name
        startedUtc = $timingStartedUtc.AddMilliseconds(
            $startedMilliseconds).ToString('o')
        completedUtc = $timingStartedUtc.AddMilliseconds(
            $completedMilliseconds).ToString('o')
        elapsedMilliseconds = $ElapsedMilliseconds
        status = $Status
    })
}

function Start-AcceptanceTimingPhase {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ($null -ne $activeTimingStopwatch) {
        throw "Acceptance timing phase '$activeTimingName' is still active."
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

    if ($null -eq $activeTimingStopwatch) {
        throw 'No acceptance timing phase is active.'
    }
    $activeTimingStopwatch.Stop()
    $completedMilliseconds = [long]$timingStopwatch.Elapsed.TotalMilliseconds
    $timingPhases.Add([pscustomobject][ordered]@{
        name = $activeTimingName
        startedUtc = $timingStartedUtc.AddMilliseconds(
            $activeTimingStartedMilliseconds).ToString('o')
        completedUtc = $timingStartedUtc.AddMilliseconds(
            $completedMilliseconds).ToString('o')
        elapsedMilliseconds =
            $completedMilliseconds - $activeTimingStartedMilliseconds
        status = $Status
    })
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
        configuration = $Configuration
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
    if ($null -ne $activeTimingStopwatch) {
        Complete-AcceptanceTimingPhase -Status failed
    }
    Write-AcceptanceTimingEvidence `
        -Status failed `
        -Failure $_.Exception.Message
    throw
}

Start-AcceptanceTimingPhase -Name 'restore'
Invoke-SharpProofDotnet -Arguments @(
    'restore', 'SharpProof.sln', '--locked-mode')
Complete-AcceptanceTimingPhase

Start-AcceptanceTimingPhase -Name 'static-validation'
. (Join-Path $repositoryRoot 'scripts\Get-SharpProofTcbPaths.ps1')
. (Join-Path $repositoryRoot 'scripts\Resolve-SharpProofContainedPath.ps1')
. (Join-Path $repositoryRoot 'scripts\CSharpSourceMetrics.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofContainerContract.ps1')
    & (Join-Path $repositoryRoot 'scripts\Generate-DiagnosticDescriptors.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-CSharpScalarSemantics.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-ContractApiCatalog.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-AnalyzerDiagnosticCatalog.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-ProjectionCatalog.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-LauncherArguments.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-BoundContractModel.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-EffectContractMappings.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-OperationSupportCatalog.ps1') -Verify
    & (Join-Path $repositoryRoot 'scripts\Generate-IrModel.ps1') -Verify
& (Join-Path $repositoryRoot 'scripts\Generate-ApiSpecCatalog.ps1') -Verify
& (Join-Path $repositoryRoot 'scripts\Generate-ProtocolModel.ps1') -Verify
& (Join-Path $repositoryRoot 'scripts\Generate-CompilerArtifactModel.ps1') -Verify
& (Join-Path $repositoryRoot 'scripts\Test-CompilerArtifactModelGenerator.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofMutationEvidence.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofMutationScheduling.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofMutationBaselines.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofReleaseConfigurationFixtures.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofReleaseAuthorityClosure.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofReleaseAuthorityClosureFixtures.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofPilotAuthorityFixtures.ps1')
& (Join-Path $repositoryRoot 'scripts\Test-SharpProofContainedPathFixtures.ps1')
& (Join-Path $repositoryRoot 'scripts\Generate-DeclarativeModels.ps1') -Verify
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
$portableTargetsPath = Join-Path `
    $repositoryRoot `
    'SharpProof.Package\buildTransitive\SharpProof.targets'
[xml]$portableTargets = Get-Content -LiteralPath $portableTargetsPath -Raw
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

function Invoke-SharpProofDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [int]$TimeoutSeconds = 300
    )

    & $wrapperPath `
        -TimeoutSeconds $TimeoutSeconds `
        @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
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
    }
}

function Measure-RepositoryCSharpSyntax {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Paths,

        [Parameter(Mandatory = $true)]
        [string]$Scope
    )

    Assert-RepositoryPaths -Paths $Paths -Scope $Scope
    $syntaxTokens = 0
    $syntaxNodes = 0
    $expressionNodes = 0
    $decisionPoints = 0
    $members = 0
    foreach ($untypedRelativePath in $Paths) {
        $relativePath = [string]$untypedRelativePath
        if (-not $relativePath.EndsWith(
                '.cs',
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Scope contains a non-C# source path: $relativePath"
        }
        $fullPath = Join-Path $repositoryRoot $relativePath
        $metrics = Measure-CSharpSourceText `
            -Source (Get-Content -LiteralPath $fullPath -Raw) `
            -Path $relativePath
        $syntaxTokens += $metrics.syntaxTokens
        $syntaxNodes += $metrics.syntaxNodes
        $expressionNodes += $metrics.expressionNodes
        $decisionPoints += $metrics.decisionPoints
        $members += $metrics.members
    }
    return [pscustomobject]@{
        syntaxTokens = $syntaxTokens
        syntaxNodes = $syntaxNodes
        expressionNodes = $expressionNodes
        decisionPoints = $decisionPoints
        members = $members
    }
}

Assert-Equal $contract.schemaVersion 4 'schemaVersion'
Assert-Equal $contract.releaseLine '1.0.0-preview' 'releaseLine'
Assert-Equal $contract.flagship 'effects' 'flagship'
Assert-Equal $contract.analyzer.defaultProfile 'advisory' 'analyzer.defaultProfile'
Assert-Equal $contract.analyzer.defaultFeatures 'all' 'analyzer.defaultFeatures'
Assert-Equal $contract.analyzer.defaultVerifyPolicy 'advisory' 'analyzer.defaultVerifyPolicy'
Assert-Equal $contract.analyzer.defaultAssumptionPolicy 'allow' 'analyzer.defaultAssumptionPolicy'
Assert-Equal $contract.analyzer.defaultDiagnosticSeverity 'Info' 'analyzer.defaultDiagnosticSeverity'
Assert-Equal $contract.analyzer.diagnosticsEnabledByDefault $true 'analyzer.diagnosticsEnabledByDefault'
Assert-Equal $contract.analyzer.unsupportedUnannotatedCallableBehavior 'silent' 'analyzer.unsupportedUnannotatedCallableBehavior'
Assert-Equal $contract.analyzer.unsupportedSelectedCallableDiagnostic 'SP0047' 'analyzer.unsupportedSelectedCallableDiagnostic'
Assert-Equal $contract.automation.solutionBuildWallSeconds 600 'automation.solutionBuildWallSeconds'
Assert-Equal $contract.mutationEvidence.schemaVersion 1 'mutationEvidence.schemaVersion'
if ([int]$contract.mutationEvidence.expectedCatalogCount -le 0) {
    throw 'mutationEvidence.expectedCatalogCount must be positive.'
}
if ([string]$contract.mutationEvidence.expectedCatalogSha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'mutationEvidence.expectedCatalogSha256 must be a lowercase SHA-256 digest.'
}
Assert-Equal $previewEvidence.schemaVersion 1 'previewEvidence.schemaVersion'
Assert-Equal `
    $previewEvidence.requiredHumanApprovals `
    0 `
    'previewEvidence.requiredHumanApprovals'
Assert-Equal `
    (@($previewEvidence.requiredEvidence) -join ',') `
    'executable-regression,mutation-evidence,soundness-note-when-semantics-change,exact-commit-release-artifacts,debug-solution-gate,release-acceptance-gate' `
    'previewEvidence.requiredEvidence'
Assert-Equal `
    (Get-MsBuildDefault $portableTargets 'SharpProofProfile' 'portable package') `
    $contract.analyzer.defaultProfile `
    'SharpProofProfile'
Assert-Equal `
    (Get-MsBuildDefault $portableTargets 'SharpProofFeatures' 'portable package') `
    $contract.analyzer.defaultFeatures `
    'SharpProofFeatures'
Assert-Equal `
    (Get-MsBuildDefault $verifierTargets 'SharpProofVerifyPolicy' 'verifier package') `
    $contract.analyzer.defaultVerifyPolicy `
    'SharpProofVerifyPolicy'
Assert-Equal `
    (Get-MsBuildDefault $verifierTargets 'SharpProofAssumptionPolicy' 'verifier package') `
    $contract.analyzer.defaultAssumptionPolicy `
    'SharpProofAssumptionPolicy'
Assert-Equal ($contract.supportedTargetFrameworks -join ',') 'netstandard2.0,net8.0,net472' 'supportedTargetFrameworks'
Assert-Equal $contract.worker.protocolVersion 11 'worker.protocolVersion'
Assert-Equal $contract.worker.manifestSchemaVersion 4 'worker.manifestSchemaVersion'
Assert-Equal $contract.worker.compilerArtifactSchemaVersion 12 'worker.compilerArtifactSchemaVersion'
Assert-Equal $contract.worker.maximumCompilerReferenceModuleBytes 268435456 'worker.maximumCompilerReferenceModuleBytes'
Assert-Equal $contract.worker.maximumCompilerReferenceClosureBytes 1073741824 'worker.maximumCompilerReferenceClosureBytes'
Assert-Equal $contract.worker.maximumCompilerReferenceModules 4096 'worker.maximumCompilerReferenceModules'
Assert-Equal $contract.worker.relationalSummarySchemaVersion 1 'worker.relationalSummarySchemaVersion'
Assert-Equal $contract.worker.specificationPackSchemaVersion 1 'worker.specificationPackSchemaVersion'
Assert-Equal $contract.worker.maximumParallelism 4 'worker.maximumParallelism'
Assert-Equal $contract.worker.maximumExpressionDepth 64 'worker.maximumExpressionDepth'
Assert-Equal $contract.worker.queryRlimit 3000000 'worker.queryRlimit'
Assert-Equal $contract.worker.methodRlimit 20000000 'worker.methodRlimit'
Assert-Equal $contract.worker.maximumMethodWallSeconds 10 'worker.maximumMethodWallSeconds'
Assert-Equal $contract.worker.maximumProjectWallSeconds 300 'worker.maximumProjectWallSeconds'
Assert-Equal $contract.worker.forcedTerminationMilliseconds 1000 'worker.forcedTerminationMilliseconds'
Assert-Equal $contract.cache.schemaVersion 13 'cache.schemaVersion'
Assert-Equal $contract.cache.enabledByDefault $true 'cache.enabledByDefault'
Assert-Equal $contract.cache.maximumMiB 512 'cache.maximumMiB'
Assert-Equal ($contract.cache.cacheableOutcomes -join ',') 'Refuted' 'cache.cacheableOutcomes'
Assert-Equal `
    (Get-MsBuildProperty $portableProps '_SharpProofPortablePackagePresent' 'portable package') `
    'true' `
    '_SharpProofPortablePackagePresent'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps '_SharpProofVerifierPackagePresent' 'verifier package') `
    'true' `
    '_SharpProofVerifierPackagePresent'
Assert-Equal $packageManifest.schemaVersion 1 'package manifest schemaVersion'
Assert-Equal `
    (Get-MsBuildProperty $directoryBuildProps 'Deterministic' 'repository build') `
    'true' `
    'Deterministic'
Assert-Equal `
    (Get-MsBuildProperty $directoryBuildProps 'DebugSymbols' 'repository build') `
    'true' `
    'DebugSymbols'
Assert-Equal `
    (Get-MsBuildProperty $directoryBuildProps 'DebugType' 'repository build') `
    'portable' `
    'DebugType'
Assert-Equal `
    (Get-MsBuildProperty $directoryBuildProps 'EmbedUntrackedSources' 'repository build') `
    'true' `
    'EmbedUntrackedSources'
Assert-Equal `
    (Get-MsBuildProperty $packageMetadata 'PublishRepositoryUrl' 'package metadata') `
    'true' `
    'PublishRepositoryUrl'
Assert-Equal `
    (Get-MsBuildProperty $packageMetadata 'IncludeSymbols' 'package metadata') `
    'true' `
    'IncludeSymbols'
Assert-Equal `
    (Get-MsBuildProperty $packageMetadata 'SymbolPackageFormat' 'package metadata') `
    'snupkg' `
    'SymbolPackageFormat'
Assert-Equal `
    (Get-MsBuildProperty $packageMetadata 'EnablePackageValidation' 'package metadata') `
    'true' `
    'EnablePackageValidation'
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
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyQueryRlimit' 'verifier package') `
    ([string]$contract.worker.queryRlimit) `
    'SharpProofVerifyQueryRlimit'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyMethodRlimit' 'verifier package') `
    ([string]$contract.worker.methodRlimit) `
    'SharpProofVerifyMethodRlimit'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyMethodWallTimeMilliseconds' 'verifier package') `
    ([string]([int]$contract.worker.maximumMethodWallSeconds * 1000)) `
    'SharpProofVerifyMethodWallTimeMilliseconds'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyProjectWallTimeMilliseconds' 'verifier package') `
    ([string]([int]$contract.worker.maximumProjectWallSeconds * 1000)) `
    'SharpProofVerifyProjectWallTimeMilliseconds'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyMaxParallelism' 'verifier package') `
    ([string]$contract.worker.maximumParallelism) `
    'SharpProofVerifyMaxParallelism'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyMaximumExpressionDepth' 'verifier package') `
    ([string]$contract.worker.maximumExpressionDepth) `
    'SharpProofVerifyMaximumExpressionDepth'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyTerminationGraceMilliseconds' 'verifier package') `
    ([string]$contract.worker.forcedTerminationMilliseconds) `
    'SharpProofVerifyTerminationGraceMilliseconds'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyCacheMaximumBytes' 'verifier package') `
    ([string]([int64]$contract.cache.maximumMiB * 1024 * 1024)) `
    'SharpProofVerifyCacheMaximumBytes'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyCacheEnabled' 'verifier package') `
    ([string]$contract.cache.enabledByDefault).ToLowerInvariant() `
    'SharpProofVerifyCacheEnabled'
Push-Location $repositoryRoot
try {
    $kernelPaths = @($contract.trustedKernel.paths)
    if ($kernelPaths.Count -eq 0) {
        throw 'The trusted-kernel contract must declare paths.'
    }
    Assert-RepositoryPaths `
        -Paths $kernelPaths `
        -Scope 'trusted-kernel'
    Write-Host "Trusted-kernel paths: $($kernelPaths.Count)"

    # contract.json owns path classification. A separately reviewed digest
    # inside the contract makes path additions, removals, and moves explicit.
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
    $canonicalTcbPaths = @(Get-SharpProofTcbPaths -Contract $contract)
    $sortedTcbPaths = [string[]]@($canonicalTcbPaths)
    [Array]::Sort($sortedTcbPaths, [StringComparer]::Ordinal)
    $inventoryText = ($sortedTcbPaths -join "`n") + "`n"
    $inventoryBytes = [Text.Encoding]::UTF8.GetBytes($inventoryText)
    $inventoryDigest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($inventoryBytes)
    ).ToLowerInvariant()
    $expectedInventoryDigest =
        [string]$contract.trustedComputingBase.inventorySha256
    if ($inventoryDigest -cne $expectedInventoryDigest) {
        throw "The trusted-computing-base inventory digest changed. " +
            "Review path ownership and update the intentional digest pin."
    }
    foreach ($component in $tcbComponents) {
        $name = [string]$component.name
        $paths = @($component.paths)
        Assert-RepositoryPaths `
            -Paths $paths `
            -Scope "trusted-computing-base component '$name'"
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
        $currentMetrics = Measure-RepositoryCSharpSyntax `
            -Paths @($path) `
            -Scope "production coordinator '$name'"
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

    & (Join-Path $repositoryRoot 'scripts\Test-ProductionCSharpComplexity.ps1')

    Complete-AcceptanceTimingPhase

    if (-not $SkipBuild) {
        Start-AcceptanceTimingPhase -Name 'build'
        Invoke-SharpProofDotnet `
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
        Invoke-SharpProofDotnet -Arguments @(
            'run',
            '--project',
            'Tools\SharpProof.Fuzz\SharpProof.Fuzz.csproj',
            '-c',
            $Configuration,
            '--no-build',
            '--',
            '--cases',
            [string]$contract.fuzz.pullRequestCases,
            '--seed',
            '23063',
            '--max-parallelism',
            [string]$contract.fuzz.maximumParallelism
        )
        Complete-AcceptanceTimingPhase

        Start-AcceptanceTimingPhase -Name 'corpus-and-performance'
        Invoke-SharpProofDotnet -Arguments @(
            'run',
            '--project',
            'SharpProof.Gates\SharpProof.Gates.csproj',
            '-c',
            $Configuration,
            '--no-build',
            '--',
            'all'
        )
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
