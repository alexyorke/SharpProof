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
        [Parameter(Mandatory = $true)][int]$ExpectedMaximumParallelism,
        [switch]$AllowFailure,
        [scriptblock]$AfterValidation
    )

    $document = $null
    $bytes = $null
    $json = $null
    try {
        $stream = [IO.FileStream]::new(
            $Path,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        try {
            if ($stream.Length -eq 0 -or $stream.Length -gt 1048576) {
                throw 'The fuzz runner result exceeds its byte limit.'
            }
            $bytes = [byte[]]::new([int]$stream.Length)
            $offset = 0
            while ($offset -lt $bytes.Length) {
                $read = $stream.Read(
                    $bytes,
                    $offset,
                    $bytes.Length - $offset)
                if ($read -eq 0) {
                    throw 'The fuzz runner result ended before its declared length.'
                }
                $offset += $read
            }
            if ($stream.ReadByte() -ne -1) {
                throw 'The fuzz runner result changed during validation.'
            }
        }
        finally {
            $stream.Dispose()
        }
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $document = [Text.Json.JsonDocument]::Parse(
            $json)
        $root = $document.RootElement
        Assert-ExactJsonObjectProperties -Object $root `
            -Description 'Fuzz runner result' `
            -Expected @(
                'SchemaVersion', 'Cases', 'Seed', 'MaximumParallelism',
                'Agreements', 'Abstentions', 'FrontendAgreements',
                'SmtAgreements', 'FiniteSmtSatisfiable',
                'FiniteSmtUnsatisfiable', 'FiniteSmtAssumptions',
                'PartialSmtAgreements', 'PartialSmtDefinedTrue',
                'PartialSmtDefinedFalse', 'PartialSmtUndefined',
                'FrontendCoverage',
                'CoverageSatisfied', 'Failures', 'AbstentionEvidence', 'Passed')

        $schema = Get-ExactJsonInt32 $root 'SchemaVersion'
        $cases = Get-ExactJsonInt32 $root 'Cases'
        $seed = Get-ExactJsonInt32 $root 'Seed'
        $maximumParallelism = Get-ExactJsonInt32 $root 'MaximumParallelism'
        $agreements = Get-ExactJsonInt32 $root 'Agreements'
        $abstentions = Get-ExactJsonInt32 $root 'Abstentions'
        $frontendAgreements = Get-ExactJsonInt32 $root 'FrontendAgreements'
        $smtAgreements = Get-ExactJsonInt32 $root 'SmtAgreements'
        $finiteSmtSatisfiable = Get-ExactJsonInt32 `
            $root 'FiniteSmtSatisfiable'
        $finiteSmtUnsatisfiable = Get-ExactJsonInt32 `
            $root 'FiniteSmtUnsatisfiable'
        $finiteSmtAssumptions = Get-ExactJsonInt32 `
            $root 'FiniteSmtAssumptions'
        $partialSmtDefinedTrue = Get-ExactJsonInt32 `
            $root 'PartialSmtDefinedTrue'
        $partialSmtDefinedFalse = Get-ExactJsonInt32 `
            $root 'PartialSmtDefinedFalse'
        $partialSmtUndefined = Get-ExactJsonInt32 `
            $root 'PartialSmtUndefined'
        $partialSmtAgreements = Get-ExactJsonInt32 `
            $root 'PartialSmtAgreements'
        $coverageSatisfied = Get-ExactJsonBoolean $root 'CoverageSatisfied'
        $passed = Get-ExactJsonBoolean $root 'Passed'
        $failureCount = $root.GetProperty('Failures').GetArrayLength()

        if ($schema -ne 6) { throw "Unsupported fuzz schema '$schema'." }
        if ($cases -lt 1) {
            throw 'The fuzz runner case count must be positive.'
        }
        if ($maximumParallelism -lt 1 -or $maximumParallelism -gt 4) {
            throw 'The fuzz runner maximum parallelism must be between 1 and 4.'
        }
        if ($cases -ne $ExpectedCases -or $seed -ne $ExpectedSeed -or
            $maximumParallelism -ne $ExpectedMaximumParallelism) {
            throw 'The fuzz runner invocation identity does not match its result.'
        }
        if ($agreements -lt 0 -or $abstentions -lt 0 -or
            $frontendAgreements -lt 0 -or $smtAgreements -lt 0 -or
            $finiteSmtSatisfiable -lt 0 -or
            $finiteSmtUnsatisfiable -lt 0 -or
            $finiteSmtAssumptions -lt 0 -or
            $partialSmtDefinedTrue -lt 0 -or
            $partialSmtDefinedFalse -lt 0 -or
            $partialSmtUndefined -lt 0 -or
            $partialSmtAgreements -lt 0 -or
            $agreements + $abstentions -gt $cases -or
            (-not $AllowFailure -and
                ($agreements + $abstentions -ne $cases -or
                 $abstentions -ne 0 -or $agreements -ne $cases)) -or
            ($AllowFailure -and
                ($passed -or
                 ($agreements + $abstentions -eq $cases -and
                  $abstentions -eq 0 -and $failureCount -eq 0) -or
                 ($agreements + $abstentions -lt $cases -and
                  $failureCount -eq 0))) -or
            (-not $AllowFailure -and
                ($frontendAgreements -ne $cases -or
                 $smtAgreements -ne $cases -or
                 $partialSmtAgreements -ne $cases)) -or
            ($AllowFailure -and
                ($frontendAgreements -gt $cases -or
                 $smtAgreements -gt $cases -or
                 $partialSmtAgreements -gt $cases)) -or
            $finiteSmtSatisfiable + $finiteSmtUnsatisfiable -ne $cases -or
            $finiteSmtAssumptions -eq 0 -or
            $partialSmtDefinedTrue + $partialSmtDefinedFalse +
                $partialSmtUndefined -ne $cases * 2 -or
            $partialSmtAgreements -gt $cases) {
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
            $count = Get-ExactJsonInt32 $coverage $name
            if ($count -lt 0 -or ($cases -ge 1000 -and $count -eq 0)) {
                throw "Frontend coverage '$name' is invalid for the executed case count."
            }
        }
        $exceptionTotal = [long](Get-ExactJsonInt32 `
                $coverage 'DivideByZeroExceptions') +
            [long](Get-ExactJsonInt32 $coverage 'OverflowExceptions') +
            [long](Get-ExactJsonInt32 $coverage 'NullReferenceExceptions') +
            [long](Get-ExactJsonInt32 `
                $coverage 'IndexOutOfRangeExceptions') +
            [long](Get-ExactJsonInt32 $coverage 'InvalidCastExceptions')
        if ($exceptionTotal -gt $cases) {
            throw 'Frontend exception coverage exceeds the executed case count.'
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
        $abstentionEvidence = $root.GetProperty('AbstentionEvidence')
        if ($abstentionEvidence.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
            throw 'Fuzz abstention evidence must be a non-null JSON array.'
        }
        foreach ($abstention in $abstentionEvidence.EnumerateArray()) {
            Assert-ExactJsonObjectProperties -Object $abstention `
                -Description 'Fuzz abstention evidence' `
                -Expected @('Case', 'Seed', 'Oracle', 'Input', 'Detail')
            [void](Get-ExactJsonInt32 $abstention 'Case')
            [void](Get-ExactJsonInt32 $abstention 'Seed')
            foreach ($name in @('Oracle', 'Input', 'Detail')) {
                if ($abstention.GetProperty($name).ValueKind -ne
                    [Text.Json.JsonValueKind]::String) {
                    throw "Fuzz abstention '$name' must be a string."
                }
            }
        }
        if ((-not $AllowFailure -and
                ($failures.GetArrayLength() -ne 0 -or
                 $abstentionEvidence.GetArrayLength() -ne 0 -or
                 -not $coverageSatisfied -or -not $passed)) -or
            ($AllowFailure -and -not $coverageSatisfied)) {
            throw 'The fuzz runner did not produce a passing result.'
        }
    }
    catch {
        throw "Invalid fuzz runner result: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }

    if ($null -ne $AfterValidation) {
        & $AfterValidation $Path
    }

    $result = $json | ConvertFrom-Json -ErrorAction Stop
    $result | Add-Member -NotePropertyName ResultSha256 -NotePropertyValue (
        [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant())
    return $result
}
