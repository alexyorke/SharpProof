[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath = 'artifacts\mutation\summary.json',

    [switch]$KeepWorkspace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
if (-not $output.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath must be inside the repository: $output"
}

& git -C $repositoryRoot diff --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked working tree.'
}
& git -C $repositoryRoot diff --cached --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked index.'
}

$mutations = @(
    [pscustomobject]@{
        Name = 'scalar-int32-upper-bound'
        File = 'SharpProof.Frontend\CSharpScalarSemantics.generated.cs'
        Original = 'new(SpecialType.System_Int32, true, 32, -2147483648L, 2147483647L),'
        Mutated = 'new(SpecialType.System_Int32, true, 32, -2147483648L, 2147483646L),'
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~SupportedIntegerCatalogIsExactAndExhaustive'
    },
    [pscustomobject]@{
        Name = 'lowering-unchecked-arithmetic'
        File = 'SharpProof.Frontend\RoslynOperationLowerer.cs'
        Original = "CSharpScalarSemantics.RequiresCheckedArithmetic(operation.OperatorKind) &&`n                !operation.IsChecked)"
        Mutated = "CSharpScalarSemantics.RequiresCheckedArithmetic(operation.OperatorKind) &&`n                operation.IsChecked)"
        Project = 'SharpProof.Frontend.Test\SharpProof.Frontend.Test.csproj'
        Filter = 'FullyQualifiedName~OverflowAndConversionShapesAreExactOnlyWhenRepresentable'
    },
    [pscustomobject]@{
        Name = 'smt-strict-less-than'
        File = 'SharpProof.Smt\IrSmtBackend.cs'
        Original = '_context.MkLt(Integer(left), Integer(right)),'
        Mutated = '_context.MkLe(Integer(left), Integer(right)),'
        Project = 'SharpProof.Smt.Test\SharpProof.Smt.Test.csproj'
        Filter = 'FullyQualifiedName~StrictComparisonDoesNotAcceptEqualityBoundary'
    },
    [pscustomobject]@{
        Name = 'spec-approved-assembly-name'
        File = 'SharpProof.Effects\ApiSpecResolution.cs'
        Original = 'string.Equals(approved.Name, identity.Name, StringComparison.Ordinal) &&'
        Mutated = 'string.Equals(approved.Name, identity.Name, StringComparison.Ordinal) ||'
        Project = 'SharpProof.Specs.Test\SharpProof.Specs.Test.csproj'
        Filter = 'FullyQualifiedName~ResolverRejectsATypeFromAnUnapprovedAssemblyIdentity'
    },
    [pscustomobject]@{
        Name = 'untrusted-return-annotation'
        File = 'SharpProof.Effects\ManagedAbstractFlow.cs'
        Original = "_trustedBoundaries.AuthorizesDeclaredContracts(method))"
        Mutated = 'method.ContainingAssembly != null)'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~UnverifiedReturnAnnotationsCannotDischargeRuntimeExceptions'
    },
    [pscustomobject]@{
        Name = 'trusted-boundary-nonblank-reason'
        File = 'SharpProof.Effects\TrustedBoundaryPolicy.cs'
        Original = '!string.IsNullOrWhiteSpace(reason));'
        Mutated = 'reason != "\0");'
        Project = 'SharpProof.Effects.Test\SharpProof.Effects.Test.csproj'
        Filter = 'FullyQualifiedName~UnverifiedReturnAnnotationsCannotDischargeRuntimeExceptions'
    },
    [pscustomobject]@{
        Name = 'counterexample-replay-polarity'
        File = 'SharpProof.Worker\CallableCounterexampleReplayer.cs'
        Original = 'evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false }'
        Mutated = 'evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: true }'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~TrivialNormalCompletionRequiresItsPostconditionToBeFalse'
    },
    [pscustomobject]@{
        Name = 'modeled-call-flow-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = 'predicate = application.Predicate;'
        Mutated = 'predicate = factory.Boolean(true);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallArgumentDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'modeled-call-receiver-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = 'guard = receiverGuard;'
        Mutated = 'guard = factory.Boolean(true);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallReceiverDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'modeled-call-argument-definedness'
        File = 'SharpProof.Worker\AcyclicBlockPredicateExecutor.cs'
        Original = 'guard = argumentGuard;'
        Mutated = 'guard = factory.Boolean(true);'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~SpecCallArgumentDefinednessConstrainsSubsequentFlow'
    },
    [pscustomobject]@{
        Name = 'effect-refutation-fail-closed'
        File = 'SharpProof.Worker\EffectClaimResultAssembler.cs'
        Original = 'if (evidence.Outcome == WorkerClaimOutcome.Refuted)'
        Mutated = 'if (evidence.Outcome == WorkerClaimOutcome.Unknown)'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~CompilerOnlyEffectViolationFailsClosedWithoutAReplayTrace'
    },
    [pscustomobject]@{
        Name = 'cache-manifest-binding'
        File = 'SharpProof.Worker\VerificationCache.cs'
        Original = '!string.Equals(payload.ManifestHash, manifest.Hash, StringComparison.Ordinal) ||'
        Mutated = 'false ||'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~RehashedCacheSealedForDifferentManifestMissesAndRecomputes'
    },
    [pscustomobject]@{
        Name = 'protocol-manifest-result-equality'
        File = 'SharpProof.Worker.Protocol\ProtocolJson.cs'
        Original = "actual.OrderBy(static value => value, StringComparer.Ordinal)`n            .SequenceEqual(expected.OrderBy(static value => value, StringComparer.Ordinal),`n                StringComparer.Ordinal)"
        Mutated = 'true'
        Project = 'SharpProof.Worker.Test\SharpProof.Worker.Test.csproj'
        Filter = 'FullyQualifiedName~StrictResponseValidationRequiresExactManifestAndResultSets'
    },
    [pscustomobject]@{
        Name = 'launcher-kill-on-close'
        File = 'SharpProof.Worker.Launcher\Program.cs'
        Original = 'NativeMethods.JobObjectLimitFlags.KillOnJobClose |'
        Mutated = 'NativeMethods.JobObjectLimitFlags.ActiveProcess |'
        Project = 'SharpProof.Package.Test\SharpProof.Package.Test.csproj'
        Filter = 'FullyQualifiedName~WorkerContainmentIsMandatoryOnTheSupportedHost'
    }
)

$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) 'SharpProof-mutation'
$workspace = Join-Path $mutationRoot (
    'workspace-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $workspace 'source'
$archive = Join-Path $workspace 'source.zip'
$logs = Join-Path (Split-Path -Parent $output) 'mutation-logs'
New-Item -ItemType Directory -Path $sourceRoot, $logs -Force | Out-Null

function Invoke-IsolatedDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$LogName
    )

    $log = Join-Path $logs $LogName
    Push-Location $sourceRoot
    try {
        & (Join-Path $sourceRoot 'scripts\Invoke-SharpProofDotnet.ps1') `
            -MemoryLimitMb 8192 `
            -TimeoutSeconds 600 `
            @Arguments *> $log
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

function Assert-UniqueMutationTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$Needle,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $first = $Content.IndexOf($Needle, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        throw "Mutation '$Name' target text was not found."
    }
    $second = $Content.IndexOf(
        $Needle,
        $first + $Needle.Length,
        [StringComparison]::Ordinal)
    if ($second -ge 0) {
        throw "Mutation '$Name' target text is not unique."
    }
}

try {
    & git -C $repositoryRoot archive `
        --format=zip `
        --output=$archive `
        HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "git archive failed with exit code $LASTEXITCODE."
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $sourceRoot

    $restoreExit = Invoke-IsolatedDotnet `
        -Arguments @('restore', 'SharpProof.sln') `
        -LogName 'restore.log'
    if ($restoreExit -ne 0) {
        throw "Mutation workspace restore failed; see $logs\restore.log."
    }

    foreach ($mutation in $mutations) {
        $baselineExit = Invoke-IsolatedDotnet `
            -Arguments @(
                'test',
                $mutation.Project,
                '-c',
                $Configuration,
                '--no-restore',
                '--filter',
                $mutation.Filter,
                '--logger',
                'console;verbosity=minimal') `
            -LogName ($mutation.Name + '-baseline.log')
        if ($baselineExit -ne 0) {
            throw (
                "Baseline for mutation '$($mutation.Name)' failed; see " +
                "$logs\$($mutation.Name)-baseline.log.")
        }
    }

    $results = @()
    foreach ($mutation in $mutations) {
        $path = Join-Path $sourceRoot $mutation.File
        $originalContent = [IO.File]::ReadAllText($path)
        Assert-UniqueMutationTarget `
            -Content $originalContent `
            -Needle $mutation.Original `
            -Name $mutation.Name
        $mutatedContent = $originalContent.Replace(
            $mutation.Original,
            $mutation.Mutated,
            [StringComparison]::Ordinal)
        try {
            [IO.File]::WriteAllText(
                $path,
                $mutatedContent,
                [Text.UTF8Encoding]::new($false))
            $buildExit = Invoke-IsolatedDotnet `
                -Arguments @(
                    'build',
                    $mutation.Project,
                    '-c',
                    $Configuration,
                    '--no-restore') `
                -LogName ($mutation.Name + '-build.log')
            if ($buildExit -ne 0) {
                throw (
                    "Mutation '$($mutation.Name)' did not compile; see " +
                    "$logs\$($mutation.Name)-build.log.")
            }
            $testExit = Invoke-IsolatedDotnet `
                -Arguments @(
                    'test',
                    $mutation.Project,
                    '-c',
                    $Configuration,
                    '--no-build',
                    '--filter',
                    $mutation.Filter,
                    '--logger',
                    'console;verbosity=minimal') `
                -LogName ($mutation.Name + '-test.log')
            if ($testExit -eq 0) {
                throw (
                    "Mutation '$($mutation.Name)' survived its focused test; " +
                    "see $logs\$($mutation.Name)-test.log.")
            }
            if ($testExit -eq 124) {
                throw (
                    "Mutation '$($mutation.Name)' timed out instead of being " +
                    "killed by an assertion.")
            }
            $results += [pscustomobject]@{
                name = $mutation.Name
                file = $mutation.File.Replace('\', '/')
                test = $mutation.Filter
                killed = $true
            }
        }
        finally {
            [IO.File]::WriteAllText(
                $path,
                $originalContent,
                [Text.UTF8Encoding]::new($false))
        }
    }

    $outputDirectory = Split-Path -Parent $output
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $sha = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    [pscustomobject]@{
        schemaVersion = 1
        commit = $sha
        configuration = $Configuration
        mutationCount = $results.Count
        killedCount = @($results | Where-Object killed).Count
        mutations = $results
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $output -Encoding utf8NoBOM
    Write-Host "Killed $($results.Count) trusted-boundary mutations."
    Write-Host "Evidence: $output"
}
finally {
    if (-not $KeepWorkspace -and
        (Test-Path -LiteralPath $workspace)) {
        $resolvedWorkspace = [IO.Path]::GetFullPath($workspace)
        $resolvedMutationRoot = [IO.Path]::GetFullPath($mutationRoot)
        if (-not $resolvedWorkspace.StartsWith(
                $resolvedMutationRoot +
                [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove mutation workspace: $resolvedWorkspace"
        }
        Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
    }
}
