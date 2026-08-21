Set-StrictMode -Version Latest

function Assert-ExactJsonObjectProperties {
    param(
        [Parameter(Mandatory = $true)]
        [Text.Json.JsonElement]$Object,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Object.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
        throw "$Description must be a JSON object."
    }
    $actual = @($Object.EnumerateObject() | ForEach-Object { $_.Name })
    if ($actual.Count -ne $Expected.Count -or
        @($actual | Select-Object -Unique).Count -ne $actual.Count -or
        @($actual | Where-Object { $Expected -cnotcontains $_ }).Count -ne 0) {
        throw "$Description has an unexpected property set."
    }
}

function Get-ExactJsonInt32 {
    param(
        [Parameter(Mandatory = $true)]
        [Text.Json.JsonElement]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $property = $Object.GetProperty($Name)
    $value = 0
    if ($property.ValueKind -ne [Text.Json.JsonValueKind]::Number -or
        -not $property.TryGetInt32([ref]$value)) {
        throw "JSON property '$Name' must be an Int32 number token."
    }
    return $value
}

function Get-ExactJsonBoolean {
    param(
        [Parameter(Mandatory = $true)]
        [Text.Json.JsonElement]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $property = $Object.GetProperty($Name)
    if ($property.ValueKind -notin @(
            [Text.Json.JsonValueKind]::True,
            [Text.Json.JsonValueKind]::False)) {
        throw "JSON property '$Name' must be a Boolean token."
    }
    return $property.GetBoolean()
}

function Assert-SharpProofFuzzRunnerResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$ExpectedCases,
        [Parameter(Mandatory = $true)][int]$ExpectedSeed,
        [Parameter(Mandatory = $true)][int]$ExpectedMaximumParallelism
    )

    $document = $null
    try {
        $document = [Text.Json.JsonDocument]::Parse(
            [IO.File]::ReadAllText($Path))
        $root = $document.RootElement
        Assert-ExactJsonObjectProperties -Object $root `
            -Description 'Fuzz runner result' `
            -Expected @(
                'SchemaVersion', 'Cases', 'Seed', 'MaximumParallelism',
                'Agreements', 'Abstentions', 'FrontendAgreements',
                'SmtAgreements', 'PartialSmtAgreements', 'FrontendCoverage',
                'CoverageSatisfied', 'Failures', 'Passed')

        $schema = Get-ExactJsonInt32 $root 'SchemaVersion'
        $cases = Get-ExactJsonInt32 $root 'Cases'
        $seed = Get-ExactJsonInt32 $root 'Seed'
        $maximumParallelism = Get-ExactJsonInt32 $root 'MaximumParallelism'
        $agreements = Get-ExactJsonInt32 $root 'Agreements'
        $abstentions = Get-ExactJsonInt32 $root 'Abstentions'
        $frontendAgreements = Get-ExactJsonInt32 $root 'FrontendAgreements'
        $smtAgreements = Get-ExactJsonInt32 $root 'SmtAgreements'
        $partialSmtAgreements = Get-ExactJsonInt32 `
            $root 'PartialSmtAgreements'
        $coverageSatisfied = Get-ExactJsonBoolean $root 'CoverageSatisfied'
        $passed = Get-ExactJsonBoolean $root 'Passed'

        if ($schema -ne 4) { throw "Unsupported fuzz schema '$schema'." }
        if ($cases -ne $ExpectedCases -or $seed -ne $ExpectedSeed -or
            $maximumParallelism -ne $ExpectedMaximumParallelism) {
            throw 'The fuzz runner invocation identity does not match its result.'
        }
        if ($agreements -lt 0 -or $abstentions -lt 0 -or
            $frontendAgreements -lt 0 -or $smtAgreements -lt 0 -or
            $partialSmtAgreements -lt 0 -or
            $agreements + $abstentions -ne $cases -or
            $abstentions -ne 0 -or $agreements -ne $cases -or
            $frontendAgreements -ne $cases -or
            $smtAgreements -ne $cases -or
            $partialSmtAgreements -ne $cases) {
            throw 'The fuzz runner counts do not form a complete agreement partition.'
        }

        $coverage = $root.GetProperty('FrontendCoverage')
        $coverageProperties = @(
            'TextParameters', 'StringLiterals', 'NullStrings',
            'StringConcatenations', 'StringLengths', 'StringCasts',
            'ArrayLengths', 'ArrayIndexes', 'DivideByZeroExceptions',
            'OverflowExceptions', 'NullReferenceExceptions',
            'IndexOutOfRangeExceptions', 'InvalidCastExceptions')
        Assert-ExactJsonObjectProperties -Object $coverage `
            -Expected $coverageProperties -Description 'Frontend coverage'
        foreach ($name in $coverageProperties) {
            if ((Get-ExactJsonInt32 $coverage $name) -le 0) {
                throw "Frontend coverage '$name' must be positive."
            }
        }

        $failures = $root.GetProperty('Failures')
        if ($failures.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
            throw 'Fuzz failures must be a non-null JSON array.'
        }
        foreach ($failure in $failures.EnumerateArray()) {
            Assert-ExactJsonObjectProperties -Object $failure `
                -Description 'Fuzz failure' `
                -Expected @(
                    'Case', 'Seed', 'Oracle', 'Original', 'Minimized',
                    'Detail', 'Term')
            [void](Get-ExactJsonInt32 $failure 'Case')
            [void](Get-ExactJsonInt32 $failure 'Seed')
            foreach ($name in @(
                    'Oracle', 'Original', 'Minimized', 'Detail', 'Term')) {
                if ($failure.GetProperty($name).ValueKind -ne
                    [Text.Json.JsonValueKind]::String) {
                    throw "Fuzz failure '$name' must be a string."
                }
            }
        }
        if ($failures.GetArrayLength() -ne 0 -or
            -not $coverageSatisfied -or -not $passed) {
            throw 'The fuzz runner did not produce a passing result.'
        }
    }
    catch {
        throw "Invalid fuzz runner result: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }

    return Get-Content -LiteralPath $Path -Raw |
        ConvertFrom-Json -ErrorAction Stop
}
