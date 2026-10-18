Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Assert-SharpProofJsonProperties.ps1')

function Read-SharpProofBoundedJsonDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ByteLimitMessage,
        [Parameter(Mandatory = $true)][string]$ShortReadMessage,
        [Parameter(Mandatory = $true)][string]$GrowthMessage
    )

    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        if ($stream.Length -eq 0 -or $stream.Length -gt 1048576) {
            throw $ByteLimitMessage
        }
        $bytes = [byte[]]::new([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read(
                $bytes,
                $offset,
                $bytes.Length - $offset)
            if ($read -eq 0) {
                throw $ShortReadMessage
            }
            $offset += $read
        }
        if ($stream.ReadByte() -ne -1) {
            throw $GrowthMessage
        }
    }
    finally {
        $stream.Dispose()
    }

    $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    return [pscustomobject]@{
        Document = [Text.Json.JsonDocument]::Parse($json)
        Bytes = $bytes
        Json = $json
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
        [scriptblock]$AfterValidation
    )

    $document = $null
    $bytes = $null
    $json = $null
    try {
        $validation = Read-SharpProofBoundedJsonDocument -Path $Path `
            -ByteLimitMessage 'The fuzz runner result exceeds its byte limit.' `
            -ShortReadMessage 'The fuzz runner result ended before its declared length.' `
            -GrowthMessage 'The fuzz runner result changed during validation.'
        $document = $validation.Document
        $bytes = $validation.Bytes
        $json = $validation.Json
        $root = $document.RootElement
        if ($root.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            throw 'Fuzz runner result must be a JSON object.'
        }
        Assert-SharpProofExactJsonProperties `
            -Actual @($root.EnumerateObject() | ForEach-Object { $_.Name }) `
            -Description 'Fuzz runner result' `
            -RejectDuplicates `
            -Expected @(
                'SchemaVersion', 'Cases', 'Seed', 'MaximumParallelism',
                'Agreements', 'Abstentions', 'FrontendAgreements',
                'SmtAgreements', 'PartialSmtAgreements', 'FrontendCoverage',
                'TotalProgramCoverage', 'MetadataProgramCoverage', 'CoverageSatisfied', 'Failures', 'Passed')

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

        if ($schema -ne 7) { throw "Unsupported fuzz schema '$schema'." }
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
        if ($coverage.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            throw 'Frontend coverage must be a JSON object.'
        }
        Assert-SharpProofExactJsonProperties `
            -Actual @($coverage.EnumerateObject() | ForEach-Object { $_.Name }) `
            -RejectDuplicates `
            -Expected $coverageProperties -Description 'Frontend coverage'
        [long]$exceptionTotal = 0
        $coverageValues = [ordered]@{}
        foreach ($name in $coverageProperties) {
            $count = Get-ExactJsonInt32 $coverage $name
            $coverageValues[$name] = $count
            if ($count -lt 0 -or ($cases -ge 1000 -and $count -eq 0)) {
                throw "Frontend coverage '$name' is invalid for the executed case count."
            }
            if ($name -in @(
                    'DivideByZeroExceptions', 'OverflowExceptions',
                    'NullReferenceExceptions', 'IndexOutOfRangeExceptions',
                    'InvalidCastExceptions')) {
                $exceptionTotal += [long]$count
            }
        }
        if ($exceptionTotal -gt $cases) {
            throw 'Frontend exception coverage exceeds the executed case count.'
        }

        $totalCoverage = $root.GetProperty('TotalProgramCoverage')
        $totalProperties = @(
            'Cases', 'Agreements', 'NativeProofs', 'NativeRefutations',
            'WrappedBodies', 'CheckedBodies', 'FinallyBodies', 'SourceCalls',
            'BooleanBodies', 'LoopBodies', 'ReferenceBodies', 'ExceptionalExits', 'TypeMask', 'ArrayReadBodies')
        if ($totalCoverage.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            throw 'Total program coverage must be a JSON object.'
        }
        Assert-SharpProofExactJsonProperties `
            -Actual @($totalCoverage.EnumerateObject() | ForEach-Object { $_.Name }) `
            -RejectDuplicates -Expected $totalProperties -Description 'Total program coverage'
        $totalValues = [ordered]@{}
        foreach ($name in $totalProperties) {
            $count = Get-ExactJsonInt32 $totalCoverage $name
            if ($count -lt 0) { throw "Total program coverage '$name' is negative." }
            $totalValues[$name] = $count
        }
        [long]$bodyTotal = 0
        foreach ($name in @('WrappedBodies', 'CheckedBodies', 'FinallyBodies', 'SourceCalls',
                'BooleanBodies', 'LoopBodies', 'ReferenceBodies')) {
            $bodyTotal += [long]$totalValues[$name]
            if ($cases -ge 1000 -and $totalValues[$name] -eq 0) {
                throw "Total program coverage '$name' is missing."
            }
        }
        if ($totalValues.Cases -ne $cases -or $totalValues.Agreements -ne $cases -or
            $totalValues.NativeProofs -ne $cases -or $bodyTotal -ne $cases -or
            [long]$totalValues.NativeRefutations + $totalValues.ExceptionalExits -ne $cases -or
            $totalValues.ExceptionalExits -gt $totalValues.ReferenceBodies -or
            $totalValues.ArrayReadBodies -gt $totalValues.ReferenceBodies -or
            $totalValues.TypeMask -lt 1 -or $totalValues.TypeMask -gt 8191 -or
            ($cases -ge 1000 -and ($totalValues.TypeMask -ne 8191 -or $totalValues.ExceptionalExits -eq 0 -or $totalValues.ArrayReadBodies -eq 0))) {
            throw 'Total program coverage does not form a complete agreement partition.'
        }

        $metadataCoverage = $root.GetProperty('MetadataProgramCoverage')
        $metadataProperties = @('Cases', 'Agreements', 'NativeProofs', 'NativeRefutations', 'TypeMask', 'RecipeMask')
        if ($metadataCoverage.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            throw 'Metadata program coverage must be a JSON object.'
        }
        Assert-SharpProofExactJsonProperties `
            -Actual @($metadataCoverage.EnumerateObject() | ForEach-Object { $_.Name }) `
            -Description 'Metadata program coverage' -RejectDuplicates -Expected $metadataProperties
        $metadataValues = [ordered]@{}
        foreach ($name in $metadataProperties) {
            $metadataValues[$name] = Get-ExactJsonInt32 $metadataCoverage $name
        }
        if ($metadataValues.Cases -ne $cases -or $metadataValues.Agreements -ne $cases -or
            $metadataValues.NativeProofs -ne $cases -or $metadataValues.NativeRefutations -ne $cases -or
            $metadataValues.TypeMask -lt 1 -or $metadataValues.TypeMask -gt 1023 -or
            $metadataValues.RecipeMask -lt 1 -or $metadataValues.RecipeMask -gt 127 -or
            ($cases -ge 1000 -and ($metadataValues.TypeMask -ne 1023 -or $metadataValues.RecipeMask -ne 127))) {
            throw 'Metadata program coverage does not form complete native agreement evidence.'
        }

        $failures = $root.GetProperty('Failures')
        if ($failures.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
            throw 'Fuzz failures must be a non-null JSON array.'
        }
        foreach ($failure in $failures.EnumerateArray()) {
            if ($failure.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
                throw 'Fuzz failure must be a JSON object.'
            }
            Assert-SharpProofExactJsonProperties `
                -Actual @($failure.EnumerateObject() | ForEach-Object { $_.Name }) `
                -Description 'Fuzz failure' `
                -RejectDuplicates `
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
        $result = [pscustomobject][ordered]@{
            SchemaVersion = $schema
            Cases = $cases
            Seed = $seed
            MaximumParallelism = $maximumParallelism
            Agreements = $agreements
            Abstentions = $abstentions
            FrontendAgreements = $frontendAgreements
            SmtAgreements = $smtAgreements
            PartialSmtAgreements = $partialSmtAgreements
            FrontendCoverage = [pscustomobject]$coverageValues
            TotalProgramCoverage = [pscustomobject]$totalValues
            MetadataProgramCoverage = [pscustomobject]$metadataValues
            CoverageSatisfied = $coverageSatisfied
            Failures = [object[]]@()
            Passed = $passed
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

    # The validator owns the bytes it accepted.  Re-seal them over the
    # validated path after the callback so a replacement during validation
    # cannot become the artifact cited by the campaign summary.  Move the
    # snapshot into place atomically and clean up on failure.
    $temporary = Join-Path ([IO.Path]::GetDirectoryName(
            [IO.Path]::GetFullPath($Path))) (
        '.' + [IO.Path]::GetFileName($Path) + '.' +
        [Guid]::NewGuid().ToString('N') + '.validated.tmp')
    try {
        [IO.File]::WriteAllBytes($temporary, $bytes)
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        if ([IO.File]::Exists($temporary)) {
            [IO.File]::Delete($temporary)
        }
    }

    return $result
}
