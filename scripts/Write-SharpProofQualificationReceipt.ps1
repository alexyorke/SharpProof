[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('acceptance-debug', 'acceptance-release', 'coverage', 'mutation',
        'package-consumers', 'pilots', 'release-configuration',
        'portable-linux', 'portable-windows', 'portable-macos')]
    [string]$Gate,

    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$ReceiptDirectory = 'artifacts/release-qualification/qualification-receipts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseBundle.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')
. (Join-Path $PSScriptRoot 'Test-SharpProofPilotReport.ps1')

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
        throw "Qualification evidence '$Name' must be an Int32 JSON number."
    }
    return $value
}

function Get-ExactJsonInt64 {
    param(
        [Parameter(Mandatory = $true)]
        [Text.Json.JsonElement]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $property = $Object.GetProperty($Name)
    $value = 0L
    if ($property.ValueKind -ne [Text.Json.JsonValueKind]::Number -or
        -not $property.TryGetInt64([ref]$value)) {
        throw "Qualification evidence '$Name' must be an Int64 JSON number."
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
        throw "Qualification evidence '$Name' must be a Boolean JSON token."
    }
    return $property.GetBoolean()
}

$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$resolvedEvidence = (Resolve-Path -LiteralPath $EvidencePath).Path
$relativeEvidence = [IO.Path]::GetRelativePath(
    $repositoryRoot,
    $resolvedEvidence).Replace('\', '/')
if ($relativeEvidence.StartsWith('../', [StringComparison]::Ordinal) -or
    [IO.Path]::IsPathRooted($relativeEvidence)) {
    throw 'Qualification gate evidence must remain inside the repository.'
}
$evidenceBytes = [IO.File]::ReadAllBytes($resolvedEvidence)
$evidenceLength = [int64]$evidenceBytes.LongLength
$evidenceSha256 = [BitConverter]::ToString(
    [Security.Cryptography.SHA256]::HashData($evidenceBytes)).Replace('-', '').ToLowerInvariant()
$evidenceText = [Text.UTF8Encoding]::new($false, $true).GetString($evidenceBytes)
$evidenceDocument = [Text.Json.JsonDocument]::Parse($evidenceText)
$packageByteValues = [Collections.Generic.List[long]]::new()
try {
    $evidenceRoot = $evidenceDocument.RootElement
    if ($evidenceRoot.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
        throw 'Qualification gate evidence must be a JSON object.'
    }
    if ($Gate -ne 'pilots') {
        $schemaVersion = Get-ExactJsonInt32 $evidenceRoot 'schemaVersion'
    }
    if ($Gate -eq 'coverage') {
        $coveragePassed = Get-ExactJsonBoolean $evidenceRoot 'passed'
    }
    if ($Gate -eq 'mutation') {
        $mutationCount = Get-ExactJsonInt32 $evidenceRoot 'mutationCount'
        $killedCount = Get-ExactJsonInt32 $evidenceRoot 'killedCount'
    }
    if ($Gate -in @(
            'package-consumers', 'pilots', 'portable-linux',
            'portable-windows', 'portable-macos')) {
        $packageArray = $evidenceRoot.GetProperty('packageArtifacts')
        if ($packageArray.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
            throw 'Qualification packageArtifacts must be a JSON array.'
        }
        foreach ($packageElement in $packageArray.EnumerateArray()) {
            $packageByteValues.Add(
                (Get-ExactJsonInt64 $packageElement 'bytes'))
        }
    }
}
finally {
    $evidenceDocument.Dispose()
}
$evidence = $evidenceText | ConvertFrom-Json -ErrorAction Stop
$packageArtifacts = @()
if ($Gate -in @(
        'package-consumers', 'pilots', 'portable-linux',
        'portable-windows', 'portable-macos')) {
    $packageArtifactRows = [Collections.Generic.List[object]]::new()
    $packageIndex = 0
    foreach ($artifact in @($evidence.packageArtifacts)) {
        $fileName = [string]$artifact.fileName
        $bytes = $packageByteValues[$packageIndex]
        $packageIndex++
        $sha256 = [string]$artifact.sha256
        if ([IO.Path]::GetFileName($fileName) -cne $fileName -or
            $fileName -notmatch '\.(?:nupkg|snupkg)$' -or
            $bytes -le 0 -or
            $sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "Qualification package evidence is malformed: '$fileName'."
        }
        $packageArtifactRows.Add([ordered]@{
            fileName = $fileName
            bytes = $bytes
            sha256 = $sha256
        })
    }
    $packageArtifacts = @($packageArtifactRows.ToArray() | Sort-Object fileName)
    if ($packageArtifacts.Count -ne 6 -or
        @($packageArtifacts.fileName | Group-Object).Count -ne 6) {
        throw 'Qualification evidence must bind exactly six unique package artifacts.'
    }
}
$valid = switch -Regex ($Gate) {
    '^acceptance-(?:debug|release)$' {
        $schemaVersion -eq 1 -and
        [string]$evidence.command -ceq 'acceptance' -and
        [string]$evidence.configuration -ceq $Gate.Substring(11) -and
        [string]$evidence.status -ceq 'passed' -and
        [string]$evidence.commit -ceq $commit
    }
    '^portable-(?:linux|windows|macos)$' {
        $schemaVersion -eq 2 -and
        [string]$evidence.status -ceq 'passed' -and
        [string]$evidence.commit -ceq $commit -and
        [string]$evidence.osFamily -ceq $Gate.Substring(9)
    }
    '^release-configuration$' {
        $schemaVersion -eq 1 -and
        [string]$evidence.commit -ceq $commit
    }
    'coverage' {
        $schemaVersion -eq 1 -and
        $coveragePassed -and
        [string]$evidence.commit -ceq $commit
    }
    'mutation' {
        $schemaVersion -eq 2 -and
        [string]$evidence.selection -ceq 'full' -and
        [string]$evidence.commit -ceq $commit -and
        $mutationCount -gt 0 -and
        $mutationCount -eq $killedCount
    }
    'package-consumers' {
        $schemaVersion -eq 2 -and
        [string]$evidence.status -ceq 'passed' -and
        [string]$evidence.commit -ceq $commit
    }
    'pilots' {
        [string]$evidence.reviewStatus -ceq 'Reviewed' -and
        (Test-SharpProofPilotReport -Report $evidence -ExpectedCommit $commit `
            -RepositoryRoot $repositoryRoot)
    }
}
if (-not $valid) {
    throw "Qualification evidence is incomplete, stale, or failed: '$Gate'."
}
$receiptCandidate = if ([IO.Path]::IsPathRooted($ReceiptDirectory)) {
    $ReceiptDirectory
}
else {
    Join-Path $repositoryRoot $ReceiptDirectory
}
$receiptDirectory = [IO.Path]::GetFullPath($receiptCandidate)
if (-not $receiptDirectory.StartsWith(
        $repositoryRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::Ordinal)) {
    throw 'ReceiptDirectory must remain inside the repository.'
}
[IO.Directory]::CreateDirectory($receiptDirectory) | Out-Null
$receipt = [ordered]@{
    schemaVersion = 2
    gate = $Gate
    status = 'passed'
    commit = $commit
    evidence = [ordered]@{
        path = $relativeEvidence
        bytes = $evidenceLength
        sha256 = $evidenceSha256
    }
}
if ($packageArtifacts.Count -ne 0) {
    $receipt.packageArtifacts = $packageArtifacts
}
if ($Gate -eq 'pilots') {
    $receipt.pilotEvidence = @($evidence.pilots | Sort-Object id | ForEach-Object {
            [ordered]@{ id = [string]$_.id; evidence = @($_.evidence) }
        })
}
Write-SharpProofAtomicText `
    -Path (Join-Path $receiptDirectory "$Gate.json") `
    -Value (($receipt | ConvertTo-Json -Depth 5) + "`n")
