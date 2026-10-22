Set-StrictMode -Version Latest

function ConvertTo-SharpProofTrxName {
    param([string]$Name)

    # VSTest 18.8.1 XmlPersistence.RemoveInvalidXmlChar serializes UTF-16 code units.
    # Apply its one-way encoding to discovery only; preserve raw receipt names.
    return [regex]::Replace($Name, '[^\x09\x0A\x0D\x20-\uD7FF\uE000-\uFFFD]', {
            param($match)
            '\u' + ([int][char]$match.Value[0]).ToString('x4', [Globalization.CultureInfo]::InvariantCulture)
        })
}

function Get-SharpProofTrxDiscoveryNames {
    param([object[]]$Discovery)
    $discoverySnapshotBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($discoverySnapshotItem in $Discovery) { $discoverySnapshotBuilder.Add($discoverySnapshotItem) }
    $discoverySnapshot = if ($null -eq $Discovery) { $null } else { , $discoverySnapshotBuilder.ToImmutable() }


    $rawNames = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, string]([StringComparer]::Ordinal)
    foreach ($case in $discoverySnapshot) {
        $rawName = $case.assembly + '|' + $case.fullyQualifiedName
        $encodedName = $case.assembly + '|' + (ConvertTo-SharpProofTrxName -Name $case.fullyQualifiedName)
        if ($rawNames.ContainsKey($encodedName) -and $rawNames[$encodedName] -cne $rawName) {
            throw 'Distinct NUnit case names collide under the actual TRX serializer encoding.'
        }
        $rawNames[$encodedName] = $rawName
        $encodedName
    }
}

function Read-SharpProofCanonicalCases {
    param([string]$Root)

    $cases = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $identities = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.trx' -File)) {
        [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
        $definitionBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::OrdinalIgnoreCase)
        foreach ($definition in $trx.SelectNodes('//*[local-name()="UnitTest"]')) {
            $definitionId = [string]$definition.id
            if ([string]::IsNullOrWhiteSpace($definitionId) -or $definitionBuilder.ContainsKey($definitionId)) {
                throw 'Canonical TRX definition has an absent or duplicated test identity.'
            }
            $definitionBuilder.Add($definitionId, $definition)
        }
        $definitions = $definitionBuilder.ToImmutable()
        $results = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($trx.SelectNodes('//*[local-name()="UnitTestResult"]')))
        $counters = $trx.SelectSingleNode('//*[local-name()="Counters"]')
        if ($null -eq $counters -or $results.Count -eq 0 -or
            $results.Count -ne [int]$counters.total) {
            throw 'Canonical TRX results are empty or differ from their counters.'
        }
        foreach ($result in $results) {
            $execution = [string]$result.executionId
            $definition = if ($definitions.ContainsKey([string]$result.testId)) { $definitions[[string]$result.testId] } else { $null }
            if ($null -eq $definition -or [string]$definition.id -cne [string]$result.testId) {
                throw 'Canonical TRX result has no matching execution definition.'
            }
            $method = $definition.SelectSingleNode('*[local-name()="TestMethod"]')
            $assembly = [IO.Path]::GetFileName(([string]$method.codeBase).Replace('\', '/'))
            $framework = [IO.Path]::GetFileName([IO.Path]::GetDirectoryName(([string]$method.codeBase).Replace('\', '/')))
            $relativeTrx = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
            $task = $relativeTrx.Split('/')[0]
            $identity = $assembly + '|' + $framework + '|' + $task + '|' + $execution
            if ([string]::IsNullOrWhiteSpace($execution) -or -not $identities.Add($identity)) {
                throw 'Canonical TRX execution identity is absent or duplicated.'
            }
            $errorMessage = $result.SelectSingleNode('*[local-name()="Output"]/*[local-name()="ErrorInfo"]/*[local-name()="Message"]')
            $cases.Add(([ordered]@{
                        assembly           = $assembly
                        testId             = [string]$result.testId
                        executionId        = $execution
                        framework          = $framework
                        task               = $task
                        trx                = $relativeTrx
                        className          = [string]$method.className
                        methodName         = [string]$method.name
                        displayName        = [string]$result.testName
                        outcome            = [string]$result.outcome
                        reason             = $(if ($null -eq $errorMessage) { '' } else { [string]$errorMessage.InnerText })
                        fullyQualifiedName = [string]$method.className + '.' + [string]$method.name
                    }).AsReadOnly())
        }
    }
    if ($cases.Count -eq 0) { throw 'Canonical cohort has no actual TRX cases.' }
    return [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($cases.ToImmutable() | Sort-Object { $_.assembly }, { $_.testId }, { $_.executionId }))
}

function Assert-SharpProofHistoricalCaseAliases {
    param([object[]]$Cases, [object[]]$Discovery)
    $casesSnapshotBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($casesSnapshotItem in $Cases) { $casesSnapshotBuilder.Add($casesSnapshotItem) }
    $casesSnapshot = if ($null -eq $Cases) { $null } else { , $casesSnapshotBuilder.ToImmutable() }

    $discoverySnapshotBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($discoverySnapshotItem in $Discovery) { $discoverySnapshotBuilder.Add($discoverySnapshotItem) }
    $discoverySnapshot = if ($null -eq $Discovery) { $null } else { , $discoverySnapshotBuilder.ToImmutable() }


    # These two GUID collisions and twofold name multiplicities occur in
    # authenticated bf6b and 32b Frontend TRX. Distinct inputs share SetName.
    $knownBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, string]([StringComparer]::OrdinalIgnoreCase)
    $knownBuilder.Add('06d903a7-4bce-3d34-b913-60c489b39f2a', 'SharpProof.Frontend.Test.TypedReferenceProgramLoweringTests.Reference observations: int Target(string x) => x is not null ? x.Length : 0; #0')
    $knownBuilder.Add('1b1e9d07-66cf-197c-b1d4-b3710a68a33b', 'SharpProof.Frontend.Test.TypedReferenceProgramLoweringTests.Reference observations: bool Target(object x) => x is null or not null; #True')
    $known = $knownBuilder.ToImmutable()
    foreach ($group in @($casesSnapshot | Group-Object { $_.assembly + '|' + $_.testId } | Where-Object { $_.Count -gt 1 })) {
        $case = $group.Group[0]
        if ($group.Count -ne 2 -or -not $known.ContainsKey($case.testId) -or
            @($group.Group | Where-Object {
                    $_.assembly -cne 'SharpProof.Frontend.Test.dll' -or $_.framework -cne 'net8.0' -or
                    $_.task -cne 'semantic-projects' -or $_.outcome -cne 'Passed' -or
                    $_.fullyQualifiedName -cne $known[$case.testId]
                }).Count -ne 0 -or
            @($group.Group | ForEach-Object { $_.trx } | Select-Object -Unique).Count -ne 1 -or
            @($group.Group | ForEach-Object { $_.executionId } | Select-Object -Unique).Count -ne 2) {
            throw 'Canonical TRX has an unintended duplicate test GUID.'
        }
        $discovered = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($discoverySnapshot | Where-Object {
                    $_.assembly -ceq $case.assembly -and $_.fullyQualifiedName -ceq $case.fullyQualifiedName
                }))
        if ($discovered.Count -ne 2 -or @($discovered | ForEach-Object { $_.nunitId } | Select-Object -Unique).Count -ne 2) {
            throw 'Historical TRX name alias lacks two distinct actual NUnit discovery identities.'
        }
    }
}

function Read-SharpProofCanonicalDiscovery {
    param([string]$Root, [string[]]$ExcludedCategories = @('Performance', 'Coverage', 'Corpus'))

    $cases = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    $ids = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    foreach ($dump in @(Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.discovery.dump' -File)) {
        $assembly = $dump.Name.Substring(0, $dump.Name.Length - '.discovery.dump'.Length)
        $taskRoot = Split-Path -Parent $dump.DirectoryName
        $selectedNames = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
        foreach ($name in Get-Content -LiteralPath (Join-Path $taskRoot 'discovered-tests.txt')) {
            [void]$selectedNames.Add([string]$name)
        }
        $selectedNames = $selectedNames.ToImmutable()
        [xml]$document = Get-Content -LiteralPath $dump.FullName -Raw
        foreach ($test in $document.SelectNodes('//*[local-name()="test-case"]')) {
            $name = [string]$test.fullname
            if (-not $selectedNames.Contains($name)) { continue }
            $categories = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($test.SelectNodes('ancestor-or-self::*/*[local-name()="properties"]/*[local-name()="property"][@name="Category"]') |
                        ForEach-Object { [string]$_.value }))
            if (@($categories | Where-Object { $_ -in $ExcludedCategories }).Count) { continue }
            $id = [string]$test.id
            if ([string]::IsNullOrWhiteSpace($id) -or -not $ids.Add($assembly + '|' + $id)) {
                throw 'Actual NUnit discovery has an absent or duplicated per-case identity.'
            }
            $cases.Add(([ordered]@{ assembly = $assembly; nunitId = $id; fullyQualifiedName = $name }).AsReadOnly())
        }
    }
    if ($cases.Count -eq 0) { throw 'Canonical NUnit per-case discovery is empty.' }
    return [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($cases.ToImmutable() | Sort-Object { $_.assembly }, { $_.nunitId }))
}

function Assert-SharpProofCanonicalOutcomes {
    param([object[]]$Cases, [string]$Cohort)
    $casesSnapshotBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($casesSnapshotItem in $Cases) { $casesSnapshotBuilder.Add($casesSnapshotItem) }
    $casesSnapshot = if ($null -eq $Cases) { $null } else { , $casesSnapshotBuilder.ToImmutable() }


    # These six exact identities/reasons were NotExecuted in authenticated
    # bf6b coverage artifact 11832715058; they are existing Explicit fixtures.
    $knownBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, string]([StringComparer]::OrdinalIgnoreCase)
    $knownBuilder.Add('5cd4e5c7-9f73-10c1-7c2c-636c241172c5', 'ControlFlowLongFuzzTests.GeneratedControlFlowClaimsAgreeWithRuntime')
    $knownBuilder.Add('b5009c46-3dd2-c61d-cf8e-ec663ae861ca', 'TypedFeatureLongFuzzTests.GeneratedTypedClaimsAgreeWithClr')
    $knownBuilder.Add('4dc99683-82af-ebfd-e344-78b6c2879455', 'AnalyzerPipelineLongFuzzTests.GeneratedClaimsAgreeWithAnalyzerWorkerAndRuntime')
    $knownBuilder.Add('230ff36e-e535-704c-3792-7b29dd55347b', 'CompositionalLongFuzzTests.GeneratedCompositionsAgreeWithClr')
    $knownBuilder.Add('2da18c89-eef0-4b0f-6dbe-375956629323', 'TypedWidthLongFuzzTests.GeneratedWidthClaimsAgreeWithClrWitnesses')
    $knownBuilder.Add('5dbb42c1-950e-a564-1fb7-0c9e9c268eca', 'ContractMatrixLongFuzzTests.GeneratedContractsAgreeWithClrAdmission')
    $known = $knownBuilder.ToImmutable()
    $skipped = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($casesSnapshot | Where-Object { $_.outcome -cne 'Passed' }))
    if ($Cohort -eq 'Gates' -and $skipped.Count) { throw 'Canonical Gates has an unsuccessful case.' }
    if ($Cohort -eq 'Remaining' -and $skipped.Count -ne $known.Count) {
        throw 'Canonical non-Gates skip census differs from the existing six Explicit fixtures.'
    }
    foreach ($case in $skipped) {
        if ($case.outcome -cne 'NotExecuted' -or $case.assembly -cne 'SharpProof.Worker.Test.dll' -or
            -not $known.ContainsKey($case.testId) -or
            $case.fullyQualifiedName -cne ('SharpProof.Worker.Test.LongFuzz.' + $known[$case.testId]) -or
            $case.reason -cne 'OneTimeSetUp: Long-running fuzz campaign; select with TestCategory=LongFuzz.') {
            throw 'Canonical case is not an exact existing Explicit skip with its actual reason.'
        }
    }
}

function Save-SharpProofCanonicalCohort {
    param(
        [string]$Root, [string]$RepositoryRoot, [string]$Cohort,
        [string]$Configuration, [string]$Filter, [string[]]$Projects,
        [object[]]$Expected, [object[]]$Completed)
    $projectsSnapshotBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($projectsSnapshotItem in $Projects) { $projectsSnapshotBuilder.Add($projectsSnapshotItem) }
    $projectsSnapshot = if ($null -eq $Projects) { $null } else { , $projectsSnapshotBuilder.ToImmutable() }

    $expectedSnapshotBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($expectedSnapshotItem in $Expected) { $expectedSnapshotBuilder.Add($expectedSnapshotItem) }
    $expectedSnapshot = if ($null -eq $Expected) { $null } else { , $expectedSnapshotBuilder.ToImmutable() }

    $completedSnapshotBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($completedSnapshotItem in $Completed) { $completedSnapshotBuilder.Add($completedSnapshotItem) }
    $completedSnapshot = if ($null -eq $Completed) { $null } else { , $completedSnapshotBuilder.ToImmutable() }


    $commit = [string](& git -C $RepositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Canonical source commit could not be read.' }
    $tree = [string](& git -C $RepositoryRoot rev-parse 'HEAD^{tree}')
    if ($LASTEXITCODE -ne 0) { throw 'Canonical source tree could not be read.' }
    if ($env:GITHUB_RUN_ID -notmatch '^\d+$' -or
        $env:GITHUB_RUN_ATTEMPT -notmatch '^[1-9]\d*$' -or
        $commit -cne $env:GITHUB_SHA) {
        throw 'Retained canonical cohort requires the actual GitHub run, attempt, and checked-out SHA.'
    }
    $cases = [Collections.Immutable.ImmutableArray]::CreateRange[object](@(Read-SharpProofCanonicalCases -Root $Root))
    $discoveredCases = [Collections.Immutable.ImmutableArray]::CreateRange[object](@(Read-SharpProofCanonicalDiscovery -Root $Root))
    Assert-SharpProofHistoricalCaseAliases -Cases $cases -Discovery $discoveredCases
    $expectedNames = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($expectedSnapshot | ForEach-Object { [string]$_.Name } | Sort-Object))
    $completedNames = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($completedSnapshot | ForEach-Object { [string]$_.Test.Name } | Sort-Object))
    $discovered = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($task in $expectedSnapshot) {
        $discoveryPath = Join-Path $Root ($task.Name + '/discovered-tests.txt')
        if (-not (Test-Path -LiteralPath $discoveryPath -PathType Leaf)) {
            throw 'Canonical task has no actual filtered discovery receipt.'
        }
        foreach ($name in @(Get-Content -LiteralPath $discoveryPath)) {
            if ([string]::IsNullOrWhiteSpace($name)) { throw 'Canonical discovery contains an empty name.' }
            $discovered.Add([string]$name)
        }
    }
    $discovered.Sort([StringComparer]::Ordinal)
    $receipt = ([ordered]@{
            schemaVersion   = 1
            commit          = $commit
            tree            = $tree
            runId           = $env:GITHUB_RUN_ID
            runAttempt      = $env:GITHUB_RUN_ATTEMPT
            cohort          = $Cohort
            configuration   = $Configuration
            filter          = $Filter
            projects        = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($projectsSnapshot | Sort-Object))
            expectedTasks   = $expectedNames
            completedTasks  = $completedNames
            completed       = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@($completedSnapshot | ForEach-Object {
                        ([ordered]@{ name = $_.Test.Name; exitCode = $_.ExitCode; elapsedMilliseconds = $_.ElapsedMilliseconds }).AsReadOnly()
                    }))
            discovered      = $discovered.ToImmutable()
            discoveredCases = $discoveredCases
            cases           = $cases
        }).AsReadOnly()
    $receipt | ConvertTo-Json -Depth 7 |
        Set-Content -LiteralPath (Join-Path $Root 'canonical-cohort.json') -Encoding utf8NoBOM
}

# CI may run one cohort as several parts on separate runners. Each part's
# receipt sits beside the merged task directories as canonical-cohort.<part>.json.
# The parts must share their run, source, and selection; their task, discovery,
# and case lists merge into one receipt that the checks below treat as whole.
function Read-SharpProofCanonicalReceipt {
    param([string]$CohortRoot, [Collections.IDictionary]$ExpectedAttempts, [string]$CurrentAttempt = $env:GITHUB_RUN_ATTEMPT)

    if ($null -ne $ExpectedAttempts) {
        $files = @(Get-ChildItem -LiteralPath $CohortRoot -File -Filter 'canonical-cohort*.json')
        if ($CurrentAttempt -notmatch '^[1-9]\d*$' -or $ExpectedAttempts.Count -ne $files.Count) {
            throw 'Canonical artifact attempts do not identify every receipt.'
        }
        foreach ($file in $files) {
            $part = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $attempt = [string]$ExpectedAttempts[$file.Name]
            if (-not $ExpectedAttempts.Contains($file.Name) -or $attempt -notmatch '^[1-9]\d*$' -or
                [long]$attempt -gt [long]$CurrentAttempt -or [string]$part.runAttempt -cne $attempt) {
                throw 'Canonical receipt differs from its successful producer attempt.'
            }
        }
    }

    $receipts = [Collections.Immutable.ImmutableArray]::CreateRange[object](@(Get-ChildItem -LiteralPath $CohortRoot -File -Filter 'canonical-cohort*.json' |
                Sort-Object Name | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json }))
    if ($receipts.Length -eq 0) { throw 'Canonical cohort has no receipt.' }
    if ($receipts.Length -eq 1) { return $receipts[0] }
    $first = $receipts[0]
    foreach ($part in $receipts) {
        foreach ($name in @('schemaVersion', 'commit', 'tree', 'runId', 'runAttempt', 'cohort', 'configuration', 'filter')) {
            if (($name -cne 'runAttempt' -or $null -eq $ExpectedAttempts) -and [string]$part.$name -cne [string]$first.$name) {
                throw 'Canonical cohort parts differ in their run, source, or selection.'
            }
        }
        if ((@($part.projects) -join "`n") -cne (@($first.projects) -join "`n")) {
            throw 'Canonical cohort parts differ in their run, source, or selection.'
        }
    }
    $discovered = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    foreach ($part in $receipts) { $discovered.AddRange([string[]]@($part.discovered)) }
    $discovered.Sort([StringComparer]::Ordinal)
    return ([ordered]@{
            schemaVersion   = $first.schemaVersion
            commit          = $first.commit
            tree            = $first.tree
            runId           = $first.runId
            runAttempt      = $first.runAttempt
            cohort          = $first.cohort
            configuration   = $first.configuration
            filter          = $first.filter
            projects        = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($first.projects))
            expectedTasks   = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($receipts | ForEach-Object { $_.expectedTasks } | Sort-Object))
            completedTasks  = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($receipts | ForEach-Object { $_.completedTasks } | Sort-Object))
            completed       = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($receipts | ForEach-Object { $_.completed }))
            discovered      = $discovered.ToImmutable()
            discoveredCases = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($receipts | ForEach-Object { $_.discoveredCases }))
            cases           = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($receipts | ForEach-Object { $_.cases }))
        }).AsReadOnly()
}

function Assert-SharpProofCanonicalCohorts {
    param([string]$Root, [string]$RepositoryRoot, [Collections.IDictionary]$ArtifactAttempts)

    $canonicalFilter = 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
    $projects = [Collections.Immutable.ImmutableArray]::CreateRange[object](@((Get-Content -LiteralPath (Join-Path $RepositoryRoot 'SharpProof.Semantic.Tests.slnf') -Raw |
                    ConvertFrom-Json).solution.projects | ForEach-Object { ([string]$_).Replace('\', '/') }))
    $gatesProject = 'src/SharpProof.Gates.Tests/SharpProof.Gates.Tests.csproj'
    $commit = [string](& git -C $RepositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Canonical aggregate source commit could not be read.' }
    $tree = [string](& git -C $RepositoryRoot rev-parse 'HEAD^{tree}')
    if ($LASTEXITCODE -ne 0) { throw 'Canonical aggregate source tree could not be read.' }
    $allIds = [Collections.Immutable.ImmutableHashSet]::CreateBuilder[string]([StringComparer]::Ordinal)
    $testOwners = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, string]([StringComparer]::OrdinalIgnoreCase)
    $summary = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($cohort in @('Remaining', 'Gates')) {
        $cohortRoot = Join-Path $Root $cohort.ToLowerInvariant()
        $expectedAttempts = $null
        if ($null -ne $ArtifactAttempts) {
            if ($ArtifactAttempts.Count -ne 2 -or -not $ArtifactAttempts.Contains($cohort)) {
                throw 'Canonical artifact attempts do not identify both cohorts.'
            }
            $expectedAttempts = $ArtifactAttempts[$cohort]
        }
        $receipt = Read-SharpProofCanonicalReceipt -CohortRoot $cohortRoot -ExpectedAttempts $expectedAttempts
        $expectedProjects = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($projects | Where-Object {
                    if ($cohort -eq 'Gates') { $_ -ceq $gatesProject }
                    else { $_ -cne $gatesProject }
                } | Sort-Object))
        if ([int]$receipt.schemaVersion -ne 1 -or $receipt.cohort -cne $cohort -or
            $receipt.commit -cne $commit -or $receipt.tree -cne $tree -or
            $receipt.commit -cne $env:GITHUB_SHA -or
            $receipt.runId -cne $env:GITHUB_RUN_ID -or
            ($null -eq $expectedAttempts -and $receipt.runAttempt -cne $env:GITHUB_RUN_ATTEMPT) -or
            $receipt.configuration -cne 'Release' -or $receipt.filter -cne $canonicalFilter -or
            (@($receipt.projects) -join "`n") -cne ($expectedProjects -join "`n")) {
            throw 'Canonical cohort does not match this attempt and the complete project partition.'
        }
        $expectedTasks = if ($cohort -eq 'Gates') {
            , [Collections.Immutable.ImmutableArray]::Create[string]('gates-corpus', 'gates-remainder-1', 'gates-remainder-2', 'gates-remainder-3')
        }
        else {
            , [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
                    [Collections.Immutable.ImmutableArray]::Create[string](
                        'semantic-projects', 'worker-claim-manifest', 'worker-compiler-manifest', 'worker-core',
                        'worker-remainder-1', 'worker-remainder-2', 'worker-remainder-3', 'worker-remainder-4') | Sort-Object))
        }
        if ((@($receipt.expectedTasks) -join "`n") -cne ($expectedTasks -join "`n") -or
            (@($receipt.completedTasks) -join "`n") -cne ($expectedTasks -join "`n") -or
            @($receipt.completed | Where-Object { $_.exitCode -ne 0 }).Count -ne 0) {
            throw 'Canonical cohort did not successfully complete every unchanged task.'
        }
        $cases = [Collections.Immutable.ImmutableArray]::CreateRange[object](@(Read-SharpProofCanonicalCases -Root $cohortRoot))
        $serializedCases = ConvertTo-Json -InputObject @($cases) -Depth 5 -Compress
        $recordedCases = ConvertTo-Json -InputObject @($receipt.cases | Sort-Object { $_.assembly }, { $_.testId }, { $_.executionId }) -Depth 5 -Compress
        if ($serializedCases -cne $recordedCases) {
            throw 'Canonical case identities or outcomes differ from their real TRX receipts.'
        }
        Assert-SharpProofCanonicalOutcomes -Cases $cases -Cohort $cohort
        $discoveredBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $discoveredBuilder.AddRange([string[]]@(Get-ChildItem -LiteralPath $cohortRoot -Recurse -Filter 'discovered-tests.txt' -File |
                    ForEach-Object { Get-Content -LiteralPath $_.FullName }))
        $discoveredBuilder.Sort([StringComparer]::Ordinal)
        $discovered = $discoveredBuilder.ToImmutable()
        if (($discovered -join "`n") -cne (@($receipt.discovered) -join "`n")) {
            throw 'Canonical method-name discovery differs from its retained receipt.'
        }
        $discoveredCases = [Collections.Immutable.ImmutableArray]::CreateRange[object](@(Read-SharpProofCanonicalDiscovery -Root $cohortRoot))
        Assert-SharpProofHistoricalCaseAliases -Cases $cases -Discovery $discoveredCases
        if ((ConvertTo-Json -InputObject @($discoveredCases) -Depth 5 -Compress) -cne
            (ConvertTo-Json -InputObject @($receipt.discoveredCases | Sort-Object { $_.assembly }, { $_.nunitId }) -Depth 5 -Compress)) {
            throw 'Canonical per-case NUnit identities differ from actual discovery XML.'
        }
        $discoveredNameBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $discoveredNameBuilder.AddRange([string[]]@(Get-SharpProofTrxDiscoveryNames -Discovery $discoveredCases))
        $observedNameBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $observedNameBuilder.AddRange([string[]]@($cases | ForEach-Object { $_.assembly + '|' + $_.fullyQualifiedName }))
        $discoveredNameBuilder.Sort([StringComparer]::Ordinal)
        $observedNameBuilder.Sort([StringComparer]::Ordinal)
        $discoveredNames = $discoveredNameBuilder.ToImmutable()
        $observedNames = $observedNameBuilder.ToImmutable()
        if (($discoveredNames -join "`n") -cne ($observedNames -join "`n")) {
            throw 'Canonical parameterized case-name multisets differ between actual NUnit discovery and TRX.'
        }
        $requiredAssemblyBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
        $requiredAssemblyBuilder.AddRange([string[]]@($expectedProjects | ForEach-Object {
                    [xml]$projectDocument = Get-Content -LiteralPath (Join-Path $RepositoryRoot $_) -Raw
                    $declaredName = $projectDocument.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
                    $assemblyName = if ($null -eq $declaredName) { [IO.Path]::GetFileNameWithoutExtension($_) } else { [string]$declaredName.InnerText }
                    if ([string]::IsNullOrWhiteSpace($assemblyName) -or $assemblyName.Contains('$(')) {
                        throw "Canonical project has an unresolved assembly identity: $_"
                    }
                    $assemblyName + '.dll'
                }))
        if ($cohort -eq 'Remaining') { $requiredAssemblyBuilder.Add('SharpProof.Worker.Test.dll') }
        $requiredAssemblies = $requiredAssemblyBuilder.ToImmutable()
        $observedAssemblies = [Collections.Immutable.ImmutableArray]::CreateRange[object](@($cases | ForEach-Object { $_.assembly } | Sort-Object -Unique))
        if (($observedAssemblies -join "`n") -cne (@($requiredAssemblies | Sort-Object) -join "`n")) {
            throw 'Canonical TRX assembly ownership does not match its complete partition.'
        }
        foreach ($case in $cases) {
            $testIdentity = $case.assembly + '|' + $case.testId
            if ($testOwners.ContainsKey($testIdentity) -and $testOwners[$testIdentity] -cne $cohort) {
                throw 'Canonical children have overlapping test ownership.'
            }
            $testOwners[$testIdentity] = $cohort
            if (-not $allIds.Add($case.assembly + '|' + $case.framework + '|' + $case.task + '|' + $case.executionId)) {
                throw 'Canonical children executed overlapping execution identities.'
            }
        }
        $summary.Add(([ordered]@{ cohort = $cohort; projects = $expectedProjects; cases = $cases.Count;
                    passed      = @($cases | Where-Object { $_.outcome -ceq 'Passed' }).Count;
                    notExecuted = @($cases | Where-Object { $_.outcome -ceq 'NotExecuted' }).Count
                }).AsReadOnly())
    }
    ([ordered]@{ schemaVersion = 1; commit = $commit; tree = $tree; runId = $env:GITHUB_RUN_ID;
        runAttempt = $env:GITHUB_RUN_ATTEMPT; artifactAttempts = $ArtifactAttempts; cases = $allIds.Count; cohorts = $summary.ToImmutable()
    }).AsReadOnly() |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $Root 'canonical-cohort-validation.json') -Encoding utf8NoBOM
}
