[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$acceptanceRoot = $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $acceptanceRoot '..\..')).Path
$contractPath = Join-Path $acceptanceRoot 'contract.json'
$wrapperPath = Join-Path $repositoryRoot 'scripts\Invoke-SharpProofDotnet.ps1'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
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
    'SharpProof.Verifier.Win-x64\buildTransitive\SharpProof.Verifier.Win-x64.props'
[xml]$verifierProps = Get-Content -LiteralPath $verifierPropsPath -Raw
$verifierTargetsPath = Join-Path `
    $repositoryRoot `
    'SharpProof.Verifier.Win-x64\buildTransitive\SharpProof.Verifier.Win-x64.targets'
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
        -MemoryLimitMb ([int]$contract.worker.maximumMemoryMiB) `
        -TimeoutSeconds $TimeoutSeconds `
        @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Measure-RepositoryNonblankLines {
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
    $nonblankLines = 0
    foreach ($untypedRelativePath in $Paths) {
        $relativePath = [string]$untypedRelativePath
        if ([string]::IsNullOrWhiteSpace($relativePath) -or
            -not $seenPaths.Add($relativePath)) {
            throw "$Scope contains a blank or duplicate path: $relativePath"
        }
        $fullPath = [IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot $relativePath))
        if (-not $fullPath.StartsWith(
                $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Invalid $Scope path: $relativePath"
        }
        $nonblankLines += @(
            Get-Content -LiteralPath $fullPath |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        ).Count
    }
    return $nonblankLines
}

Assert-Equal $contract.schemaVersion 3 'schemaVersion'
Assert-Equal $contract.releaseLine '0.2.0-preview' 'releaseLine'
Assert-Equal $contract.flagship 'effects' 'flagship'
Assert-Equal $contract.analyzer.defaultProfile 'advisory' 'analyzer.defaultProfile'
Assert-Equal $contract.analyzer.defaultFeatures 'all' 'analyzer.defaultFeatures'
Assert-Equal $contract.analyzer.defaultVerifyPolicy 'advisory' 'analyzer.defaultVerifyPolicy'
Assert-Equal $contract.analyzer.defaultAssumptionPolicy 'allow' 'analyzer.defaultAssumptionPolicy'
Assert-Equal $contract.analyzer.defaultDiagnosticSeverity 'Info' 'analyzer.defaultDiagnosticSeverity'
Assert-Equal $contract.analyzer.diagnosticsEnabledByDefault $true 'analyzer.diagnosticsEnabledByDefault'
Assert-Equal $contract.analyzer.unsupportedUnannotatedCallableBehavior 'silent' 'analyzer.unsupportedUnannotatedCallableBehavior'
Assert-Equal $contract.analyzer.unsupportedSelectedCallableDiagnostic 'SP0047' 'analyzer.unsupportedSelectedCallableDiagnostic'
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
Assert-Equal $contract.worker.protocolVersion 5 'worker.protocolVersion'
Assert-Equal $contract.worker.manifestSchemaVersion 2 'worker.manifestSchemaVersion'
Assert-Equal $contract.worker.compilerArtifactSchemaVersion 3 'worker.compilerArtifactSchemaVersion'
Assert-Equal $contract.worker.maximumParallelism 4 'worker.maximumParallelism'
Assert-Equal $contract.worker.maximumMemoryMiB 2048 'worker.maximumMemoryMiB'
Assert-Equal $contract.worker.queryRlimit 3000000 'worker.queryRlimit'
Assert-Equal $contract.worker.methodRlimit 20000000 'worker.methodRlimit'
Assert-Equal $contract.worker.maximumMethodWallSeconds 10 'worker.maximumMethodWallSeconds'
Assert-Equal $contract.worker.maximumProjectWallSeconds 300 'worker.maximumProjectWallSeconds'
Assert-Equal $contract.worker.forcedTerminationMilliseconds 1000 'worker.forcedTerminationMilliseconds'
Assert-Equal $contract.cache.schemaVersion 6 'cache.schemaVersion'
Assert-Equal $contract.cache.maximumMiB 512 'cache.maximumMiB'
Assert-Equal ($contract.cache.cacheableOutcomes -join ',') 'Proven,Refuted' 'cache.cacheableOutcomes'
Assert-Equal `
    (Get-MsBuildProperty $portableProps '_SharpProofPortablePackagePresent' 'portable package') `
    'true' `
    '_SharpProofPortablePackagePresent'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps '_SharpProofVerifierPackagePresent' 'verifier package') `
    'true' `
    '_SharpProofVerifierPackagePresent'
Assert-Equal $packageManifest.schemaVersion 1 'package manifest schemaVersion'
$expectedPackageProjects = @(
    'SharpProof.Attributes/SharpProof.Attributes.csproj',
    'SharpProof.Package/SharpProof.Package.csproj',
    'SharpProof.Verifier.Win-x64/SharpProof.Verifier.Win-x64.csproj'
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
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyProcessMemoryLimitBytes' 'verifier package') `
    ([string]([int64]$contract.worker.maximumMemoryMiB * 1024 * 1024)) `
    'SharpProofVerifyProcessMemoryLimitBytes'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyTerminationGraceMilliseconds' 'verifier package') `
    ([string]$contract.worker.forcedTerminationMilliseconds) `
    'SharpProofVerifyTerminationGraceMilliseconds'
Assert-Equal `
    (Get-MsBuildProperty $verifierProps 'SharpProofVerifyCacheMaximumBytes' 'verifier package') `
    ([string]([int64]$contract.cache.maximumMiB * 1024 * 1024)) `
    'SharpProofVerifyCacheMaximumBytes'

Push-Location $repositoryRoot
try {
    $kernelPaths = @($contract.trustedKernel.paths)
    $kernelMaximum = [int]$contract.trustedKernel.maximumNonblankLines
    if ($kernelPaths.Count -eq 0 -or $kernelMaximum -le 0) {
        throw 'The trusted-kernel LOC contract must declare paths and a positive limit.'
    }
    $kernelNonblankLines = Measure-RepositoryNonblankLines `
        -Paths $kernelPaths `
        -Scope 'trusted-kernel'
    if ($kernelNonblankLines -gt $kernelMaximum) {
        throw "Trusted-kernel nonblank LOC $kernelNonblankLines exceeds " +
            "the contract limit $kernelMaximum."
    }
    Write-Host (
        "Trusted-kernel nonblank lines: $kernelNonblankLines " +
        "(maximum $kernelMaximum)")

    $requiredTcbComponents = @(
        'discovery',
        'lowering',
        'execution',
        'encoding',
        'replay',
        'policy',
        'cacheValidation'
    )
    $tcbComponents = @($contract.trustedComputingBase.components)
    $actualTcbComponents = @(
        $tcbComponents |
            ForEach-Object { [string]$_.name } |
            Sort-Object
    )
    $expectedTcbComponents = @($requiredTcbComponents | Sort-Object)
    if (($actualTcbComponents -join ',') -ne
        ($expectedTcbComponents -join ',')) {
        throw "Trusted-computing-base components must be exactly: " +
            ($requiredTcbComponents -join ', ') + "."
    }
    $allTcbPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($component in $tcbComponents) {
        $name = [string]$component.name
        $paths = @($component.paths)
        $maximum = [int]$component.maximumNonblankLines
        if ($maximum -le 0) {
            throw "Trusted-computing-base component '$name' must have a positive limit."
        }
        foreach ($path in $paths) {
            if (-not $allTcbPaths.Add([string]$path)) {
                throw "Trusted-computing-base path belongs to multiple components: $path"
            }
        }
        $nonblankLines = Measure-RepositoryNonblankLines `
            -Paths $paths `
            -Scope "trusted-computing-base component '$name'"
        if ($nonblankLines -gt $maximum) {
            throw "Trusted-computing-base component '$name' nonblank LOC " +
                "$nonblankLines exceeds the contract limit $maximum."
        }
        Write-Host (
            "Trusted-computing-base $name nonblank lines: " +
            "$nonblankLines (maximum $maximum)")
    }

    & (Join-Path $repositoryRoot 'scripts\Test-ProductionCSharpSize.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw 'The production C# size ratchet failed.'
    }

    if (-not $SkipBuild) {
        Invoke-SharpProofDotnet `
            -Arguments @('build', 'SharpProof.sln', '-c', $Configuration, '--no-restore') `
            -TimeoutSeconds ([int]$contract.worker.maximumProjectWallSeconds)
    }

    $testProjects = @(
        'SharpProof.Analyzer.Test\SharpProof.Analyzer.Test.csproj',
        'SharpProof.ArchitectureTest\SharpProof.ArchitectureTest.csproj',
        'SharpProof.Attributes.Test\SharpProof.Attributes.Test.csproj',
        'SharpProof.ContractForGenerator.Test\SharpProof.ContractForGenerator.Test.csproj',
        'SharpProof.Contracts.Test\SharpProof.Contracts.Test.csproj',
        'SharpProof.Dataflow.Test\SharpProof.Dataflow.Test.csproj',
        'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj',
        'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj',
        'SharpProof.Ir.Test\SharpProof.Ir.Test.csproj',
        'SharpProof.Meta.Analyzers.Test\SharpProof.Meta.Analyzers.Test.csproj',
        'SharpProof.Package.Test\SharpProof.Package.Test.csproj',
        'SharpProof.Smt.Test\SharpProof.Smt.Test.csproj',
        'SharpProof.Specs.Test\SharpProof.Specs.Test.csproj',
        'SharpProof.Testing.Test\SharpProof.Testing.Test.csproj',
        'SharpProof.Gates.Test\SharpProof.Gates.Test.csproj',
        'SharpProof.Fuzz.Test\SharpProof.Fuzz.Test.csproj',
        'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj',
        'SharpProof.Verify.Test\SharpProof.Verify.Test.csproj'
    )

    foreach ($testProject in $testProjects) {
        $resolvedTestProject = Join-Path $repositoryRoot $testProject
        if (-not (Test-Path -LiteralPath $resolvedTestProject -PathType Leaf)) {
            throw "Required test project is missing: $testProject"
        }

        $testTimeoutSeconds = if ($testProject -like 'SharpProof.Package.Test*') {
            600
        } else {
            300
        }
        Invoke-SharpProofDotnet -TimeoutSeconds $testTimeoutSeconds -Arguments @(
            'test',
            $testProject,
            '-c',
            $Configuration,
            '--no-build',
            '--logger',
            'console;verbosity=minimal'
        )
    }

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
}
finally {
    Pop-Location
}

Write-Host 'SharpProof acceptance checks passed.'
