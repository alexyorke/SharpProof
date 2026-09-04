Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Assert-SharpProofJsonProperties.ps1')

function Assert-ExactJsonProperties {
    param(
        [Parameter(Mandatory = $true)][object]$Value,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $actual = @($Value.PSObject.Properties.Name)
    if ($actual.Count -ne $Expected.Count -or
        @($actual | Where-Object { $Expected -cnotcontains $_ }).Count -ne 0) {
        throw "$Description has an unexpected property set."
    }
}

function Assert-SharpProofStandaloneGateResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]
        [ValidateSet('corpus', 'performance')][string]$ExpectedGate,
        [Parameter(Mandatory = $true)][string]$ExpectedCommit,
        [Parameter(Mandatory = $true)][string]$ExpectedMvid
    )

    $jsonDocument = $null
    try {
        $bytes = [IO.File]::ReadAllBytes($Path)
        if ($bytes.Length -eq 0) {
            throw 'The standalone gate result is empty.'
        }
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            throw 'The standalone gate result must be UTF-8 without a BOM.'
        }
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $jsonDocument = [Text.Json.JsonDocument]::Parse($json)
        if ($jsonDocument.RootElement.ValueKind -ne
                [Text.Json.JsonValueKind]::Object) {
            throw 'The standalone gate result must be an object.'
        }
        Assert-UniqueJsonProperties `
            $jsonDocument.RootElement 'Standalone gate result'
        $document = $json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "The standalone gate result is not canonical JSON: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $jsonDocument) {
            $jsonDocument.Dispose()
        }
    }
    Assert-ExactJsonProperties -Value $document -Description 'Gate envelope' `
        -Expected @(
            'SchemaVersion', 'Gate', 'Passed', 'SourceCommit',
            'Executable', 'Result')
    if ($document.SchemaVersion -isnot [long] -or
        [int]$document.SchemaVersion -ne 1) {
        throw 'The standalone gate result schema is unsupported.'
    }
    if ($document.Gate -isnot [string] -or
        $document.Gate -cne $ExpectedGate) {
        throw 'The standalone gate result identifies the wrong gate.'
    }
    if ($document.Passed -isnot [bool] -or -not $document.Passed) {
        throw 'The standalone gate result did not pass.'
    }
    if ($document.SourceCommit -isnot [string] -or
        $document.SourceCommit -cne $ExpectedCommit) {
        throw 'The standalone gate result is bound to the wrong source commit.'
    }
    Assert-ExactJsonProperties -Value $document.Executable `
        -Description 'Gate executable identity' `
        -Expected @('Mvid')
    if ($document.Executable.Mvid -isnot [string] -or
        $document.Executable.Mvid -cne $ExpectedMvid -or
        [string]::IsNullOrWhiteSpace($document.Executable.Mvid)) {
        throw 'The standalone gate result has the wrong executable identity.'
    }

    $corpusProperties = @(
        'Passed', 'CaseCount', 'BaseCaseCount', 'OpenSourceMethodCount',
        'SupportedOpenSourceMethodCount', 'OpenSourceFileCount',
        'SyntheticSeedCount', 'VariantCount', 'DiagnosticCount',
        'SupportedCaseCount', 'IntentionallyUnsupportedCaseCount',
        'SupportedUnknownCount', 'UnknownCount', 'SilentUnknownCount',
        'TotalUnknownCount', 'UnknownRate', 'SilentUnknownRate',
        'TotalUnknownRate', 'CacheReplayCount', 'ConcurrentReplayCount',
        'UnknownReasons', 'AllowedDegradations', 'Failures')
    $performanceProperties = @(
        'Passed', 'Warmups', 'Samples', 'PackageBuildEstimatorVersion',
        'PackageBuildSdk', 'PackageBuildSamples', 'OrderBalancedRatios',
        'UnannotatedAdvisoryAnalyzerDriverRunCount',
        'UnannotatedAdvisoryAnalysisSessionCreateCount',
        'UnannotatedAdvisoryApiSpecCreateCount',
        'UnannotatedAdvisoryEffectAnalysisCreateCount',
        'OrderBalancedMedianRatio', 'RawMedianRatio',
        'BaselineFirstMedianRatio', 'UnannotatedAdvisoryFirstMedianRatio',
        'RawP95Ratio', 'BaselineRetainedBytes',
        'UnannotatedAdvisoryRetainedBytes', 'RetainedMemoryRatio',
        'RetainedMemoryIncreaseMiB', 'EnabledRetainedCompilationCount',
        'EnabledRetainedMemoryIncreaseMiB', 'IdeEdits',
        'IdeEditP95Milliseconds', 'IdeEditMaximumMilliseconds',
        'IdeDiagnosticReplayFailureCount', 'CancellationP95Milliseconds',
        'ForcedTerminationMilliseconds', 'Failures')
    $expectedResultProperties = if ($ExpectedGate -ceq 'corpus') {
        $corpusProperties
    } else {
        $performanceProperties
    }
    Assert-ExactJsonProperties -Value $document.Result `
        -Description "$ExpectedGate result" `
        -Expected $expectedResultProperties
    foreach ($property in $document.Result.PSObject.Properties) {
        if ($property.Name -ceq 'Passed') { continue }
        if ($property.Name -in @(
                'UnknownReasons', 'AllowedDegradations', 'Failures',
                'PackageBuildSamples', 'OrderBalancedRatios')) {
            if ($property.Value -isnot [Array]) {
                throw "The $ExpectedGate result property '$($property.Name)' must be an array."
            }
            continue
        }
        if ($property.Name -ceq 'PackageBuildEstimatorVersion') {
            if ($property.Value -isnot [string] -or
                [string]::IsNullOrWhiteSpace($property.Value)) {
                throw 'The performance estimator version must be a string.'
            }
            continue
        }
        if ($property.Name -ceq 'PackageBuildSdk') { continue }
        if ($property.Value -isnot [ValueType] -or
            $property.Value -is [bool]) {
            throw "The $ExpectedGate result property '$($property.Name)' must be numeric."
        }
    }
    if ($ExpectedGate -ceq 'corpus') {
        foreach ($reason in @($document.Result.UnknownReasons)) {
            Assert-ExactJsonProperties -Value $reason `
                -Description 'Corpus unknown-reason row' `
                -Expected @('Reason', 'Count')
        }
        if (@($document.Result.AllowedDegradations | Where-Object {
                    $_ -isnot [string]
                }).Count -ne 0) {
            throw 'Corpus allowed degradations must be strings.'
        }
    }
    else {
            Assert-ExactJsonProperties -Value $document.Result.PackageBuildSdk `
                -Description 'Performance SDK identity' `
                -Expected @(
                'ConfiguredVersion', 'RollForward', 'ResolvedVersion')
        foreach ($sample in @($document.Result.PackageBuildSamples)) {
            Assert-ExactJsonProperties -Value $sample `
                -Description 'Performance package-build sample' `
                -Expected @(
                    'Index', 'UnannotatedAdvisoryFirst',
                    'BaselineMilliseconds',
                    'UnannotatedAdvisoryMilliseconds', 'Ratio')
        }
    }
    if ($document.Result.Passed -isnot [bool] -or
        -not $document.Result.Passed -or
        $document.Result.Failures -isnot [Array] -or
        @($document.Result.Failures).Count -ne 0) {
        throw "The $ExpectedGate result does not carry an exact passing status."
    }
    return $document
}
