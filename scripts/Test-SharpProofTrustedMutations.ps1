[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath = 'artifacts\mutation\summary.json',

    [string[]]$MutationName = @(),

    [ValidateRange(0, 15)]
    [int]$MutationShardIndex = 0,

    [ValidateRange(1, 16)]
    [int]$MutationShardCount = 1,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit,

    [string]$BaselineEvidencePath = '',

    [switch]$BaselineOnly,

    [switch]$Resume,

    [switch]$KeepWorkspace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationEvidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationScheduling.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'SharpProof.MutationBaselines.psm1') -Force
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Resolve-SharpProofContainedPath `
    -Root $repositoryRoot -Path $OutputPath -ParameterName 'OutputPath'
$baselineFile = if ([string]::IsNullOrWhiteSpace($BaselineEvidencePath)) {
    $null
}
else {
    Resolve-SharpProofContainedPath `
        -Root $repositoryRoot -Path $BaselineEvidencePath `
        -ParameterName 'BaselineEvidencePath'
}
if ($BaselineOnly -and $null -eq $baselineFile) {
    throw 'BaselineOnly requires BaselineEvidencePath.'
}
if ($BaselineOnly -and
        ($MutationShardCount -ne 1 -or $MutationShardIndex -ne 0)) {
    throw 'BaselineOnly cannot be combined with catalog sharding.'
}
if ($BaselineOnly -and $Resume) {
    throw 'BaselineOnly cannot be combined with Resume.'
}

$sourceCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
    throw 'Unable to resolve the mutation source commit.'
}
if ($ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw "ExpectedCommit must be a 40-character commit SHA: '$ExpectedCommit'."
}
if ($sourceCommit -ne $ExpectedCommit) {
    throw "Mutation source commit '$sourceCommit' does not match '$ExpectedCommit'."
}

function Get-MutationTargetIssue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$Needle
    )

    $first = $Content.IndexOf($Needle, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        return 'target text was not found'
    }
    if ($Content.IndexOf(
            $Needle,
            $first + $Needle.Length,
            [StringComparison]::Ordinal) -ge 0) {
        return 'target text is not unique'
    }
    return $null
}

$mutationCatalogPath = Join-Path $repositoryRoot 'eng/mutations/trusted-mutations.json'
if (-not (Test-Path -LiteralPath $mutationCatalogPath -PathType Leaf)) {
    throw "Trusted mutation catalog is missing: $mutationCatalogPath"
}
try {
    $catalogValue = Get-Content -LiteralPath $mutationCatalogPath -Raw |
        ConvertFrom-Json
}
catch {
    throw "Trusted mutation catalog is invalid: $($_.Exception.Message)"
}
if ($catalogValue -isnot [Array]) {
    throw 'Trusted mutation catalog must be a JSON array.'
}

$mutationPropertyNames = @(
    'name',
    'file',
    'original',
    'mutated',
    'project',
    'filter'
)
$mutationNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$mutationRows = foreach ($entry in $catalogValue) {
        if ($null -eq $entry) {
            throw 'Trusted mutation catalog contains a null entry.'
        }
        $properties = @($entry.PSObject.Properties.Name)
        $missing = @($mutationPropertyNames | Where-Object {
                $_ -notin $properties
            })
        $extra = @($properties | Where-Object {
                $_ -notin $mutationPropertyNames
            })
        if ($properties.Count -ne $mutationPropertyNames.Count -or
            $missing.Count -ne 0 -or
            $extra.Count -ne 0) {
            throw (
                'Trusted mutation catalog entries must contain exactly ' +
                'name, file, original, mutated, project, and filter.')
        }

        foreach ($propertyName in $mutationPropertyNames) {
            if ($entry.PSObject.Properties[$propertyName].Value -isnot [string]) {
                throw (
                    "Trusted mutation catalog property '$propertyName' " +
                    'must be a string.')
            }
        }

        $name = [string]$entry.name
        if ([string]::IsNullOrWhiteSpace($name) -or
            -not $mutationNames.Add($name)) {
            throw "Trusted mutation names must be nonempty and unique: '$name'."
        }
        foreach ($pathProperty in @('file', 'project')) {
            $path = [string]$entry.$pathProperty
            if ([string]::IsNullOrWhiteSpace($path) -or
                $path.Contains('\') -or
                [IO.Path]::IsPathRooted($path) -or
                $path.StartsWith('/', [StringComparison]::Ordinal) -or
                $path.EndsWith('/', [StringComparison]::Ordinal) -or
                $path.Contains('//')) {
                throw (
                    "Trusted mutation $pathProperty path is not canonical: " +
                    "'$path'.")
            }
            foreach ($segment in $path.Split('/')) {
                if ($segment -eq '.' -or $segment -eq '..') {
                    throw (
                        "Trusted mutation $pathProperty path contains a dot " +
                        "segment: '$path'.")
                }
            }
        }
        if ([string]::IsNullOrWhiteSpace([string]$entry.original) -or
            [string]::IsNullOrWhiteSpace([string]$entry.filter)) {
            throw (
                "Trusted mutation '$name' has an empty original or filter.")
        }

        [pscustomobject][ordered]@{
            Name = $name
            File = [string]$entry.file
            Original = [string]$entry.original
            Mutated = [string]$entry.mutated
            Project = [string]$entry.project
            Filter = [string]$entry.filter
        }
    }
$mutations = [object[]]$mutationRows

$acceptanceContract = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng\acceptance\contract.json') -Raw |
    ConvertFrom-Json
$mutationPolicy = $acceptanceContract.mutationEvidence
$catalogCount = @($mutations).Count
if ($catalogCount -ne [int]$mutationPolicy.expectedCatalogCount) {
    throw (
        'Trusted mutation registrations do not match the acceptance ' +
        "catalog policy. Actual count: $catalogCount.")
}

$invalidTargets = [Collections.Generic.List[string]]::new()
$liveTargetCache = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal)
foreach ($mutation in $mutations) {
    $targetKey = ([string]$mutation.File).Replace('\', '/')
    $targetState = $null
    if (-not $liveTargetCache.TryGetValue($targetKey, [ref]$targetState)) {
        $targetPath = Join-Path $repositoryRoot $targetKey
        if (Test-Path -LiteralPath $targetPath -PathType Leaf) {
            $targetState = [pscustomobject]@{
                Exists = $true
                Content = Get-Content -LiteralPath $targetPath -Raw
            }
        }
        else {
            $targetState = [pscustomobject]@{
                Exists = $false
                Content = $null
            }
        }
        $liveTargetCache.Add($targetKey, $targetState)
    }

    if (-not $targetState.Exists) {
        $invalidTargets.Add(
            ([string]$mutation.Name) + ': target file was not found')
        continue
    }
    $issue = Get-MutationTargetIssue `
        -Content $targetState.Content `
        -Needle ([string]$mutation.Original)
    if ($null -ne $issue) {
        $invalidTargets.Add(
            ([string]$mutation.Name) + ': ' + $issue)
    }
}
if ($invalidTargets.Count -ne 0) {
    throw (
        "Trusted mutation target preflight failed:`n - " +
        ($invalidTargets -join "`n - "))
}

& git -C $repositoryRoot diff --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked working tree.'
}
& git -C $repositoryRoot diff --cached --quiet --
if ($LASTEXITCODE -ne 0) {
    throw 'Mutation testing requires a clean tracked index.'
}

$defaultShardWeight = [int]$acceptanceContract.automation.mutationDefaultWeight
if ($defaultShardWeight -lt 1) {
    throw 'The default mutation shard weight must be positive.'
}
$projectWeights = @{}
foreach ($property in @(
        $acceptanceContract.automation.mutationProjectWeights.PSObject.Properties)) {
    $weight = [int]$property.Value
    if ($weight -lt 1) {
        throw "Mutation project weight must be positive: $($property.Name)."
    }
    $projectWeights[[string]$property.Name] = $weight
}
if ($MutationName.Count -gt 0 -and $MutationShardCount -ne 1) {
    throw 'Named mutation selection cannot be combined with catalog sharding.'
}
if ($MutationShardIndex -ge $MutationShardCount) {
    throw 'MutationShardIndex must be less than MutationShardCount.'
}

if ($MutationName.Count -gt 0) {
    $selection = 'selected'
    $knownNames = @($mutations.Name)
    $requestedNames = @($MutationName | Select-Object -Unique)
    $unknownNames = @($requestedNames | Where-Object { $_ -notin $knownNames })
    if ($unknownNames.Count -gt 0) {
        throw "Unknown mutation name(s): $($unknownNames -join ', ')."
    }
    $mutations = [object[]]($mutations | Where-Object {
            $_.Name -in $requestedNames
        })
}
elseif ($MutationShardCount -gt 1) {
    $selection = 'selected'
    $plan = Get-SharpProofWeightedMutationShards `
        -Mutations $mutations `
        -ShardCount $MutationShardCount `
        -ProjectWeights $projectWeights `
        -DefaultWeight $defaultShardWeight
    $selected = @($plan.Shards[$MutationShardIndex])
    $mutations = [object[]]($selected | ForEach-Object {
            $_.Mutation | Add-Member -NotePropertyName CatalogOrdinal `
                -NotePropertyValue ([int]$_.CatalogOrdinal) -PassThru
        })
    if ($mutations.Count -eq 0) {
        throw "Mutation shard $MutationShardIndex is empty."
    }
}
else {
    $selection = 'full'
}

if ($Resume -and $selection -ne 'full') {
    throw 'Resume is supported only for the complete mutation catalog.'
}

$completedResults = @()
if ($Resume -and (Test-Path -LiteralPath $output -PathType Leaf)) {
    $checkpoint = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    $checkpointMutations = @($checkpoint.mutations)
    if ([int]$checkpoint.schemaVersion -ne 2 -or
        [string]$checkpoint.commit -ne $sourceCommit -or
        [string]$checkpoint.configuration -ne $Configuration -or
        [string]$checkpoint.selection -notin @('inProgress', 'full') -or
        [int]$checkpoint.catalogCount -ne $catalogCount -or
        [int]$checkpoint.mutationCount -ne $checkpointMutations.Count -or
        [int]$checkpoint.killedCount -ne $checkpointMutations.Count) {
        throw 'Mutation checkpoint does not match the current exact catalog run.'
    }

    if ($checkpointMutations.Count -gt $mutations.Count) {
        throw 'Mutation checkpoint contains more results than the catalog.'
    }
    for ($index = 0; $index -lt $checkpointMutations.Count; $index++) {
        $result = $checkpointMutations[$index]
        $registered = $mutations[$index]
        $name = [string]$result.name
        if ($name -ne [string]$registered.Name) {
            throw 'Mutation checkpoint is not a canonical catalog prefix.'
        }
        if ([string]$result.file -ne ([string]$registered.File).Replace('\', '/') -or
            [string]$result.project -ne ([string]$registered.Project).Replace('\', '/') -or
            [string]$result.test -ne [string]$registered.Filter -or
            [string]$result.original -ne [string]$registered.Original -or
            [string]$result.mutated -ne [string]$registered.Mutated -or
            -not [bool]$result.killed -or
            [int]$result.exitCode -eq 0 -or
            [int]$result.assertionFailureCount -lt 1 -or
            [string]$result.baselineInvocation -ne
                (Get-SharpProofMutationBaselineInvocation `
                    -Project ([string]$registered.Project) `
                    -Filter ([string]$registered.Filter) `
                    -Configuration $Configuration).Identity -or
            @($result.baselineSelectedTests).Count -eq 0) {
            throw 'Mutation checkpoint result does not match its catalog entry.'
        }
    }
    $completedResults = $checkpointMutations
    if ([string]$checkpoint.selection -eq 'full') {
        if ($completedResults.Count -ne $catalogCount) {
            throw 'Completed mutation evidence does not cover the full catalog.'
        }
        & (Join-Path $PSScriptRoot 'Test-SharpProofMutationCatalog.ps1') `
            -EvidencePath $output `
            -ExpectedCommit $sourceCommit
        Write-Host "Mutation evidence is already complete: $output"
        return
    }
}

$completedMutationNames = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($result in $completedResults) {
    [void]$completedMutationNames.Add([string]$result.name)
}

$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) 'SharpProof-mutation'
$workspace = Join-Path $mutationRoot (
    'workspace-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $workspace 'source'
$runId = $sourceCommit.Substring(0, 12) + '-' +
    [Guid]::NewGuid().ToString('N')
$logs = Join-Path (Join-Path (Split-Path -Parent $output) 'mutation-logs') $runId
New-Item -ItemType Directory -Path $sourceRoot, $logs -Force | Out-Null
if (-not $Resume) {
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
}
$restoreElapsedMilliseconds = 0L
$baselineElapsedMilliseconds = 0L
$mutationElapsedMilliseconds = 0L
$baselineInvocationCount = 0
$mutationInvocationCount = 0
$mutationTimings = [Collections.Generic.List[object]]::new()

function Invoke-IsolatedDotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$LogName
    )

    $log = Join-Path $logs $LogName
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $exitCode = $null
    Push-Location $sourceRoot
    try {
        & (Join-Path $sourceRoot 'scripts\Invoke-SharpProofDotnet.ps1') `
            -TimeoutSeconds 600 `
            @Arguments *> $log
        $exitCode = [int]$LASTEXITCODE
    }
    finally {
        $timer.Stop()
        Pop-Location
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        LogPath = $log
        ElapsedMilliseconds = [long]$timer.Elapsed.TotalMilliseconds
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

    $issue = Get-MutationTargetIssue -Content $Content -Needle $Needle
    if ($null -ne $issue) {
        throw "Mutation '$Name' $issue."
    }
}

function Write-MutationEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Results,

        [Parameter(Mandatory = $true)]
        [ValidateSet('inProgress', 'selected', 'full')]
        [string]$EvidenceSelection
    )

    $outputDirectory = Split-Path -Parent $output
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $temporaryOutput =
        $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [pscustomobject]@{
        schemaVersion = 2
        commit = $sourceCommit
        configuration = $Configuration
        selection = $EvidenceSelection
        catalogCount = $catalogCount
        mutationCount = $Results.Count
        killedCount = @($Results | Where-Object killed).Count
        mutations = $Results
        timing = [ordered]@{
            restoreElapsedMilliseconds = $restoreElapsedMilliseconds
            baselineElapsedMilliseconds = $baselineElapsedMilliseconds
            mutationElapsedMilliseconds = $mutationElapsedMilliseconds
            baselineInvocationCount = $baselineInvocationCount
            mutationInvocationCount = $mutationInvocationCount
            mutations = @($mutationTimings)
        }
    } | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $temporaryOutput -Encoding utf8NoBOM
    Move-Item -LiteralPath $temporaryOutput -Destination $output -Force
}

try {
    # Package provenance tests require the exact commit as well as its files.
    # Keep mutations in a private checkout, with no writes to the source repo.
    & git clone --quiet --shared --no-checkout $repositoryRoot $sourceRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Mutation workspace clone failed with exit code $LASTEXITCODE."
    }
    & git -C $sourceRoot checkout --quiet --detach $sourceCommit
    if ($LASTEXITCODE -ne 0) {
        throw "Mutation workspace checkout failed with exit code $LASTEXITCODE."
    }

    $checkoutTargetCache = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    foreach ($mutation in $mutations) {
        $targetKey = ([string]$mutation.File).Replace('\', '/')
        $content = $null
        if (-not $checkoutTargetCache.TryGetValue($targetKey, [ref]$content)) {
            $path = Join-Path $sourceRoot $targetKey
            $content = [IO.File]::ReadAllText($path)
            $checkoutTargetCache.Add($targetKey, $content)
        }
        Assert-UniqueMutationTarget `
            -Content $content `
            -Needle $mutation.Original `
            -Name $mutation.Name
    }

    $restoreRun = Invoke-IsolatedDotnet `
        -Arguments @('restore', 'SharpProof.slnx') `
        -LogName 'restore.log'
    $restoreElapsedMilliseconds = $restoreRun.ElapsedMilliseconds
    if ($restoreRun.ExitCode -ne 0) {
        throw "Mutation workspace restore failed; see $logs\restore.log."
    }

    $pendingMutations = @($mutations | Where-Object {
            -not $completedMutationNames.Contains([string]$_.Name)
        })
    if ($null -ne $baselineFile -and -not $BaselineOnly) {
        if (-not (Test-Path -LiteralPath $baselineFile -PathType Leaf)) {
            throw "Mutation baseline evidence is missing: $baselineFile"
        }
        $savedBaseline = Get-Content -LiteralPath $baselineFile -Raw |
            ConvertFrom-Json
        $savedTests = @($savedBaseline.tests)
        if ([int]$savedBaseline.schemaVersion -ne 2 -or
            [string]$savedBaseline.commit -ne $sourceCommit -or
            [string]$savedBaseline.configuration -ne $Configuration -or
            [string]$savedBaseline.selection -notin @('full', 'selected') -or
            [int]$savedBaseline.catalogCount -ne $catalogCount -or
            [int]$savedBaseline.testCount -ne $savedTests.Count) {
            throw 'Mutation baseline evidence does not match this campaign.'
        }
        $baselineMap = [Collections.Generic.Dictionary[
            string, object]]::new([StringComparer]::Ordinal)
        foreach ($test in $savedTests) {
            $project = [string]$test.project
            $filter = [string]$test.filter
            $ledger = @($test.ledger)
            if ([string]::IsNullOrWhiteSpace($project) -or
                [string]::IsNullOrWhiteSpace($filter) -or
                [string]$test.configuration -ne $Configuration -or
                $ledger.Count -eq 0) {
                throw 'Mutation baseline evidence contains an invalid test row.'
            }
            $invocation = Get-SharpProofMutationBaselineInvocation `
                -Project $project -Filter $filter -Configuration $Configuration
            if ([string]$test.invocation -ne $invocation.Identity) {
                throw 'Mutation baseline evidence has a mismatched invocation identity.'
            }
            $baselineRoot = Split-Path -Parent $baselineFile
            $baselineTrxPath = [IO.Path]::GetFullPath((Join-Path `
                    $baselineRoot ([string]$test.trx)))
            if (-not $baselineTrxPath.StartsWith(
                    $baselineRoot + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::Ordinal) -or
                -not [IO.File]::Exists($baselineTrxPath)) {
                throw 'Mutation baseline evidence has an invalid TRX receipt.'
            }
            $method = $filter.Substring('FullyQualifiedName~'.Length)
            [void](Read-SharpProofMutationTestEvidence `
                    -TrxPath $baselineTrxPath `
                    -EvidenceName ($project + ' saved baseline') `
                    -Mode Baseline `
                    -ProcessExitCode 0 `
                    -ExpectedMethodName $method `
                    -ExpectedLedger $ledger)
            $key = $invocation.Identity
            if (-not $baselineMap.TryAdd($key, [object]$test)) {
                throw "Mutation baseline evidence duplicates '$project::$filter'."
            }
        }
        foreach ($mutation in $pendingMutations) {
            $invocation = Get-SharpProofMutationBaselineInvocation `
                -Project ([string]$mutation.Project) `
                -Filter ([string]$mutation.Filter) `
                -Configuration $Configuration
            $key = $invocation.Identity
            if (-not $baselineMap.ContainsKey($key)) {
                throw (
                    "Mutation baseline evidence does not cover " +
                    "'$($mutation.Project)::$($mutation.Filter)'.")
            }
            $saved = $baselineMap[$key]
            $mutation | Add-Member `
                -NotePropertyName BaselineLedger `
                -NotePropertyValue @($saved.ledger)
            $mutation | Add-Member `
                -NotePropertyName BaselineInvocation `
                -NotePropertyValue $key
            $mutation | Add-Member -NotePropertyName BaselineTrx `
                -NotePropertyValue ([string]$saved.trx)
        }
    }
    else {
        $baselineRows = [Collections.Generic.List[object]]::new()
        $baselineGroupIndex = 0
        $baselinePlan = @(Get-SharpProofMutationBaselinePlan `
                -Mutations $pendingMutations `
                -Configuration $Configuration)
        foreach ($baselineGroup in $baselinePlan) {
            $baselineGroupIndex++
            $projectMutations = @($baselineGroup.Mutations)
            $invocation = $baselineGroup.Invocation
            $expectedMethodName = $invocation.Filter.Substring(
                'FullyQualifiedName~'.Length)
            $baselineTrxName = 'project-' +
                $baselineGroupIndex.ToString(
                    'D2', [Globalization.CultureInfo]::InvariantCulture) +
                '-baseline.trx'
            $baselineTrx = Join-Path $logs $baselineTrxName
            Remove-Item -LiteralPath $baselineTrx `
                -Force -ErrorAction SilentlyContinue
            $baselineRun = Invoke-IsolatedDotnet `
                -Arguments @(
                    'test',
                    $invocation.Project,
                    '-c',
                    $Configuration,
                    '--no-restore',
                    '--filter',
                    $invocation.Filter,
                    '--logger',
                    'console;verbosity=minimal',
                    '--logger',
                    "trx;LogFileName=$baselineTrxName",
                    '--results-directory',
                    $logs) `
                -LogName ('project-' + $baselineGroupIndex.ToString(
                        'D2', [Globalization.CultureInfo]::InvariantCulture) +
                    '-baseline.log')
            $baselineElapsedMilliseconds += $baselineRun.ElapsedMilliseconds
            $baselineInvocationCount++
            Assert-SharpProofMutationBaselineResult `
                -ExitCode $baselineRun.ExitCode `
                -TrxPath $baselineTrx `
                -EvidenceName ($invocation.Project + '::' + $invocation.Filter)
            $baselineTestEvidence = Read-SharpProofMutationTestEvidence `
                -TrxPath $baselineTrx `
                -EvidenceName ($invocation.Project + ' baseline') `
                -Mode Baseline `
                -ProcessExitCode $baselineRun.ExitCode `
                -ExpectedMethodName $expectedMethodName
            $ledger = @($baselineTestEvidence.testLedgers[$expectedMethodName])
            $baselineEvidenceRoot = if ($null -ne $baselineFile) {
                Split-Path -Parent $baselineFile
            }
            else {
                Split-Path -Parent $output
            }
            $baselineTrxRelative = [IO.Path]::GetRelativePath(
                $baselineEvidenceRoot,
                $baselineTrx).Replace('\', '/')
            foreach ($mutation in $projectMutations) {
                $mutation | Add-Member `
                    -NotePropertyName BaselineLedger `
                    -NotePropertyValue $ledger
                $mutation | Add-Member `
                    -NotePropertyName BaselineInvocation `
                    -NotePropertyValue $invocation.Identity
                $mutation | Add-Member -NotePropertyName BaselineTrx `
                    -NotePropertyValue $baselineTrxRelative
            }
            $baselineRows.Add([pscustomobject]@{
                project = [string]$invocation.Project
                filter = [string]$invocation.Filter
                configuration = $Configuration
                invocation = $invocation.Identity
                ledger = $ledger
                trx = $baselineTrxRelative
            })
        }
        if ($BaselineOnly) {
            $baselineParent = Split-Path -Parent $baselineFile
            [IO.Directory]::CreateDirectory($baselineParent) | Out-Null
            $temporaryBaseline = $baselineFile + '.' +
                [Guid]::NewGuid().ToString('N') + '.tmp'
            [pscustomobject]@{
                schemaVersion = 2
                commit = $sourceCommit
                configuration = $Configuration
                selection = $selection
                catalogCount = $catalogCount
                testCount = $baselineRows.Count
                tests = @($baselineRows | Sort-Object project, filter)
                timing = [ordered]@{
                    restoreElapsedMilliseconds = $restoreElapsedMilliseconds
                    baselineElapsedMilliseconds = $baselineElapsedMilliseconds
                    baselineInvocationCount = $baselineInvocationCount
                }
            } | ConvertTo-Json -Depth 7 |
                Set-Content -LiteralPath $temporaryBaseline -Encoding utf8NoBOM
            Move-Item -LiteralPath $temporaryBaseline `
                -Destination $baselineFile -Force
            Write-Host (
                "Recorded $($baselineRows.Count) exact mutation baselines " +
                "from $baselineInvocationCount focused invocations.")
            Write-Host "Baseline evidence: $baselineFile"
            return
        }
    }

    $results = [Collections.Generic.List[object]]::new()
    foreach ($completedResult in $completedResults) {
        $results.Add($completedResult)
    }
    foreach ($mutation in $pendingMutations) {
        $path = Join-Path $sourceRoot $mutation.File
        $originalContent = [IO.File]::ReadAllText($path)
        $mutatedContent = $originalContent.Replace(
            $mutation.Original,
            $mutation.Mutated,
            [StringComparison]::Ordinal)
        try {
            [IO.File]::WriteAllText(
                $path,
                $mutatedContent,
                [Text.UTF8Encoding]::new($false))
            $testTrxName = $mutation.Name + '-test.trx'
            $testTrx = Join-Path $logs $testTrxName
            Remove-Item -LiteralPath $testTrx -Force -ErrorAction SilentlyContinue
            $testRun = Invoke-IsolatedDotnet `
                -Arguments @(
                    'test',
                    $mutation.Project,
                    '-c',
                    $Configuration,
                    '--no-restore',
                    '--filter',
                    $mutation.Filter,
                    '--logger',
                    'console;verbosity=minimal',
                    '--logger',
                    "trx;LogFileName=$testTrxName",
                    '--results-directory',
                    $logs) `
                -LogName ($mutation.Name + '-test.log')
            $testExit = $testRun.ExitCode
            $mutationElapsedMilliseconds += $testRun.ElapsedMilliseconds
            $mutationInvocationCount++
            $mutationTimings.Add([pscustomobject]@{
                name = $mutation.Name
                elapsedMilliseconds = $testRun.ElapsedMilliseconds
            })
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
            if (-not (Test-Path -LiteralPath $testTrx -PathType Leaf)) {
                throw (
                    "Mutation '$($mutation.Name)' did not compile or did " +
                    "not produce test evidence; see " +
                    "$logs\$($mutation.Name)-test.log.")
            }
            $expectedMethodName = $mutation.Filter.Substring(
                'FullyQualifiedName~'.Length)
            $testEvidence = Read-SharpProofMutationTestEvidence `
                -TrxPath $testTrx `
                -EvidenceName $mutation.Name `
                -Mode Mutation `
                -ProcessExitCode $testExit `
                -ExpectedMethodName $expectedMethodName `
                -ExpectedLedger $mutation.BaselineLedger
            $result = [pscustomobject]@{
                name = $mutation.Name
                file = $mutation.File.Replace('\', '/')
                test = $mutation.Filter
                project = $mutation.Project.Replace('\', '/')
                original = $mutation.Original
                mutated = $mutation.Mutated
                killed = $true
                exitCode = $testExit
                executedCount = $testEvidence.executedCount
                failedCount = $testEvidence.failedCount
                assertionFailureCount = $testEvidence.assertionFailureCount
                selectedTests = $testEvidence.testLedger
                baselineInvocation = $mutation.BaselineInvocation
                baselineSelectedTests = @($mutation.BaselineLedger)
                baselineTrx = $mutation.BaselineTrx
                log = "mutation-logs/$runId/$($mutation.Name)-test.log"
                trx = "mutation-logs/$runId/$testTrxName"
            }
            if ($MutationShardCount -gt 1) {
                $result | Add-Member -NotePropertyName catalogOrdinal `
                    -NotePropertyValue ([int]$mutation.CatalogOrdinal)
            }
            $results.Add($result)
            Write-MutationEvidence `
                -Results $results.ToArray() `
                -EvidenceSelection inProgress
        }
        finally {
            [IO.File]::WriteAllText(
                $path,
                $originalContent,
                [Text.UTF8Encoding]::new($false))
        }
    }

    $currentCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    & git -C $repositoryRoot diff --quiet --
    $trackedTreeChanged = $LASTEXITCODE -ne 0
    & git -C $repositoryRoot diff --cached --quiet --
    $trackedIndexChanged = $LASTEXITCODE -ne 0
    if ($currentCommit -ne $sourceCommit -or
        $trackedTreeChanged -or $trackedIndexChanged) {
        throw 'Repository identity changed while mutation evidence was produced.'
    }
    Write-MutationEvidence -Results $results.ToArray() -EvidenceSelection $selection
    Write-Host "Killed $($results.Count) trusted-boundary mutations."
    Write-Host "Evidence: $output"
}
finally {
    if (-not $KeepWorkspace -and
        (Test-Path -LiteralPath $workspace)) {
        $resolvedWorkspace = [IO.Path]::GetFullPath($workspace)
        $resolvedMutationRoot = [IO.Path]::GetFullPath($mutationRoot)
        [void](Resolve-SharpProofContainedPath `
            -Root $resolvedMutationRoot -Path $resolvedWorkspace `
            -ParameterName 'Mutation workspace')
        Remove-Item -LiteralPath $resolvedWorkspace -Recurse -Force
    }
}
