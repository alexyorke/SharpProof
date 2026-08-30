[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('contract', 'restore', 'build', 'self-apply', 'check', 'pr-gates', 'test', 'test-changed', 'semantic-tests', 'portable-tests', 'worker-tests', 'package-tests', 'package-consumers', 'samples', 'corpus', 'corpus-update', 'performance', 'performance-smoke', 'gates', 'coverage', 'mutation', 'fuzz-nightly', 'dependency-audit', 'acceptance', 'pack', 'pilots', 'pilot-review', 'release-tag', 'release-baseline', 'release-plan', 'release-qualification', 'release-publish')]
    [string]$Command,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$Target = 'SharpProof.sln',

    [string]$PackageSource = '',

    [string]$TestFilter = '',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repositoryRoot

Import-Module (Join-Path `
    $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force

if (-not $IsLinux -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    throw 'SharpProof container commands require Linux x64.'
}
if ($env:SHARPPROOF_CONTAINER -cne '1' -or
    -not (Test-Path -LiteralPath '/etc/sharpproof/container-contract.json' -PathType Leaf)) {
    throw 'SharpProof container commands require the canonical container contract.'
}

if ($NoBuild -and $Command -notin @(
        'test', 'test-changed', 'semantic-tests', 'portable-tests',
        'worker-tests', 'package-tests')) {
    throw (
        '-NoBuild is supported only for test commands that can reuse an ' +
        'existing build in the current container workspace.')
}

function Invoke-DotNet([string[]]$Arguments) {
    $effectiveArguments = @(
        Add-SharpProofStaticGraphArgument -Arguments $Arguments
    )
    & dotnet @effectiveArguments
    if ($LASTEXITCODE -ne 0) {
        throw (
            "dotnet $($effectiveArguments -join ' ') failed with exit " +
            "code $LASTEXITCODE.")
    }
}

$testProjectParallelism = Get-SharpProofTestProjectParallelism `
    -RepositoryRoot $repositoryRoot

switch ($Command) {
    'contract' {
        & (Join-Path $repositoryRoot `
            'scripts/Test-SharpProofContainerContract.ps1')
    }
    'restore' {
        Invoke-DotNet @('restore', $Target, '--locked-mode')
    }
    'build' {
        Invoke-DotNet @('restore', $Target, '--locked-mode')
        Invoke-DotNet @('build', $Target, '--configuration', $Configuration, '--no-restore')
    }
    'self-apply' {
        $trackedProjects = @(
            & git -C $repositoryRoot ls-files -- '*.csproj' |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object {
                    Join-Path $repositoryRoot $_
                } |
                Sort-Object
        )
        if ($LASTEXITCODE -ne 0 -or $trackedProjects.Count -eq 0) {
            throw 'self-apply requires a Git-backed repository with tracked project files.'
        }

        $sourceProjects = @(
            $trackedProjects |
                Where-Object {
                    $relative = [IO.Path]::GetRelativePath(
                        $repositoryRoot, $_).Replace('\', '/')
                    $relative -notlike 'samples/*' -and
                        $relative -notlike 'eng/pilots/*'
                }
        )
        if ($sourceProjects.Count -eq 0) {
            throw 'self-apply found no source-tree projects to analyze.'
        }

        # Build the complete source tree once with the self lane disabled so
        # every analyzer and generator output is available as a stable input.
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', $Configuration,
            '--no-restore', '--nologo',
            '-p:SharpProofSelfApplication=false',
            '-p:SharpProofProfile=off',
            '-p:SharpProofVerify=false',
            '-p:GeneratePackageOnBuild=false')

        $ordinal = 0
        foreach ($project in $sourceProjects) {
            $ordinal++
            Write-Host ("Self-applying SharpProof ({0}/{1}): {2}" -f
                $ordinal, $sourceProjects.Count,
                [IO.Path]::GetRelativePath($repositoryRoot, $project))
            Invoke-DotNet @(
                'build', $project, '--configuration', $Configuration,
                '--no-restore', '--no-dependencies', '--nologo',
                '-p:SharpProofSelfApplication=true',
                '-p:SharpProofProfile=advisory',
                '-p:SharpProofFeatures=all',
                '-p:SharpProofVerify=false',
                '-p:GeneratePackageOnBuild=false')
        }

        if (-not [string]::IsNullOrWhiteSpace($PackageSource)) {
            # The source self-application builds can leave Roslyn's shared
            # compiler server holding source-built analyzer load contexts.
            # Stop it before package pilots so the package lane observes only
            # the candidate analyzer payload.
            Invoke-DotNet @('build-server', 'shutdown')
            $resolvedPackageSource = if ([IO.Path]::IsPathRooted($PackageSource)) {
                [IO.Path]::GetFullPath($PackageSource)
            }
            else {
                [IO.Path]::GetFullPath(
                    (Join-Path $repositoryRoot $PackageSource))
            }
            if (-not (Test-Path -LiteralPath $resolvedPackageSource -PathType Container)) {
                throw "self-apply package source is missing: '$resolvedPackageSource'."
            }
            & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPilots.ps1') `
                -PackageSource $resolvedPackageSource
            if ($LASTEXITCODE -ne 0) {
                throw 'SharpProof self-application pilot validation failed.'
            }
        }

        # Package-backed samples exercise the same analyzer payload through
        # the supported package-consumer path.  The sample harness creates and
        # cleans its own isolated local feed and temporary build roots.  Keep
        # this after pilots because its pack restores may update lock files in
        # the disposable checkout, which would violate the pilot clean guard.
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofSamples.ps1') `
            -Configuration $Configuration `
            -ExpectedSmt Required
        if ($LASTEXITCODE -ne 0) {
            throw 'SharpProof self-application sample validation failed.'
        }
    }
    'check' {
        & (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofDevCheck.ps1') `
            -Configuration $Configuration
    }
    'pr-gates' {
        if ($Configuration -ne 'Release') {
            throw "pr-gates requires -Configuration Release. " +
                "Use the test command for Debug validation."
        }
        & (Join-Path $repositoryRoot `
            'scripts/Test-SharpProofContainerContract.ps1')
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', $Configuration,
            '--no-restore')

        $performanceOutput = Join-Path $repositoryRoot (
            'artifacts/ci/performance.json')
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofGateEvidence.ps1') `
            -Gate performance `
            -OutputPath $performanceOutput
        if ($LASTEXITCODE -ne 0) {
            throw 'PR performance validation failed.'
        }

        Invoke-DotNet @(
            'test',
            'SharpProof.Gates.Test/SharpProof.Gates.Test.csproj',
            '--configuration', $Configuration,
            '--no-build', '--no-restore',
            '--filter',
            'FullyQualifiedName~ForcedTerminationDeadlineIsStableAcrossLaunches')
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofSemanticTests.ps1') `
            -Configuration $Configuration `
            -NoBuild `
            -TestFilter (
                'TestCategory!=Performance&TestCategory!=Coverage&' +
                'TestCategory!=Corpus')
        if ($LASTEXITCODE -ne 0) {
            throw 'PR semantic validation failed.'
        }

        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofPackageTests.ps1') `
            -Configuration $Configuration `
            -NoBuild `
            -TestFilter (
                'TestCategory!=Performance&TestCategory!=Coverage&' +
                'TestCategory!=Corpus')
        if ($LASTEXITCODE -ne 0) {
            throw 'PR package validation failed.'
        }
    }
    'test' {
        if ($NoBuild -and
            $Target.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) {
            $assembly = Get-SharpProofTestAssemblyPath `
                -ProjectPath $Target `
                -Configuration $Configuration
            $arguments = @('vstest', $assembly)
            if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
                $arguments += '/TestCaseFilter:' + $TestFilter
            }
            Invoke-DotNet $arguments
            break
        }
        if (-not $NoBuild) {
            Invoke-DotNet @('restore', $Target, '--locked-mode')
        }
        $arguments = @(
            'test', $Target, '--configuration', $Configuration, '--no-restore')
        if ($NoBuild) {
            $arguments += '--no-build'
        }
        if ($Target.EndsWith('.sln', [StringComparison]::OrdinalIgnoreCase) -or
            $Target.EndsWith('.slnf', [StringComparison]::OrdinalIgnoreCase)) {
            $arguments += "/m:$testProjectParallelism"
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
    }
    'test-changed' {
        $changedArguments = @{
            Configuration = $Configuration
        }
        if ($NoBuild) {
            $changedArguments.NoBuild = $true
        }
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofChangedTests.ps1') `
            @changedArguments
    }
    'semantic-tests' {
        $semanticArguments = @{
            Configuration = $Configuration
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $semanticArguments.TestFilter = $TestFilter
        }
        if ($NoBuild) {
            $semanticArguments.NoBuild = $true
        }
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofSemanticTests.ps1') `
            @semanticArguments
    }
    'portable-tests' {
        $target = 'SharpProof.Portable.Tests.slnf'
        if (-not $NoBuild) {
            Invoke-DotNet @('restore', $target, '--locked-mode')
        }
        $arguments = @(
            'test', $target, '--configuration', $Configuration,
            '--no-restore', "/m:$testProjectParallelism")
        if ($NoBuild) {
            $arguments += '--no-build'
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
    }
    'worker-tests' {
        if (-not $NoBuild) {
            Invoke-DotNet @(
                'restore',
                'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj',
                '--locked-mode')
        }
        if ($NoBuild) {
            $assembly = Get-SharpProofTestAssemblyPath `
                -ProjectPath 'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj' `
                -Configuration $Configuration
            $arguments = @('vstest', $assembly)
            if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
                $arguments += '/TestCaseFilter:' + $TestFilter
            }
            Invoke-DotNet $arguments
            break
        }
        $arguments = @(
            'test',
            'SharpProof.Worker.Test/SharpProof.Worker.Test.csproj',
            '--configuration', $Configuration, '--no-restore')
        if ($NoBuild) {
            $arguments += '--no-build'
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $arguments += @('--filter', $TestFilter)
        }
        Invoke-DotNet $arguments
    }
    'package-tests' {
        $packageArguments = @{
            Configuration = $Configuration
            TestFilter = $TestFilter
            PackageSource = $PackageSource
        }
        if ($NoBuild) {
            $packageArguments.NoBuild = $true
        }
        & (Join-Path `
            $repositoryRoot 'scripts/Invoke-SharpProofPackageTests.ps1') `
            @packageArguments
        if ($LASTEXITCODE -ne 0) { throw 'Package tests failed.' }
    }
    'package-consumers' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw 'package-consumers requires -PackageSource.'
        }
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPackageConsumers.ps1') `
            -Configuration $Configuration `
            -ExpectedSmt Required `
            -PackageSource $PackageSource
        if ($LASTEXITCODE -ne 0) { throw 'Package consumer validation failed.' }
        $toolchain = Get-Content -LiteralPath (Join-Path `
            $repositoryRoot 'eng/container/toolchain.json') -Raw |
            ConvertFrom-Json
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPackageConsumers.ps1') `
            -Configuration $Configuration `
            -ExpectedSmt Required `
            -PackageSource $PackageSource `
            -ConsumerSdkVersion ([string]$toolchain.dotnet.minimumSdkVersion) `
            -FrameworkConsumersOnly
        if ($LASTEXITCODE -ne 0) {
            throw 'Minimum-SDK package consumer validation failed.'
        }
        $consumerEvidence = Join-Path `
            $repositoryRoot `
            'artifacts/release-qualification/package-consumers.json'
        [IO.Directory]::CreateDirectory(
            [IO.Path]::GetDirectoryName($consumerEvidence)) | Out-Null
        [IO.File]::WriteAllText(
            $consumerEvidence,
            (([ordered]@{
                schemaVersion = 1
                status = 'passed'
                commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
                packageSource = [IO.Path]::GetRelativePath(
                    $repositoryRoot,
                    [IO.Path]::GetFullPath($PackageSource)).Replace('\', '/')
                packageArtifacts = @(
                    Get-ChildItem -LiteralPath $PackageSource -File |
                        Where-Object {
                            $_.Extension -in @('.nupkg', '.snupkg')
                        } |
                        Sort-Object Name |
                        ForEach-Object {
                            [ordered]@{
                                fileName = $_.Name
                                bytes = [int64]$_.Length
                                sha256 = (Get-FileHash `
                                    -LiteralPath $_.FullName `
                                    -Algorithm SHA256).Hash.ToLowerInvariant()
                            }
                        }
                )
            } | ConvertTo-Json) + "`n"),
            [Text.UTF8Encoding]::new($false))
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate package-consumers `
            -EvidencePath $consumerEvidence
    }
    'samples' {
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofSamples.ps1') `
            -Configuration $Configuration `
            -ExpectedSmt Required `
            -PackageSource $PackageSource
        if ($LASTEXITCODE -ne 0) { throw 'Sample validation failed.' }
    }
    { $_ -in @('corpus', 'corpus-update', 'gates') } {
        $gateMode = if ($Command -ceq 'gates') { 'all' } else { $Command }
        $gateProject = 'SharpProof.Gates/SharpProof.Gates.csproj'
        Invoke-DotNet @('restore', $gateProject, '--locked-mode')
        Invoke-DotNet @(
            'run', '--project', $gateProject,
            '--configuration', $Configuration,
            '--no-restore', '--', $gateMode)
    }
    'performance' {
        Invoke-DotNet @(
            'restore', 'SharpProof.sln', '--locked-mode')
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', 'Release',
            '--no-restore')
        $output = Join-Path $repositoryRoot 'artifacts/ci/performance.json'
        & (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofGateEvidence.ps1') `
            -Gate performance `
            -OutputPath $output
        if ($LASTEXITCODE -ne 0) { throw 'Performance validation failed.' }
    }
    'performance-smoke' {
        $gateProject = 'SharpProof.Gates/SharpProof.Gates.csproj'
        Invoke-DotNet @('restore', $gateProject, '--locked-mode')
        Invoke-DotNet @(
            'run', '--project', $gateProject,
            '--configuration', $Configuration,
            '--no-restore', '--', 'performance-smoke')
    }
    'coverage' {
        if ([string]::IsNullOrWhiteSpace(
                $env:SHARPPROOF_COVERAGE_COMPARISON_REF)) {
            throw (
                'SHARPPROOF_COVERAGE_COMPARISON_REF is required for ' +
                'changed-TCB coverage enforcement.')
        }
        $comparisonRef = $env:SHARPPROOF_COVERAGE_COMPARISON_REF
        Invoke-DotNet @(
            'restore', 'SharpProof.sln', '--locked-mode')
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', 'Release',
            '--no-restore')
        $coverageRoot = Join-Path $repositoryRoot (
            'artifacts/coverage/container-' + [Guid]::NewGuid().ToString('N'))
        $coverageCollectionArguments = @{
            ResultsDirectory = $coverageRoot
        }
        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $coverageCollectionArguments.TestFilter = $TestFilter
        }
        & (Join-Path $repositoryRoot 'scripts/Invoke-SharpProofCoverage.ps1') `
            @coverageCollectionArguments
        if ($LASTEXITCODE -ne 0) { throw 'Coverage collection failed.' }
        $summaryPath = Join-Path $coverageRoot 'SharpProof.coverage.json'
        $coverageArguments = @{
            CoverageRoot = $coverageRoot
            ComparisonRef = $comparisonRef
            SummaryPath = $summaryPath
        }
        if (-not [string]::IsNullOrWhiteSpace(
                (& git status --porcelain))) {
            $coverageArguments.IncludeWorkingTree = $true
        }
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofCoverage.ps1') `
            @coverageArguments
        if ($LASTEXITCODE -ne 0) { throw 'Coverage validation failed.' }
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate coverage `
            -EvidencePath $summaryPath
    }
    'mutation' {
        $mutationOutput = 'artifacts/mutation/trusted-mutations.json'
        [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot (
                    Split-Path -Parent $mutationOutput))) | Out-Null
        $commit = (& git rev-parse HEAD).Trim()
        & (Join-Path `
            $repositoryRoot `
            'scripts/Invoke-SharpProofTrustedMutationsParallel.ps1') `
            -Configuration $Configuration `
            -OutputPath $mutationOutput `
            -ExpectedCommit $commit
        if ($LASTEXITCODE -ne 0) { throw 'Trusted mutation validation failed.' }
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate mutation `
            -EvidencePath (Join-Path $repositoryRoot $mutationOutput)
    }
    'fuzz-nightly' {
        if ($Configuration -ne 'Release') {
            throw 'fuzz-nightly requires -Configuration Release.'
        }
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', 'Release',
            '--no-restore')
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofFuzzCampaign.ps1') `
            -OutputDirectory 'artifacts/fuzz/nightly'
        if ($LASTEXITCODE -ne 0) {
            throw 'Nightly fuzz campaign failed.'
        }
    }
    'dependency-audit' {
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        $output = Join-Path $repositoryRoot (
            'artifacts/dependency-audit/dependency-audit.json')
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofDependencyAudit.ps1') `
            -SolutionPath (Join-Path $repositoryRoot 'SharpProof.sln') `
            -NuGetConfigurationPath (Join-Path $repositoryRoot 'NuGet.Config') `
            -OutputPath $output
        if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed.' }
    }
    'acceptance' {
        & (Join-Path $repositoryRoot 'eng/acceptance/Verify.ps1') `
            -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw 'Acceptance validation failed.'
        }
        if ($Configuration -ceq 'Release') {
            Invoke-DotNet @(
                'test', 'SharpProof.Gates.Test/SharpProof.Gates.Test.csproj',
                '--configuration', 'Release', '--no-build', '--no-restore',
                '--filter',
                'FullyQualifiedName~ForcedTerminationDeadlineIsStableAcrossLaunches')
        }
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate ('acceptance-' + $Configuration.ToLowerInvariant()) `
            -EvidencePath (Join-Path `
                $repositoryRoot `
                ('artifacts/timings/acceptance-' +
                    $Configuration.ToLowerInvariant() + '.json'))
    }
    'pack' {
        & (Join-Path $repositoryRoot 'scripts/Generate-Readme.ps1') -Verify
        Invoke-DotNet @('restore', 'SharpProof.sln', '--locked-mode')
        $output = Join-Path $repositoryRoot 'artifacts/container-packages'
        $artifactsRoot = [IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot 'artifacts'))
        $resolvedOutput = [IO.Path]::GetFullPath($output)
        if (-not $resolvedOutput.StartsWith(
                $artifactsRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal)) {
            throw "Refusing to clean package output outside artifacts: $resolvedOutput"
        }
        if ([IO.Directory]::Exists($resolvedOutput)) {
            [IO.Directory]::Delete($resolvedOutput, $true)
        }
        [System.IO.Directory]::CreateDirectory($output) | Out-Null
        $manifest = Get-Content (Join-Path $repositoryRoot 'scripts/package-projects.json') -Raw | ConvertFrom-Json
        $repositoryCommit = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryCommit)) {
            throw 'Could not resolve the repository commit for package provenance.'
        }
        $repositoryCommitProperty = "/p:RepositoryCommit=$repositoryCommit"
        Invoke-DotNet @(
            'build', 'SharpProof.sln', '--configuration', 'Release',
            '--no-restore', '/p:GeneratePackageOnBuild=false',
            $repositoryCommitProperty)
        foreach ($project in @($manifest.projects)) {
            Invoke-DotNet @(
                'pack', [string]$project, '--configuration', 'Release',
                '--output', $output, '--no-build', '--no-restore',
                '/p:GeneratePackageOnBuild=false',
                $repositoryCommitProperty)
        }
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPackageConsumers.ps1') `
            -PackageSource $output `
            -ValidatePackageSourceOnly
        if ($LASTEXITCODE -ne 0) { throw 'Package graph validation failed.' }
        & (Join-Path $repositoryRoot 'scripts/New-SharpProofReleaseEvidence.ps1') `
            -PackageSource $output
        if ($LASTEXITCODE -ne 0) { throw 'Release evidence generation failed.' }
        [xml]$release = Get-Content (Join-Path $repositoryRoot 'SharpProof.Release.props') -Raw
        $prefix = [string]$release.Project.PropertyGroup.SharpProofVersionPrefix
        $version = ([string]$release.Project.PropertyGroup.SharpProofPackageVersion).Replace(
            '$(SharpProofVersionPrefix)', $prefix)
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofReleaseArtifacts.ps1') `
            -PackageSource $output `
            -ExpectedTag ('v' + $version)
        if ($LASTEXITCODE -ne 0) { throw 'Release artifact validation failed.' }
    }
    'pilots' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            $PackageSource = Join-Path $repositoryRoot 'artifacts/container-packages'
        }
        & (Join-Path $repositoryRoot 'scripts/Test-SharpProofPilots.ps1') -PackageSource $PackageSource
        if ($LASTEXITCODE -ne 0) { throw 'Pilot validation failed.' }
    }
    'pilot-review' {
        & (Join-Path $repositoryRoot 'scripts/Complete-SharpProofPilotReview.ps1') `
            -SourceReportPath (Join-Path $repositoryRoot 'artifacts/pilots/report.json') `
            -ReviewLedgerPath (Join-Path $repositoryRoot 'artifacts/pilots/review-ledger.json') `
            -OutputPath (Join-Path $repositoryRoot 'artifacts/pilots/reviewed-report.json')
        if ($LASTEXITCODE -ne 0) { throw 'Pilot review validation failed.' }
        & (Join-Path $repositoryRoot `
            'scripts/Write-SharpProofQualificationReceipt.ps1') `
            -Gate pilots `
            -EvidencePath (Join-Path $repositoryRoot 'artifacts/pilots/reviewed-report.json')
    }
    'release-tag' {
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode ValidateTag
    }
    'release-baseline' {
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode ResolveCoverageBaseline
    }
    'release-plan' {
        if ([string]::IsNullOrWhiteSpace($PackageSource)) {
            throw 'release-plan requires -PackageSource.'
        }
        $planDirectory = Join-Path `
            $repositoryRoot 'artifacts/release-qualification'
        [IO.Directory]::CreateDirectory($planDirectory) | Out-Null
        & (Join-Path $repositoryRoot `
            'scripts/Publish-SharpProofRelease.ps1') `
            -PackageSource $PackageSource `
            -PlanOnly `
            -PlanOutputPath (Join-Path $planDirectory 'publication-plan.json')
    }
    'release-qualification' {
        & (Join-Path $repositoryRoot 'scripts/Generate-Readme.ps1') -Verify
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode WriteQualificationEvidence `
            -PackageSource $PackageSource
    }
    'release-publish' {
        & (Join-Path $repositoryRoot `
            'scripts/Invoke-SharpProofReleaseContainer.ps1') `
            -Mode Publish `
            -PackageSource $PackageSource
    }
}
