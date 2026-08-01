Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RequiredIntegerAttribute {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$Node,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $text = $Node.GetAttribute($Name)
    $value = 0
    if ([string]::IsNullOrWhiteSpace($text) -or
        -not [int]::TryParse($text, [ref]$value) -or
        $value -lt 0) {
        throw "TRX $Context has an invalid '$Name' counter."
    }

    return $value
}

function Test-NUnitAssertionMessage {
    param(
        [AllowNull()]
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return $false
    }

    $normalized = $Message.Replace("`r`n", "`n").Trim()
    if ($normalized -match '^Assert\.That\(') {
        $scalarFailure = $normalized -match '(?m)^\s*Expected:' -and
            $normalized -match '(?m)^\s*But was:'
        $collectionFailure =
            $normalized -match '(?m)^\s*Expected is\b' -and
            $normalized -match '(?m)\bactual is\b'
        return $scalarFailure -or $collectionFailure
    }

    if (-not $normalized.StartsWith(
            'Multiple failures or warnings in test:',
            [StringComparison]::Ordinal)) {
        return $false
    }

    $blocks = [regex]::Matches(
        $normalized,
        '(?ms)^\s*\d+\)\s+Assert\.That\(.*?(?=^\s*\d+\)|\z)')
    if ($blocks.Count -eq 0) {
        return $false
    }

    $withoutBlocks = [regex]::Replace(
        $normalized,
        '(?ms)^\s*\d+\)\s+Assert\.That\(.*?(?=^\s*\d+\)|\z)',
        '')
    $withoutHeader = $withoutBlocks.Replace(
        'Multiple failures or warnings in test:',
        '').Trim()
    if ($withoutHeader.Length -ne 0) {
        return $false
    }

    return @($blocks | Where-Object {
            $_.Value -notmatch '(?m)^\s*Expected:' -or
            $_.Value -notmatch '(?m)^\s*But was:'
        }).Count -eq 0
}

function Read-SharpProofMutationTestEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TrxPath,

        [Parameter(Mandatory = $true)]
        [string]$EvidenceName,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Baseline', 'Mutation')]
        [string]$Mode,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedMethodName,

        [AllowNull()]
        [string[]]$ExpectedLedger
    )

    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
        throw "Test evidence for '$EvidenceName' was not produced: $TrxPath"
    }

    try {
        [xml]$document = [IO.File]::ReadAllText($TrxPath)
    }
    catch {
        throw "Test evidence for '$EvidenceName' is not valid XML: $($_.Exception.Message)"
    }

    $trxNamespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
    if ($null -eq $document.DocumentElement -or
        $document.DocumentElement.LocalName -ne 'TestRun' -or
        $document.DocumentElement.NamespaceURI -ne $trxNamespace) {
        throw "Test evidence for '$EvidenceName' is not a supported TRX document."
    }

    $summaries = @($document.SelectNodes(
            "//*[local-name()='ResultSummary']"))
    if ($summaries.Count -ne 1) {
        throw "TRX for '$EvidenceName' must contain exactly one result summary."
    }
    $counterNodes = @($summaries[0].SelectNodes(
            "*[local-name()='Counters']"))
    if ($counterNodes.Count -ne 1) {
        throw "TRX counters are missing or duplicated for '$EvidenceName'."
    }

    $counters = $counterNodes[0]
    $counts = @{}
    foreach ($name in @(
            'total', 'executed', 'passed', 'failed', 'error', 'timeout',
            'aborted', 'inconclusive', 'notRunnable', 'notExecuted',
            'disconnected', 'warning', 'completed', 'inProgress', 'pending',
            'passedButRunAborted')) {
        $counts[$name] = Get-RequiredIntegerAttribute `
            -Node $counters `
            -Name $name `
            -Context "for '$EvidenceName'"
    }

    foreach ($name in @(
            'error', 'timeout', 'aborted', 'inconclusive', 'notRunnable',
            'notExecuted', 'disconnected', 'warning', 'completed',
            'inProgress', 'pending', 'passedButRunAborted')) {
        if ($counts[$name] -ne 0) {
            throw "TRX for '$EvidenceName' has non-test outcome '$name'."
        }
    }

    $definitions = @($document.SelectNodes(
            "//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))
    $entries = @($document.SelectNodes(
            "//*[local-name()='TestEntries']/*[local-name()='TestEntry']"))
    $results = @($document.SelectNodes(
            "//*[local-name()='Results']/*[local-name()='UnitTestResult']"))
    $structuralNodes = @($summaries + $counterNodes + $definitions +
        $entries + $results)
    if (@($structuralNodes | Where-Object {
                $_.NamespaceURI -ne $trxNamespace
            }).Count -ne 0) {
        throw "TRX for '$EvidenceName' mixes unsupported XML namespaces."
    }
    if ($results.Count -eq 0 -or
        $definitions.Count -ne $results.Count -or
        $entries.Count -ne $results.Count) {
        throw "TRX for '$EvidenceName' has incomplete test identity records."
    }
    if ($counts.total -ne $results.Count -or
        $counts.executed -ne $results.Count -or
        $counts.passed + $counts.failed -ne $results.Count) {
        throw "TRX counters disagree with executed results for '$EvidenceName'."
    }

    $definitionsById = @{}
    foreach ($definition in $definitions) {
        $testId = $definition.GetAttribute('id')
        $execution = $definition.SelectSingleNode("*[local-name()='Execution']")
        $method = $definition.SelectSingleNode("*[local-name()='TestMethod']")
        if ([string]::IsNullOrWhiteSpace($testId) -or
            $null -eq $execution -or $null -eq $method -or
            $definitionsById.ContainsKey($testId)) {
            throw "TRX for '$EvidenceName' has malformed test definitions."
        }
        $definitionsById[$testId] = [pscustomobject]@{
            executionId = $execution.GetAttribute('id')
            className = $method.GetAttribute('className')
            methodName = $method.GetAttribute('name')
            displayName = $definition.GetAttribute('name')
        }
    }

    $entriesById = @{}
    foreach ($entry in $entries) {
        $testId = $entry.GetAttribute('testId')
        if ([string]::IsNullOrWhiteSpace($testId) -or
            $entriesById.ContainsKey($testId)) {
            throw "TRX for '$EvidenceName' has duplicate or missing test entries."
        }
        $entriesById[$testId] = $entry.GetAttribute('executionId')
    }

    $ledger = @()
    $failedResults = @()
    $seenResults = @{}
    foreach ($result in $results) {
        $testId = $result.GetAttribute('testId')
        $executionId = $result.GetAttribute('executionId')
        if (-not $definitionsById.ContainsKey($testId) -or
            -not $entriesById.ContainsKey($testId) -or
            $seenResults.ContainsKey($testId)) {
            throw "TRX for '$EvidenceName' has unresolved or duplicate results."
        }
        $seenResults[$testId] = $true
        $definition = $definitionsById[$testId]
        if ([string]::IsNullOrWhiteSpace($executionId) -or
            $executionId -ne $definition.executionId -or
            $executionId -ne $entriesById[$testId] -or
            $definition.methodName -ne $ExpectedMethodName -or
            [string]::IsNullOrWhiteSpace($definition.className) -or
            $result.GetAttribute('testName') -ne $definition.displayName) {
            throw "TRX test identity does not match '$ExpectedMethodName' for '$EvidenceName'."
        }

        $identity = $definition.className + '.' + $definition.methodName +
            '|' + $definition.displayName
        $ledger += $identity
        $outcome = $result.GetAttribute('outcome')
        if ($outcome -eq 'Failed') {
            $failedResults += $result
        }
        elseif ($outcome -ne 'Passed') {
            throw "TRX for '$EvidenceName' has unsupported result '$outcome'."
        }
    }

    $ledger = @($ledger | Sort-Object -Unique)
    if ($ledger.Count -ne $results.Count) {
        throw "TRX for '$EvidenceName' has duplicate stable test identities."
    }
    if ($null -ne $ExpectedLedger) {
        $expected = @($ExpectedLedger | Sort-Object -Unique)
        if ($expected.Count -ne @($ExpectedLedger).Count -or
            $ledger.Count -ne $expected.Count -or
            @(Compare-Object $expected $ledger).Count -ne 0) {
            throw "TRX test ledger changed for '$EvidenceName'."
        }
    }

    if ($counts.failed -ne $failedResults.Count -or
        $counts.passed -ne $results.Count - $failedResults.Count) {
        throw "TRX pass/fail counters disagree with results for '$EvidenceName'."
    }

    $expectedSummary = if ($failedResults.Count -eq 0) { 'Completed' } else { 'Failed' }
    if ($summaries[0].GetAttribute('outcome') -ne $expectedSummary) {
        throw "TRX summary outcome disagrees with results for '$EvidenceName'."
    }
    if ($Mode -eq 'Baseline' -and $failedResults.Count -ne 0) {
        throw "Baseline for '$EvidenceName' contains failures."
    }
    if ($Mode -eq 'Mutation' -and $failedResults.Count -eq 0) {
        throw "Mutation '$EvidenceName' has no failing test result."
    }

    $assertionFailures = @($failedResults | Where-Object {
            $message = $_.SelectSingleNode(
                "*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
            $null -ne $message -and
                (Test-NUnitAssertionMessage -Message $message.InnerText)
        })
    if ($Mode -eq 'Mutation' -and
        $assertionFailures.Count -ne $failedResults.Count) {
        throw "Mutation '$EvidenceName' was not killed solely by assertions."
    }

    return [pscustomobject]@{
        executedCount = $results.Count
        failedCount = $failedResults.Count
        assertionFailureCount = $assertionFailures.Count
        testLedger = $ledger
    }
}

Export-ModuleMember -Function Read-SharpProofMutationTestEvidence
