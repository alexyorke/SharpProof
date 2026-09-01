Set-StrictMode -Version Latest

function Get-SharpProofRotatingSeed {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][DateTime]$UtcDate)

    $utcDay = [int]$UtcDate.ToUniversalTime().Date.Subtract(
        [DateTime]::UnixEpoch).TotalDays
    return [int]($utcDay * 1009 + [int][Math]::Floor($utcDay / 397))
}

function Get-SharpProofCleanFuzzSourceCommit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $headOutput = @(& git -C $RepositoryRoot rev-parse HEAD)
    $headExitCode = $LASTEXITCODE
    $head = if ($headOutput.Count -eq 1) {
        ([string]$headOutput[0]).Trim()
    }
    else {
        ''
    }
    if ($headExitCode -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        throw 'Unable to bind fuzz evidence to the exact source commit.'
    }
    $dirty = @(& git -C $RepositoryRoot status `
        --porcelain=v1 --untracked-files=no)
    if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) {
        throw 'Fuzz evidence requires a clean tracked repository tree.'
    }
    return $head
}

function Assert-SharpProofFuzzCaseBudget {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (($Value -isnot [int] -and $Value -isnot [int64]) -or
        [int64]$Value -le 0 -or [int64]$Value -gt 1000000) {
        throw "$Name must be an integer from 1 through 1000000."
    }
    return [int]$Value
}

function Assert-SharpProofFuzzCampaignBudget {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][int]$RotatingCases,
        [Parameter(Mandatory = $true)][int]$RetainedCases,
        [Parameter(Mandatory = $true)][int]$RetainedRunCount,
        [Parameter(Mandatory = $true)][int]$MaximumCases
    )

    if ($RotatingCases -le 0 -or $RetainedCases -le 0 -or
        $RetainedRunCount -lt 0 -or $RetainedRunCount -gt 1024 -or
        $MaximumCases -le 0) {
        throw 'Fuzz campaign budget inputs are invalid.'
    }
    [long]$requestedCases = [long]$RotatingCases +
        [long]$RetainedCases * [long]$RetainedRunCount
    if ($requestedCases -gt $MaximumCases) {
        throw "Fuzz campaign requests $requestedCases cases; maximum is $MaximumCases."
    }
    return [int]$requestedCases
}

function Read-SharpProofRetainedFuzzSeedManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [scriptblock]$AfterValidation
    )

    $document = $null
    try {
        $stream = [IO.FileStream]::new(
            $Path,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        try {
            if ($stream.Length -eq 0 -or $stream.Length -gt 1048576) {
                throw 'The retained fuzz seed manifest exceeds its byte limit.'
            }
            $bytes = [byte[]]::new([int]$stream.Length)
            $offset = 0
            while ($offset -lt $bytes.Length) {
                $read = $stream.Read(
                    $bytes,
                    $offset,
                    $bytes.Length - $offset)
                if ($read -eq 0) {
                    throw 'The retained fuzz seed manifest changed while read.'
                }
                $offset += $read
            }
            if ($stream.ReadByte() -ne -1) {
                throw 'The retained fuzz seed manifest changed while read.'
            }
        }
        finally {
            $stream.Dispose()
        }
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $document = [Text.Json.JsonDocument]::Parse(
            $json)
        $root = $document.RootElement
        if ($root.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
            throw 'The retained fuzz seed manifest must be an object.'
        }
        $expected = @('schemaVersion', 'casesPerSeed', 'seeds')
        $names = @($root.EnumerateObject() | ForEach-Object { $_.Name })
        if ($names.Count -ne $expected.Count -or
            @($names | Where-Object { $expected -cnotcontains $_ }).Count -ne 0) {
            throw 'The retained fuzz seed manifest has unexpected properties.'
        }

        [int]$schemaVersion = 0
        $schema = $root.GetProperty('schemaVersion')
        if ($schema.ValueKind -ne [Text.Json.JsonValueKind]::Number -or
            -not $schema.TryGetInt32([ref]$schemaVersion)) {
            throw 'The retained fuzz seed schema version must be an exact Int32.'
        }
        [int]$casesPerSeed = 0
        $cases = $root.GetProperty('casesPerSeed')
        if ($cases.ValueKind -ne [Text.Json.JsonValueKind]::Number -or
            -not $cases.TryGetInt32([ref]$casesPerSeed)) {
            throw 'Retained fuzz cases per seed must be an exact Int32.'
        }
        $seedValues = $root.GetProperty('seeds')
        if ($seedValues.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
            throw 'Retained fuzz seeds must be an array.'
        }
        $seeds = [Collections.Generic.List[int]]::new()
        foreach ($element in $seedValues.EnumerateArray()) {
            [int]$seed = 0
            if ($element.ValueKind -ne [Text.Json.JsonValueKind]::Number -or
                -not $element.TryGetInt32([ref]$seed)) {
                throw 'Every retained fuzz seed must be an exact Int32.'
            }
            $seeds.Add($seed)
        }
        if ($schemaVersion -ne 1 -or $casesPerSeed -le 0 -or
            $casesPerSeed -gt 1000000 -or
            $seeds.Count -eq 0 -or $seeds.Count -gt 1024) {
            throw 'Invalid retained fuzz seed manifest.'
        }
        if (@($seeds | Select-Object -Unique).Count -ne $seeds.Count) {
            throw 'The retained fuzz seed manifest contains duplicate seeds.'
        }
        if ($null -ne $AfterValidation) {
            & $AfterValidation $Path
        }

        return [pscustomobject]@{
            SchemaVersion = $schemaVersion
            CasesPerSeed = $casesPerSeed
            Seeds = @($seeds)
            Sha256 = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
        }
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function Initialize-SharpProofFuzzEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory
    )

    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    foreach ($file in [IO.Directory]::EnumerateFiles($OutputDirectory)) {
        $name = [IO.Path]::GetFileName($file)
        if ($name -ceq 'campaign.json' -or
            $name -ceq '.campaign.json.tmp') {
            [IO.File]::Delete($file)
            continue
        }
        if ($name -cmatch `
                '^(?:rotating|retained)-(?<seed>-?[0-9]+)\.(?:stdout\.json|stderr\.txt)$') {
            $seed = 0
            $seedToken = $Matches.seed
            if ([int]::TryParse(
                    $seedToken,
                    [Globalization.NumberStyles]::Integer,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$seed) -and
                $seedToken -ceq $seed.ToString(
                    [Globalization.CultureInfo]::InvariantCulture)) {
                [IO.File]::Delete($file)
            }
        }
    }
}

function Enter-SharpProofFuzzEvidenceLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,

        [ValidateRange(0, 3600)]
        [int]$TimeoutSeconds = 120
    )

    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $lockPath = Join-Path $OutputDirectory '.campaign.lock'
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        try {
            # FileShare.None makes the lock a process-held lease over the
            # whole namespace, including initialization and publication.
            return [IO.FileStream]::new(
                $lockPath,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::None)
        }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out acquiring fuzz evidence lease: $lockPath"
            }
            Start-Sleep -Milliseconds 100
        }
    }
}

function Exit-SharpProofFuzzEvidenceLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [IO.FileStream]$Lease
    )

    $Lease.Dispose()
}

function Publish-SharpProofFuzzEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Json
    )

    $destination = Join-Path $OutputDirectory 'campaign.json'
    $temporary = Join-Path $OutputDirectory '.campaign.json.tmp'
    try {
        [IO.File]::WriteAllText(
            $temporary,
            $Json,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $destination, $true)
    }
    finally {
        if ([IO.File]::Exists($temporary)) {
            [IO.File]::Delete($temporary)
        }
    }
}

function Complete-SharpProofFuzzEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,

        [Parameter(Mandatory = $true)]
        [object]$Summary
    )

    $json = ($Summary | ConvertTo-Json -Depth 6) -replace "`r`n", "`n"
    Write-Output $json
    Publish-SharpProofFuzzEvidence `
        -OutputDirectory $OutputDirectory `
        -Json ($json + "`n")
    if (-not [bool]$Summary.passed) {
        $destination = Join-Path $OutputDirectory 'campaign.json'
        throw "SharpProof fuzz campaign failed. Evidence: $destination"
    }
}
