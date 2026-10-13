Set-StrictMode -Version Latest

function Read-SharpProofCanonicalCases {
    param([string]$Root)

    $cases = [Collections.Generic.List[object]]::new()
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.trx' -File)) {
        [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
        $definitions = @{}
        foreach ($definition in $trx.SelectNodes('//*[local-name()="UnitTest"]')) {
            $definitions[[string]$definition.id] = $definition
        }
        $results = @($trx.SelectNodes('//*[local-name()="UnitTestResult"]'))
        $counters = $trx.SelectSingleNode('//*[local-name()="Counters"]')
        if ($null -eq $counters -or $results.Count -eq 0 -or
            $results.Count -ne [int]$counters.total) {
            throw 'Canonical TRX results are empty or differ from their counters.'
        }
        foreach ($result in $results) {
            $definition = $definitions[[string]$result.testId]
            if ($null -eq $definition) { throw 'Canonical TRX result has no definition.' }
            $method = $definition.SelectSingleNode('*[local-name()="TestMethod"]')
            $assembly = [IO.Path]::GetFileName(([string]$method.codeBase).Replace('\', '/'))
            $identity = $assembly + '|' + [string]$result.testId
            if (-not $identities.Add($identity)) { throw 'Canonical TRX test identity is duplicated.' }
            $errorMessage = $result.SelectSingleNode('*[local-name()="Output"]/*[local-name()="ErrorInfo"]/*[local-name()="Message"]')
            $cases.Add([ordered]@{
                assembly = $assembly
                testId = [string]$result.testId
                className = [string]$method.className
                methodName = [string]$method.name
                displayName = [string]$result.testName
                outcome = [string]$result.outcome
                reason = $(if ($null -eq $errorMessage) { '' } else { [string]$errorMessage.InnerText })
                fullyQualifiedName = [string]$method.className + '.' + [string]$method.name
            })
        }
    }
    if ($cases.Count -eq 0) { throw 'Canonical cohort has no actual TRX cases.' }
    return @($cases | Sort-Object assembly, testId)
}

function Read-SharpProofCanonicalDiscovery {
    param([string]$Root)

    $cases = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($dump in @(Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.discovery.dump' -File)) {
        $assembly = $dump.Name.Substring(0, $dump.Name.Length - '.discovery.dump'.Length)
        $taskRoot = Split-Path -Parent $dump.DirectoryName
        $selectedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($name in Get-Content -LiteralPath (Join-Path $taskRoot 'discovered-tests.txt')) {
            [void]$selectedNames.Add([string]$name)
        }
        [xml]$document = Get-Content -LiteralPath $dump.FullName -Raw
        foreach ($test in $document.SelectNodes('//*[local-name()="test-case"]')) {
            $name = [string]$test.fullname
            if (-not $selectedNames.Contains($name)) { continue }
            $categories = @($test.SelectNodes('ancestor-or-self::*/*[local-name()="properties"]/*[local-name()="property"][@name="Category"]') |
                ForEach-Object { [string]$_.value })
            if (@($categories | Where-Object { $_ -in @('Performance', 'Coverage', 'Corpus') }).Count) { continue }
            $id = [string]$test.id
            if ([string]::IsNullOrWhiteSpace($id) -or -not $ids.Add($assembly + '|' + $id)) {
                throw 'Actual NUnit discovery has an absent or duplicated per-case identity.'
            }
            $cases.Add([ordered]@{ assembly = $assembly; nunitId = $id; fullyQualifiedName = $name })
        }
    }
    if ($cases.Count -eq 0) { throw 'Canonical NUnit per-case discovery is empty.' }
    return @($cases | Sort-Object assembly, nunitId)
}

function Assert-SharpProofCanonicalOutcomes {
    param([object[]]$Cases, [string]$Cohort)

    # These six exact identities/reasons were NotExecuted in authenticated
    # bf6b coverage artifact 11832715058; they are existing Explicit fixtures.
    $known = @{
        '5cd4e5c7-9f73-10c1-7c2c-636c241172c5' = 'ControlFlowLongFuzzTests.GeneratedControlFlowClaimsAgreeWithRuntime'
        'b5009c46-3dd2-c61d-cf8e-ec663ae861ca' = 'TypedFeatureLongFuzzTests.GeneratedTypedClaimsAgreeWithClr'
        '4dc99683-82af-ebfd-e344-78b6c2879455' = 'AnalyzerPipelineLongFuzzTests.GeneratedClaimsAgreeWithAnalyzerWorkerAndRuntime'
        '230ff36e-e535-704c-3792-7b29dd55347b' = 'CompositionalLongFuzzTests.GeneratedCompositionsAgreeWithClr'
        '2da18c89-eef0-4b0f-6dbe-375956629323' = 'TypedWidthLongFuzzTests.GeneratedWidthClaimsAgreeWithClrWitnesses'
        '5dbb42c1-950e-a564-1fb7-0c9e9c268eca' = 'ContractMatrixLongFuzzTests.GeneratedContractsAgreeWithClrAdmission'
    }
    $skipped = @($Cases | Where-Object { $_.outcome -cne 'Passed' })
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

    $commit = [string](& git -C $RepositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Canonical source commit could not be read.' }
    $tree = [string](& git -C $RepositoryRoot rev-parse 'HEAD^{tree}')
    if ($LASTEXITCODE -ne 0) { throw 'Canonical source tree could not be read.' }
    if ($env:GITHUB_RUN_ID -notmatch '^\d+$' -or
        $env:GITHUB_RUN_ATTEMPT -notmatch '^[1-9]\d*$' -or
        $commit -cne $env:GITHUB_SHA) {
        throw 'Retained canonical cohort requires the actual GitHub run, attempt, and checked-out SHA.'
    }
    $cases = @(Read-SharpProofCanonicalCases -Root $Root)
    $expectedNames = @($Expected | ForEach-Object { [string]$_.Name } | Sort-Object)
    $completedNames = @($Completed | ForEach-Object { [string]$_.Test.Name } | Sort-Object)
    $discovered = [Collections.Generic.List[string]]::new()
    foreach ($task in $Expected) {
        $discoveryPath = Join-Path $Root ($task.Name + '/discovered-tests.txt')
        if (-not (Test-Path -LiteralPath $discoveryPath -PathType Leaf)) {
            throw 'Canonical task has no actual filtered discovery receipt.'
        }
        foreach ($name in @(Get-Content -LiteralPath $discoveryPath)) {
            if ([string]::IsNullOrWhiteSpace($name)) { throw 'Canonical discovery contains an empty name.' }
            $discovered.Add([string]$name)
        }
    }
    $receipt = [ordered]@{
        schemaVersion = 1
        commit = $commit
        tree = $tree
        runId = $env:GITHUB_RUN_ID
        runAttempt = $env:GITHUB_RUN_ATTEMPT
        cohort = $Cohort
        configuration = $Configuration
        filter = $Filter
        projects = @($Projects | Sort-Object)
        expectedTasks = $expectedNames
        completedTasks = $completedNames
        completed = @($Completed | ForEach-Object {
                [ordered]@{ name = $_.Test.Name; exitCode = $_.ExitCode; elapsedMilliseconds = $_.ElapsedMilliseconds }
            })
        discovered = @($discovered | Sort-Object)
        discoveredCases = @(Read-SharpProofCanonicalDiscovery -Root $Root)
        cases = $cases
    }
    $receipt | ConvertTo-Json -Depth 7 |
        Set-Content -LiteralPath (Join-Path $Root 'canonical-cohort.json') -Encoding utf8NoBOM
}

function Assert-SharpProofCanonicalCohorts {
    param([string]$Root, [string]$RepositoryRoot)

    $canonicalFilter = 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
    $projects = @((Get-Content -LiteralPath (Join-Path $RepositoryRoot 'SharpProof.Semantic.Tests.slnf') -Raw |
            ConvertFrom-Json).solution.projects | ForEach-Object { ([string]$_).Replace('\', '/') })
    $gatesProject = 'SharpProof.Gates.Test/SharpProof.Gates.Test.csproj'
    $commit = [string](& git -C $RepositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Canonical aggregate source commit could not be read.' }
    $tree = [string](& git -C $RepositoryRoot rev-parse 'HEAD^{tree}')
    if ($LASTEXITCODE -ne 0) { throw 'Canonical aggregate source tree could not be read.' }
    $allIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $summary = [Collections.Generic.List[object]]::new()
    foreach ($cohort in @('Remaining', 'Gates')) {
        $cohortRoot = Join-Path $Root $cohort.ToLowerInvariant()
        $receipt = Get-Content -LiteralPath (Join-Path $cohortRoot 'canonical-cohort.json') -Raw | ConvertFrom-Json
        $expectedProjects = @($projects | Where-Object {
                if ($cohort -eq 'Gates') { $_ -ceq $gatesProject }
                else { $_ -cne $gatesProject }
            } | Sort-Object)
        if ([int]$receipt.schemaVersion -ne 1 -or $receipt.cohort -cne $cohort -or
            $receipt.commit -cne $commit -or $receipt.tree -cne $tree -or
            $receipt.commit -cne $env:GITHUB_SHA -or
            $receipt.runId -cne $env:GITHUB_RUN_ID -or
            $receipt.runAttempt -cne $env:GITHUB_RUN_ATTEMPT -or
            $receipt.configuration -cne 'Release' -or $receipt.filter -cne $canonicalFilter -or
            (@($receipt.projects) -join "`n") -cne ($expectedProjects -join "`n")) {
            throw 'Canonical cohort does not match this attempt and the complete project partition.'
        }
        $expectedTasks = if ($cohort -eq 'Gates') { @('semantic-projects') }
        else { @('semantic-projects', 'worker-claim-manifest', 'worker-compiler-manifest', 'worker-core', 'worker-remainder') | Sort-Object }
        if ((@($receipt.expectedTasks) -join "`n") -cne ($expectedTasks -join "`n") -or
            (@($receipt.completedTasks) -join "`n") -cne ($expectedTasks -join "`n") -or
            @($receipt.completed | Where-Object { $_.exitCode -ne 0 }).Count -ne 0) {
            throw 'Canonical cohort did not successfully complete every unchanged task.'
        }
        $cases = @(Read-SharpProofCanonicalCases -Root $cohortRoot)
        $serializedCases = ConvertTo-Json -InputObject $cases -Depth 5 -Compress
        $recordedCases = ConvertTo-Json -InputObject @($receipt.cases) -Depth 5 -Compress
        if ($serializedCases -cne $recordedCases) {
            throw 'Canonical case identities or outcomes differ from their real TRX receipts.'
        }
        Assert-SharpProofCanonicalOutcomes -Cases $cases -Cohort $cohort
        $discovered = @(Get-ChildItem -LiteralPath $cohortRoot -Recurse -Filter 'discovered-tests.txt' -File |
            ForEach-Object { Get-Content -LiteralPath $_.FullName } | Sort-Object)
        if (($discovered -join "`n") -cne (@($receipt.discovered) -join "`n")) {
            throw 'Canonical method-name discovery differs from its retained receipt.'
        }
        $discoveredCases = @(Read-SharpProofCanonicalDiscovery -Root $cohortRoot)
        if ((ConvertTo-Json -InputObject $discoveredCases -Depth 5 -Compress) -cne
            (ConvertTo-Json -InputObject @($receipt.discoveredCases) -Depth 5 -Compress)) {
            throw 'Canonical per-case NUnit identities differ from actual discovery XML.'
        }
        $discoveredNames = @($discoveredCases | ForEach-Object { $_.assembly + '|' + $_.fullyQualifiedName } | Sort-Object)
        $observedNames = @($cases | ForEach-Object { $_.assembly + '|' + $_.fullyQualifiedName } | Sort-Object)
        if (($discoveredNames -join "`n") -cne ($observedNames -join "`n")) {
            throw 'Canonical parameterized case-name multisets differ between actual NUnit discovery and TRX.'
        }
        $requiredAssemblies = @($expectedProjects | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) + '.dll' })
        if ($cohort -eq 'Remaining') { $requiredAssemblies += 'SharpProof.Worker.Test.dll' }
        $observedAssemblies = @($cases | ForEach-Object { $_.assembly } | Sort-Object -Unique)
        if (($observedAssemblies -join "`n") -cne (@($requiredAssemblies | Sort-Object) -join "`n")) {
            throw 'Canonical TRX assembly ownership does not match its complete partition.'
        }
        foreach ($case in $cases) {
            if (-not $allIds.Add($case.assembly + '|' + $case.testId)) {
                throw 'Canonical children executed overlapping test identities.'
            }
        }
        $summary.Add([ordered]@{ cohort = $cohort; projects = $expectedProjects; cases = $cases.Count;
            passed = @($cases | Where-Object { $_.outcome -ceq 'Passed' }).Count;
            notExecuted = @($cases | Where-Object { $_.outcome -ceq 'NotExecuted' }).Count })
    }
    [ordered]@{ schemaVersion = 1; commit = $commit; tree = $tree; runId = $env:GITHUB_RUN_ID;
        runAttempt = $env:GITHUB_RUN_ATTEMPT; cases = $allIds.Count; cohorts = @($summary) } |
        ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $Root 'canonical-cohort-validation.json') -Encoding utf8NoBOM
}
