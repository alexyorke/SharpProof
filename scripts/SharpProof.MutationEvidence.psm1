Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-NUnitMultipleAssertionLines {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Lines
    )

    if ($Lines.Count -lt 3 -or
        $Lines[0] -ne 'Multiple failures or warnings in test:') {
        return $false
    }

    $failureIndexes = @(
        for ($index = 1; $index -lt $Lines.Count; $index++) {
            if ($Lines[$index] -match '^\d+\)\s+') {
                $index
            }
        })
    if ($failureIndexes.Count -eq 0 -or $failureIndexes[0] -ne 1) {
        return $false
    }

    for ($failure = 0; $failure -lt $failureIndexes.Count; $failure++) {
        $start = $failureIndexes[$failure]
        $end = if ($failure + 1 -lt $failureIndexes.Count) {
            $failureIndexes[$failure + 1]
        }
        else {
            $Lines.Count
        }
        $block = @($Lines[$start..($end - 1)])
        $hasExpected = @($block | Where-Object {
                $_ -match '^Expected(:| is\b| and actual are both\b)'
            }).Count -ne 0
        $hasActual = @($block | Where-Object {
                $_ -match '^Expected is\b.*\bactual is\b'
            }).Count -eq 1
        $hasButWas = @($block | Where-Object {
                $_ -match '^But was:'
            }).Count -eq 1
        if ($block[0] -notmatch '^\d+\)\s+Assert\.That\(' -or
            @($block | Where-Object {
                $_ -match '(?i)\bSystem\.[A-Za-z]+Exception\b'
            }).Count -ne 0 -or
            -not $hasExpected -or
            (-not $hasActual -and -not $hasButWas)) {
            return $false
        }
    }

    return $true
}

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

    $lines = @($Message.Replace("`r`n", "`n").Split("`n") |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -ne 0 })
    if (Test-NUnitMultipleAssertionLines -Lines $lines) {
        return $true
    }
    $assertionIndex = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^Assert\.That\(') {
            $assertionIndex = $index
            break
        }
    }
    if ($assertionIndex -lt 0) {
        return $false
    }
    if ($assertionIndex -gt 0 -and
        @($lines[0..($assertionIndex - 1)] | Where-Object {
            $_ -match '(?i)\b(exception|error|warning|stack trace)\b'
        }).Count -ne 0) {
        return $false
    }
    if ($lines.Count -le $assertionIndex + 1) {
        return $false
    }

    $expectedIndex = -1
    for ($index = $assertionIndex + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^Expected(:| is\b| and actual are both\b)') {
            $expectedIndex = $index
            break
        }
    }
    if ($expectedIndex -lt 0) {
        return $false
    }

    $assertionContinuation = if ($expectedIndex -gt $assertionIndex + 1) {
        @($lines[($assertionIndex + 1)..($expectedIndex - 1)])
    }
    else {
        @()
    }
    if (@($assertionContinuation | Where-Object {
            $_ -match '(?i)\b(exception|error|warning|stack trace)\b'
        }).Count -ne 0) {
        return $false
    }

    $details = @($lines[$expectedIndex..($lines.Count - 1)])
    $allowedDetail = '^(Expected:|But was:|Expected is\b|Expected and actual are both\b|Values differ at index\b|Expected string length\b|String lengths are both\b|Missing:|Extra:|First non-matching item at index\b|-+\^$)'
    if (@($details | Where-Object { $_ -notmatch $allowedDetail }).Count -ne 0) {
        return $false
    }

    $scalarFailure = @($details | Where-Object {
            $_ -match '^Expected:'
        }).Count -eq 1 -and
        @($details | Where-Object { $_ -match '^But was:' }).Count -eq 1
    $collectionFailure = @($details | Where-Object {
            $_ -match '^Expected is\b.*\bactual is\b'
        }).Count -eq 1
    return $scalarFailure -or $collectionFailure
}

function Test-NUnitMethodIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Expected
    )

    return [StringComparer]::Ordinal.Equals($Actual, $Expected) -or
        ($Actual.StartsWith(
                $Expected + '(',
                [StringComparison]::Ordinal) -and
         $Actual.EndsWith(')', [StringComparison]::Ordinal))
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
        [int]$ProcessExitCode,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedMethodName,

        [AllowNull()]
        [string[]]$ExpectedLedger
    )

    $expectedExitCode = if ($Mode -eq 'Baseline') { 0 } else { 1 }
    if ($ProcessExitCode -ne $expectedExitCode) {
        throw "Test process exit code '$ProcessExitCode' is invalid for '$EvidenceName'."
    }
    if ($Mode -eq 'Mutation' -and
        ($null -eq $ExpectedLedger -or @($ExpectedLedger).Count -eq 0)) {
        throw "Mutation '$EvidenceName' requires a nonempty baseline ledger."
    }

    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
        throw "Test evidence for '$EvidenceName' was not produced: $TrxPath"
    }

    $maximumTrxBytes = 16MB
    $trxFile = Get-Item -LiteralPath $TrxPath
    if ($trxFile.Length -gt $maximumTrxBytes) {
        throw "Test evidence for '$EvidenceName' exceeds the TRX byte limit."
    }

    try {
        $utf8 = [Text.UTF8Encoding]::new($false, $true)
        $xmlText = $utf8.GetString([IO.File]::ReadAllBytes($TrxPath))
        if ($xmlText.Length -gt 0 -and $xmlText[0] -eq [char]0xFEFF) {
            $xmlText = $xmlText.Substring(1)
        }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = $maximumTrxBytes
        $settings.MaxCharactersFromEntities = 0
        $stringReader = [IO.StringReader]::new($xmlText)
        $reader = [Xml.XmlReader]::Create($stringReader, $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
        }
        finally {
            $reader.Dispose()
            $stringReader.Dispose()
        }
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

    $namespaceManager = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaceManager.AddNamespace('trx', $trxNamespace)
    foreach ($containerName in @(
            'ResultSummary', 'TestDefinitions', 'TestEntries', 'Results')) {
        $namedNodes = @($document.SelectNodes(
                "//*[local-name()='$containerName']"))
        if ($namedNodes.Count -ne 1 -or
            $namedNodes[0].NamespaceURI -ne $trxNamespace) {
            throw "TRX for '$EvidenceName' has misplaced or duplicate '$containerName' records."
        }
    }
    $summaries = @($document.SelectNodes(
            '/trx:TestRun/trx:ResultSummary',
            $namespaceManager))
    if ($summaries.Count -ne 1) {
        throw "TRX for '$EvidenceName' must contain exactly one result summary."
    }
    $counterNodes = @($summaries[0].SelectNodes(
            'trx:Counters',
            $namespaceManager))
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

    $definitionContainers = @($document.SelectNodes(
            '/trx:TestRun/trx:TestDefinitions',
            $namespaceManager))
    $entryContainers = @($document.SelectNodes(
            '/trx:TestRun/trx:TestEntries',
            $namespaceManager))
    $resultContainers = @($document.SelectNodes(
            '/trx:TestRun/trx:Results',
            $namespaceManager))
    if ($definitionContainers.Count -ne 1 -or
        $entryContainers.Count -ne 1 -or
        $resultContainers.Count -ne 1) {
        throw "TRX for '$EvidenceName' has missing or duplicate result containers."
    }
    $definitions = @($definitionContainers[0].SelectNodes(
            'trx:UnitTest',
            $namespaceManager))
    $entries = @($entryContainers[0].SelectNodes(
            'trx:TestEntry',
            $namespaceManager))
    $results = @($resultContainers[0].SelectNodes(
            'trx:UnitTestResult',
            $namespaceManager))
    if (@($definitionContainers[0].ChildNodes | Where-Object {
                $_.NodeType -eq [Xml.XmlNodeType]::Element
            }).Count -ne $definitions.Count -or
        @($entryContainers[0].ChildNodes | Where-Object {
                $_.NodeType -eq [Xml.XmlNodeType]::Element
            }).Count -ne $entries.Count -or
        @($resultContainers[0].ChildNodes | Where-Object {
                $_.NodeType -eq [Xml.XmlNodeType]::Element
            }).Count -ne $results.Count) {
        throw "TRX for '$EvidenceName' contains unsupported result records."
    }
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

    $definitionsById =
        [Collections.Generic.Dictionary[string, object]]::new(
            [StringComparer]::Ordinal)
    $definitionExecutionIds =
        [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $testId = $definition.GetAttribute('id')
        $executions = @($definition.SelectNodes(
                'trx:Execution',
                $namespaceManager))
        $methods = @($definition.SelectNodes(
                'trx:TestMethod',
                $namespaceManager))
        if ([string]::IsNullOrWhiteSpace($testId) -or
            $executions.Count -ne 1 -or $methods.Count -ne 1 -or
            $definitionsById.ContainsKey($testId)) {
            throw "TRX for '$EvidenceName' has malformed test definitions."
        }
        $execution = $executions[0]
        $method = $methods[0]
        $executionId = $execution.GetAttribute('id')
        if ([string]::IsNullOrWhiteSpace($executionId) -or
            -not $definitionExecutionIds.Add($executionId)) {
            throw "TRX for '$EvidenceName' has malformed execution identities."
        }
        $definitionsById[$testId] = [pscustomobject]@{
            executionId = $executionId
            className = $method.GetAttribute('className')
            methodName = $method.GetAttribute('name')
            displayName = $definition.GetAttribute('name')
        }
    }

    $entriesById =
        [Collections.Generic.Dictionary[string, string]]::new(
            [StringComparer]::Ordinal)
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
    $seenResults =
        [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
    foreach ($result in $results) {
        $testId = $result.GetAttribute('testId')
        $executionId = $result.GetAttribute('executionId')
        if (-not $definitionsById.ContainsKey($testId) -or
            -not $entriesById.ContainsKey($testId) -or
            -not $seenResults.Add($testId)) {
            throw "TRX for '$EvidenceName' has unresolved or duplicate results."
        }
        $definition = $definitionsById[$testId]
        if ([string]::IsNullOrWhiteSpace($executionId) -or
            -not [StringComparer]::Ordinal.Equals(
                $executionId, $definition.executionId) -or
            -not [StringComparer]::Ordinal.Equals(
                $executionId, $entriesById[$testId]) -or
            -not (Test-NUnitMethodIdentity `
                -Actual $definition.methodName `
                -Expected $ExpectedMethodName) -or
            [string]::IsNullOrWhiteSpace($definition.className) -or
            -not [StringComparer]::Ordinal.Equals(
                $result.GetAttribute('testName'), $definition.displayName)) {
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
            $outputs = @($_.SelectNodes('trx:Output', $namespaceManager))
            $errorInfos = @($_.SelectNodes(
                    'trx:Output/trx:ErrorInfo',
                    $namespaceManager))
            $messages = @($_.SelectNodes(
                    'trx:Output/trx:ErrorInfo/trx:Message',
                    $namespaceManager))
            $outputs.Count -eq 1 -and
                $errorInfos.Count -eq 1 -and
                $messages.Count -eq 1 -and
                (Test-NUnitAssertionMessage -Message $messages[0].InnerText)
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
