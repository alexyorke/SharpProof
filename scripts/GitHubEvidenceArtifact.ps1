Set-StrictMode -Version Latest

function Get-SharpProofGitHubEvidenceHeaders {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $token = if (-not [string]::IsNullOrWhiteSpace(
            $env:SHARPPROOF_GITHUB_TOKEN)) {
        $env:SHARPPROOF_GITHUB_TOKEN
    }
    else {
        $env:GITHUB_TOKEN
    }
    if ([string]::IsNullOrWhiteSpace($token) -or
        $token -match '[\r\n]') {
        throw (
            "$Owner requires SHARPPROOF_GITHUB_TOKEN or GITHUB_TOKEN for " +
            'authenticated GitHub artifact evidence.')
    }

    return @{
        Accept = 'application/vnd.github+json'
        Authorization = "Bearer $token"
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent' = 'SharpProof-release-evidence-validator'
    }
}

function Get-SharpProofExactArtifactProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $property = $Value.PSObject.Properties |
        Where-Object { $_.Name -ceq $Name } |
        Select-Object -First 1
    if ($null -eq $property) {
        throw "$Owner is missing required property '$Name'."
    }
    return ,$property.Value
}

function Assert-SharpProofArtifactSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($Value -notmatch '^[0-9a-f]{64}$' -or
        $Value -eq ('0' * 64)) {
        throw "$Owner must be a nonzero lowercase SHA-256 digest."
    }
}

function ConvertTo-SharpProofArtifactUtcTimestamp {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($Value -is [DateTimeOffset]) {
        return ([DateTimeOffset]$Value).ToUniversalTime()
    }
    if ($Value -is [DateTime]) {
        return [DateTimeOffset]::new(
            ([DateTime]$Value).ToUniversalTime())
    }
    $text = [string]$Value
    $parsed = [DateTimeOffset]::MinValue
    if ($text -notmatch
            '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' -or
        -not [DateTimeOffset]::TryParse(
            $text,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor
                [Globalization.DateTimeStyles]::AdjustToUniversal,
            [ref]$parsed)) {
        throw "$Owner must be an exact UTC timestamp."
    }
    return $parsed
}

function Read-SharpProofBoundedArtifactEntry {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Stream]$Stream,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [long]::MaxValue)]
        [long]$ExpectedBytes,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [long]::MaxValue)]
        [long]$MaximumBytes,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($ExpectedBytes -gt $MaximumBytes) {
        throw "$Owner has an invalid declared size."
    }
    $memory = [IO.MemoryStream]::new()
    try {
        $buffer = [byte[]]::new(81920)
        $total = 0L
        while (($read = $Stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += [long]$read
            if ($total -gt $ExpectedBytes -or
                $total -gt $MaximumBytes) {
                throw "$Owner expands beyond its declared size limit."
            }
            $memory.Write($buffer, 0, $read)
        }
        if ($total -ne $ExpectedBytes) {
            throw "$Owner does not match its declared uncompressed size."
        }
        return ,$memory.ToArray()
    }
    finally {
        $memory.Dispose()
    }
}

function Get-SharpProofBoundedArtifactEntryHash {
    param(
        [Parameter(Mandatory = $true)]
        [IO.Stream]$Stream,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [long]::MaxValue)]
        [long]$ExpectedBytes,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [long]::MaxValue)]
        [long]$MaximumBytes,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    if ($ExpectedBytes -gt $MaximumBytes) {
        throw "$Owner has an invalid declared size."
    }
    $hasher = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $buffer = [byte[]]::new(81920)
        $total = 0L
        while (($read = $Stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $total += [long]$read
            if ($total -gt $ExpectedBytes -or
                $total -gt $MaximumBytes) {
                throw "$Owner expands beyond its declared size limit."
            }
            $hasher.AppendData($buffer, 0, $read)
        }
        if ($total -ne $ExpectedBytes) {
            throw "$Owner does not match its declared uncompressed size."
        }
        return [Convert]::ToHexString(
            $hasher.GetHashAndReset()).ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Assert-SharpProofReleaseManifestArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [object]$Manifest,

        [Parameter(Mandatory = $true)]
        [string]$RecordPath,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $artifactsValue = Get-SharpProofExactArtifactProperty `
        -Value $Manifest `
        -Name 'artifacts' `
        -Owner "$Owner release manifest"
    if ($artifactsValue -isnot [Array]) {
        throw "$Owner release manifest 'artifacts' is not an array."
    }
    $bindings = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
    foreach ($artifact in @($artifactsValue)) {
        $fileName = [string](Get-SharpProofExactArtifactProperty `
                -Value $artifact `
                -Name 'fileName' `
                -Owner "$Owner release artifact")
        $bytes = [long](Get-SharpProofExactArtifactProperty `
                -Value $artifact `
                -Name 'bytes' `
                -Owner "$Owner release artifact '$fileName'")
        $sha256 = [string](Get-SharpProofExactArtifactProperty `
                -Value $artifact `
                -Name 'sha256' `
                -Owner "$Owner release artifact '$fileName'")
        if ([string]::IsNullOrWhiteSpace($fileName) -or
            $fileName.Contains('\') -or
            $fileName.Contains('/') -or
            $fileName -in @('.', '..') -or
            $bytes -le 0 -or
            -not $bindings.TryAdd(
                $fileName,
                [pscustomobject]@{
                    bytes = $bytes
                    sha256 = $sha256
                })) {
            throw "$Owner release manifest contains an invalid artifact."
        }
        Assert-SharpProofArtifactSha256 `
            -Value $sha256 `
            -Owner "$Owner release artifact '$fileName'"
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $fileEntries = @(
            $archive.Entries |
                Where-Object {
                    -not [string]::IsNullOrEmpty($_.Name)
                }
        )
        $expectedFileCount = $bindings.Count + 2
        if ($archive.Entries.Count -ne $expectedFileCount -or
            $fileEntries.Count -ne $expectedFileCount -or
            @($fileEntries |
                Where-Object {
                    $_.FullName -ceq $RecordPath
                }).Count -ne 1 -or
            @($fileEntries |
                Where-Object {
                    $_.FullName -ceq 'SHA256SUMS'
                }).Count -ne 1) {
            throw (
                "$Owner archive must contain exactly the release manifest, " +
                'SHA256SUMS, and every manifest artifact.')
        }
        foreach ($binding in $bindings.GetEnumerator()) {
            $matches = @(
                $fileEntries |
                    Where-Object {
                        $_.FullName -ceq $binding.Key
                    }
            )
            if ($matches.Count -ne 1) {
                throw (
                    "$Owner archive is missing release artifact " +
                    "'$($binding.Key)'.")
            }
            $entry = $matches[0]
            if ($entry.Length -ne [long]$binding.Value.bytes) {
                throw (
                    "$Owner release artifact '$($binding.Key)' has an " +
                    'unexpected size.')
            }
            $stream = $entry.Open()
            try {
                $actualSha256 = Get-SharpProofBoundedArtifactEntryHash `
                    -Stream $stream `
                    -ExpectedBytes ([long]$binding.Value.bytes) `
                    -MaximumBytes 1GB `
                    -Owner "$Owner release artifact '$($binding.Key)'"
            }
            finally {
                $stream.Dispose()
            }
            if ($actualSha256 -cne [string]$binding.Value.sha256) {
                throw (
                    "$Owner release artifact '$($binding.Key)' does not " +
                    'match its manifest digest.')
            }
        }
        $sumsEntry = @(
            $fileEntries |
                Where-Object {
                    $_.FullName -ceq 'SHA256SUMS'
                }
        )[0]
        if ($sumsEntry.Length -le 0 -or
            $sumsEntry.Length -gt 1MB) {
            throw "$Owner SHA256SUMS has an invalid size."
        }
        $sumsStream = $sumsEntry.Open()
        try {
            $sumsBytes = Read-SharpProofBoundedArtifactEntry `
                -Stream $sumsStream `
                -ExpectedBytes $sumsEntry.Length `
                -MaximumBytes 1MB `
                -Owner "$Owner SHA256SUMS"
        }
        finally {
            $sumsStream.Dispose()
        }
        try {
            $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
            $actualSums = $strictUtf8.GetString($sumsBytes)
        }
        catch {
            throw "$Owner SHA256SUMS is not valid strict UTF-8."
        }
        $expectedSums = (
            @($artifactsValue |
                ForEach-Object {
                    [string](Get-SharpProofExactArtifactProperty `
                        -Value $_ `
                        -Name 'sha256' `
                        -Owner "$Owner release artifact") +
                    '  ' +
                    [string](Get-SharpProofExactArtifactProperty `
                        -Value $_ `
                        -Name 'fileName' `
                        -Owner "$Owner release artifact")
                }) -join "`n") + "`n"
        if ($actualSums -cne $expectedSums) {
            throw "$Owner SHA256SUMS does not match the release manifest."
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-SharpProofQualificationReceiptArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [object]$Record,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    $receipts = Get-SharpProofExactArtifactProperty `
        -Value $Record `
        -Name 'gateReceipts' `
        -Owner "$Owner qualification record"
    $expected = [Collections.Generic.Dictionary[string, string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $receipts.PSObject.Properties) {
        if ($null -eq $property.Value) {
            continue
        }
        $digest = [string]$property.Value
        Assert-SharpProofArtifactSha256 `
            -Value $digest `
            -Owner "$Owner gate receipt '$($property.Name)'"
        $path = "qualification-receipts/$($property.Name).json"
        if (-not $expected.TryAdd($path, $digest)) {
            throw "$Owner qualification record has duplicate gate receipts."
        }
    }
    if ($expected.Count -eq 0) {
        throw "$Owner qualification record has no authenticated gate receipts."
    }
    $recordTag = [string](Get-SharpProofExactArtifactProperty `
            -Value $Record `
            -Name 'tag' `
            -Owner "$Owner qualification record")
    $recordCommit = [string](Get-SharpProofExactArtifactProperty `
            -Value $Record `
            -Name 'releaseCommit' `
            -Owner "$Owner qualification record")
    $recordRun = Get-SharpProofExactArtifactProperty `
        -Value $Record `
        -Name 'run' `
        -Owner "$Owner qualification record"
    $runProperties = @(
        'provider',
        'repository',
        'runId',
        'runAttempt',
        'workflowRef',
        'job',
        'ref',
        'sha'
    )
    $evidencePaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)

    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($binding in $expected.GetEnumerator()) {
            $matches = @(
                $archive.Entries |
                    Where-Object {
                        $_.FullName -ceq $binding.Key -and
                        -not [string]::IsNullOrEmpty($_.Name)
                    }
            )
            if ($matches.Count -ne 1) {
                throw (
                    "$Owner qualification archive is missing gate receipt " +
                    "'$($binding.Key)'.")
            }
            $entry = $matches[0]
            if ($entry.Length -le 0 -or
                $entry.Length -gt 16MB) {
                throw (
                    "$Owner qualification receipt '$($binding.Key)' has an " +
                    'invalid size.')
            }
            $stream = $entry.Open()
            try {
                $receiptBytes = Read-SharpProofBoundedArtifactEntry `
                    -Stream $stream `
                    -ExpectedBytes $entry.Length `
                    -MaximumBytes 16MB `
                    -Owner "$Owner qualification receipt '$($binding.Key)'"
            }
            finally {
                $stream.Dispose()
            }
            $actualSha256 = [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData(
                    [byte[]]$receiptBytes)).ToLowerInvariant()
            if ($actualSha256 -cne $binding.Value) {
                throw (
                    "$Owner qualification receipt '$($binding.Key)' does " +
                    'not match the qualification record.')
            }
            try {
                $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
                $receipt = $strictUtf8.GetString($receiptBytes) |
                    ConvertFrom-Json -Depth 100 -ErrorAction Stop
            }
            catch {
                throw (
                    "$Owner qualification receipt '$($binding.Key)' is not " +
                    'valid strict UTF-8 JSON.')
            }
            $gateName = [IO.Path]::GetFileNameWithoutExtension($binding.Key)
            $receiptRun = Get-SharpProofExactArtifactProperty `
                -Value $receipt `
                -Name 'run' `
                -Owner "$Owner qualification receipt '$gateName'"
            $receiptEvidence = Get-SharpProofExactArtifactProperty `
                -Value $receipt `
                -Name 'evidence' `
                -Owner "$Owner qualification receipt '$gateName'"
            $humanDocumentSha = Get-SharpProofExactArtifactProperty `
                -Value $receipt `
                -Name 'humanEvidenceDocumentSha256' `
                -Owner "$Owner qualification receipt '$gateName'"
            if ([int](Get-SharpProofExactArtifactProperty `
                    -Value $receipt `
                    -Name 'schemaVersion' `
                    -Owner "$Owner qualification receipt '$gateName'") -ne 2 -or
                [string](Get-SharpProofExactArtifactProperty `
                    -Value $receipt `
                    -Name 'tag' `
                    -Owner "$Owner qualification receipt '$gateName'") -cne
                        $recordTag -or
                [string](Get-SharpProofExactArtifactProperty `
                    -Value $receipt `
                    -Name 'releaseCommit' `
                    -Owner "$Owner qualification receipt '$gateName'") -cne
                        $recordCommit -or
                [string](Get-SharpProofExactArtifactProperty `
                    -Value $receipt `
                    -Name 'gate' `
                    -Owner "$Owner qualification receipt '$gateName'") -cne
                        $gateName -or
                [string](Get-SharpProofExactArtifactProperty `
                    -Value $receipt `
                    -Name 'status' `
                    -Owner "$Owner qualification receipt '$gateName'") -cne
                        'passed' -or
                $null -ne $humanDocumentSha) {
                throw (
                    "$Owner qualification receipt '$gateName' has invalid " +
                    'schema, release identity, gate, or status.')
            }
            foreach ($propertyName in $runProperties) {
                if ([string](Get-SharpProofExactArtifactProperty `
                        -Value $receiptRun `
                        -Name $propertyName `
                        -Owner "$Owner qualification receipt '$gateName' run") -cne
                    [string](Get-SharpProofExactArtifactProperty `
                        -Value $recordRun `
                        -Name $propertyName `
                        -Owner "$Owner qualification record run")) {
                    throw (
                        "$Owner qualification receipt '$gateName' does not " +
                        'match the authenticated qualification run.')
                }
            }
            $evidencePath = [string](
                Get-SharpProofExactArtifactProperty `
                    -Value $receiptEvidence `
                    -Name 'path' `
                    -Owner "$Owner qualification receipt '$gateName' evidence")
            $evidenceSha256 = [string](
                Get-SharpProofExactArtifactProperty `
                    -Value $receiptEvidence `
                    -Name 'sha256' `
                    -Owner "$Owner qualification receipt '$gateName' evidence")
            $evidencePrefix = 'artifacts/release-qualification/'
            if (-not $evidencePath.StartsWith(
                    $evidencePrefix,
                    [StringComparison]::Ordinal) -or
                $evidencePath.Contains('\') -or
                $evidencePath.Split('/') -contains '..') {
                throw (
                    "$Owner qualification receipt '$gateName' has an unsafe " +
                    'gate-evidence path.')
            }
            $archiveEvidencePath = $evidencePath.Substring(
                $evidencePrefix.Length)
            if ([string]::IsNullOrWhiteSpace($archiveEvidencePath) -or
                $archiveEvidencePath -ceq 'qualification.json' -or
                $archiveEvidencePath.StartsWith(
                    'qualification-receipts/',
                    [StringComparison]::Ordinal) -or
                -not $evidencePaths.Add($archiveEvidencePath)) {
                throw (
                    "$Owner qualification receipt '$gateName' must identify " +
                    'one unique retained gate-evidence file.')
            }
            Assert-SharpProofArtifactSha256 `
                -Value $evidenceSha256 `
                -Owner "$Owner qualification receipt '$gateName' evidence"
            $evidenceMatches = @(
                $archive.Entries |
                    Where-Object {
                        $_.FullName -ceq $archiveEvidencePath -and
                        -not [string]::IsNullOrEmpty($_.Name)
                    }
            )
            if ($evidenceMatches.Count -ne 1) {
                throw (
                    "$Owner qualification archive is missing gate evidence " +
                    "'$archiveEvidencePath'.")
            }
            $evidenceEntry = $evidenceMatches[0]
            if ($evidenceEntry.Length -le 0 -or
                $evidenceEntry.Length -gt 1GB) {
                throw (
                    "$Owner gate evidence '$archiveEvidencePath' has an " +
                    'invalid size.')
            }
            $evidenceStream = $evidenceEntry.Open()
            try {
                $actualEvidenceSha256 =
                    Get-SharpProofBoundedArtifactEntryHash `
                        -Stream $evidenceStream `
                        -ExpectedBytes $evidenceEntry.Length `
                        -MaximumBytes 1GB `
                        -Owner "$Owner gate evidence '$archiveEvidencePath'"
            }
            finally {
                $evidenceStream.Dispose()
            }
            if ($actualEvidenceSha256 -cne $evidenceSha256) {
                throw (
                    "$Owner gate evidence '$archiveEvidencePath' does not " +
                    'match its receipt.')
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-SharpProofGitHubArtifactRecord {
    param(
        [Parameter(Mandatory = $true)]
        [ValidatePattern(
            '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?/[A-Za-z0-9_.-]{1,100}$')]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [long]::MaxValue)]
        [long]$RunId,

        [Parameter(Mandatory = $true)]
        [ValidateRange(1, [int]::MaxValue)]
        [int]$RunAttempt,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[0-9a-f]{40}$')]
        [string]$SourceCommit,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$')]
        [string]$ArtifactName,

        [Parameter(Mandatory = $true)]
        [string]$ArchiveSha256,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$')]
        [string]$RecordPath,

        [Parameter(Mandatory = $true)]
        [string]$RecordSha256,

        [Parameter()]
        [ValidateRange(1, [long]::MaxValue)]
        [long]$MaximumArchiveBytes = 2GB,

        [Parameter()]
        [switch]$RequireSingleRecord,

        [Parameter()]
        [switch]$VerifyReleaseManifestArtifacts,

        [Parameter()]
        [switch]$VerifyQualificationReceipts,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$AttemptStartedAt,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$AttemptCompletedAt,

        [Parameter(Mandatory = $true)]
        [string]$Owner
    )

    Assert-SharpProofArtifactSha256 `
        -Value $ArchiveSha256 `
        -Owner "$Owner archive"
    Assert-SharpProofArtifactSha256 `
        -Value $RecordSha256 `
        -Owner "$Owner record"
    if ($RecordPath.Contains('\') -or
        $RecordPath.StartsWith(
            '/',
            [StringComparison]::Ordinal) -or
        $RecordPath.Split('/') -contains '..') {
        throw "$Owner record path is not a safe artifact-relative path."
    }

    $headers = Get-SharpProofGitHubEvidenceHeaders -Owner $Owner
    $escapedName = [Uri]::EscapeDataString($ArtifactName)
    $listUri = (
        "https://api.github.com/repos/$Repository/actions/runs/$RunId/" +
        "artifacts?name=$escapedName&per_page=100")
    try {
        $response = Invoke-RestMethod `
            -Uri $listUri `
            -Method Get `
            -Headers $headers `
            -MaximumRedirection 0 `
            -TimeoutSec 30 `
            -ErrorAction Stop
    }
    catch {
        throw (
            "$Owner artifact inventory could not be authenticated: " +
            $_.Exception.Message)
    }

    $totalCount = [int](Get-SharpProofExactArtifactProperty `
            -Value $response `
            -Name 'total_count' `
            -Owner "$Owner artifact API response")
    $artifactsValue = Get-SharpProofExactArtifactProperty `
        -Value $response `
        -Name 'artifacts' `
        -Owner "$Owner artifact API response"
    if ($artifactsValue -isnot [Array]) {
        throw "$Owner artifact API response 'artifacts' is not an array."
    }
    $artifacts = @($artifactsValue)
    if ($totalCount -ne 1 -or $artifacts.Count -ne 1) {
        throw (
            "$Owner must identify exactly one current-attempt GitHub " +
            "artifact named '$ArtifactName'.")
    }

    $artifact = $artifacts[0]
    $artifactId = [long](Get-SharpProofExactArtifactProperty `
            -Value $artifact `
            -Name 'id' `
            -Owner "$Owner artifact")
    $apiName = [string](Get-SharpProofExactArtifactProperty `
            -Value $artifact `
            -Name 'name' `
            -Owner "$Owner artifact")
    $expired = Get-SharpProofExactArtifactProperty `
        -Value $artifact `
        -Name 'expired' `
        -Owner "$Owner artifact"
    $artifactSize = [long](Get-SharpProofExactArtifactProperty `
            -Value $artifact `
            -Name 'size_in_bytes' `
            -Owner "$Owner artifact")
    $digest = [string](Get-SharpProofExactArtifactProperty `
            -Value $artifact `
            -Name 'digest' `
            -Owner "$Owner artifact")
    $archiveUrl = [string](Get-SharpProofExactArtifactProperty `
            -Value $artifact `
            -Name 'archive_download_url' `
            -Owner "$Owner artifact")
    $artifactCreatedAt = ConvertTo-SharpProofArtifactUtcTimestamp `
        -Value (Get-SharpProofExactArtifactProperty `
            -Value $artifact `
            -Name 'created_at' `
            -Owner "$Owner artifact") `
        -Owner "$Owner artifact created_at"
    $artifactUpdatedAt = ConvertTo-SharpProofArtifactUtcTimestamp `
        -Value (Get-SharpProofExactArtifactProperty `
            -Value $artifact `
            -Name 'updated_at' `
            -Owner "$Owner artifact") `
        -Owner "$Owner artifact updated_at"
    $workflowRun = Get-SharpProofExactArtifactProperty `
        -Value $artifact `
        -Name 'workflow_run' `
        -Owner "$Owner artifact"
    $artifactRunId = [long](Get-SharpProofExactArtifactProperty `
            -Value $workflowRun `
            -Name 'id' `
            -Owner "$Owner artifact workflow run")
    $artifactSourceCommit = [string](
        Get-SharpProofExactArtifactProperty `
            -Value $workflowRun `
            -Name 'head_sha' `
            -Owner "$Owner artifact workflow run")
    $expectedArchiveUrl = (
        "https://api.github.com/repos/$Repository/actions/artifacts/" +
        "$artifactId/zip")
    if ($artifactId -le 0 -or
        $apiName -cne $ArtifactName -or
        $expired -isnot [bool] -or
        [bool]$expired -or
        $artifactSize -le 0 -or
        $artifactSize -gt $MaximumArchiveBytes -or
        $digest -cne "sha256:$ArchiveSha256" -or
        $archiveUrl -cne $expectedArchiveUrl -or
        $artifactRunId -ne $RunId -or
        $artifactSourceCommit -cne $SourceCommit -or
        $AttemptStartedAt -gt $AttemptCompletedAt -or
        $artifactCreatedAt -lt $AttemptStartedAt -or
        $artifactCreatedAt -gt $AttemptCompletedAt -or
        $artifactUpdatedAt -lt $artifactCreatedAt -or
        $artifactUpdatedAt -gt $AttemptCompletedAt) {
        throw (
            "$Owner artifact metadata must match the exact name, digest, " +
            'workflow attempt timestamps, source commit, and unexpired ' +
            'archive URL.')
    }

    $temporaryParent = Join-Path `
        ([IO.Path]::GetTempPath()) `
        'SharpProof.GitHubEvidenceArtifacts'
    [IO.Directory]::CreateDirectory($temporaryParent) |
        Out-Null
    $temporaryDirectory = Join-Path `
        $temporaryParent `
        ([Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($temporaryDirectory) |
        Out-Null
    $archivePath = Join-Path $temporaryDirectory 'artifact.zip'
    try {
        try {
            Invoke-WebRequest `
                -Uri $archiveUrl `
                -Method Get `
                -Headers $headers `
                -MaximumRedirection 5 `
                -TimeoutSec 60 `
                -OutFile $archivePath `
                -ErrorAction Stop |
                Out-Null
        }
        catch {
            throw (
                "$Owner artifact archive could not be downloaded: " +
                $_.Exception.Message)
        }
        if (-not [IO.File]::Exists($archivePath)) {
            throw "$Owner artifact download produced no archive."
        }
        $downloadedLength = ([IO.FileInfo]::new($archivePath)).Length
        if ($downloadedLength -eq 0 -or
            $downloadedLength -gt $MaximumArchiveBytes) {
            throw "$Owner artifact download has an invalid size."
        }
        $downloadedSha256 = (Get-FileHash `
            -LiteralPath $archivePath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($downloadedSha256 -cne $ArchiveSha256) {
            throw "$Owner downloaded archive does not match its API digest."
        }

        Add-Type -AssemblyName System.IO.Compression
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            if ($archive.Entries.Count -gt 512) {
                throw "$Owner archive contains too many ZIP entries."
            }
            $entryNames = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::OrdinalIgnoreCase)
            $fileEntryCount = 0
            foreach ($candidate in $archive.Entries) {
                $candidatePath = [string]$candidate.FullName
                $candidateSegments = $candidatePath.Split('/')
                $externalAttributeBits = [BitConverter]::ToUInt32(
                    [BitConverter]::GetBytes(
                        [int]$candidate.ExternalAttributes),
                    0)
                $unixMode = (
                    ($externalAttributeBits -shr 16) -band 0xf000)
                if ([string]::IsNullOrWhiteSpace($candidatePath) -or
                    $candidatePath.Contains('\') -or
                    $candidatePath.StartsWith(
                        '/',
                        [StringComparison]::Ordinal) -or
                    $candidateSegments -contains '..' -or
                    -not $entryNames.Add($candidatePath) -or
                    $unixMode -eq 0xa000) {
                    throw "$Owner archive contains an unsafe ZIP entry."
                }
                if (-not [string]::IsNullOrEmpty($candidate.Name)) {
                    $fileEntryCount++
                }
            }
            if ($RequireSingleRecord -and $fileEntryCount -ne 1) {
                throw "$Owner archive must contain only its evidence record."
            }
            $matchingEntries = @(
                $archive.Entries |
                    Where-Object {
                        $_.FullName -ceq $RecordPath -and
                        -not [string]::IsNullOrEmpty($_.Name)
                    }
            )
            if ($matchingEntries.Count -ne 1) {
                throw (
                    "$Owner archive must contain exactly one '$RecordPath' " +
                    'record.')
            }
            $entry = $matchingEntries[0]
            if ($entry.Length -le 0 -or
                $entry.Length -gt 16MB -or
                $entry.CompressedLength -gt 16MB) {
                throw "$Owner artifact record has an invalid size."
            }
            $recordStream = $entry.Open()
            try {
                $recordBytes = Read-SharpProofBoundedArtifactEntry `
                    -Stream $recordStream `
                    -ExpectedBytes $entry.Length `
                    -MaximumBytes 16MB `
                    -Owner "$Owner artifact record"
            }
            finally {
                $recordStream.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }

        $recordDigest = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData(
                [byte[]]$recordBytes)).ToLowerInvariant()
        if ($recordDigest -cne $RecordSha256) {
            throw "$Owner record does not match its declared digest."
        }
        try {
            $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
            $recordJson = $strictUtf8.GetString($recordBytes)
            $record = $recordJson |
                ConvertFrom-Json -Depth 100 -ErrorAction Stop
        }
        catch {
            throw "$Owner artifact record is not valid strict UTF-8 JSON."
        }
        if ($VerifyReleaseManifestArtifacts) {
            Assert-SharpProofReleaseManifestArchive `
                -ArchivePath $archivePath `
                -Manifest $record `
                -RecordPath $RecordPath `
                -Owner $Owner
        }
        if ($VerifyQualificationReceipts) {
            Assert-SharpProofQualificationReceiptArchive `
                -ArchivePath $archivePath `
                -Record $record `
                -Owner $Owner
        }
        return $record
    }
    finally {
        $resolvedParent = [IO.Path]::GetFullPath($temporaryParent)
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryDirectory)
        $relative = [IO.Path]::GetRelativePath(
            $resolvedParent,
            $resolvedTemporary)
        if ([IO.Path]::IsPathRooted($relative) -or
            $relative -eq '.' -or
            $relative -eq '..' -or
            $relative.StartsWith(
                '..' + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::Ordinal)) {
            throw 'Refusing to remove an unexpected artifact directory.'
        }
        if ([IO.Directory]::Exists($resolvedTemporary)) {
            foreach ($file in [IO.Directory]::EnumerateFiles(
                    $resolvedTemporary,
                    '*',
                    [IO.SearchOption]::AllDirectories)) {
                [IO.File]::SetAttributes(
                    $file,
                    [IO.FileAttributes]::Normal)
            }
            [IO.Directory]::Delete($resolvedTemporary, $true)
        }
    }
}
