[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryPath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$Commit,

    [Parameter()]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRepository = (Resolve-Path `
    -LiteralPath $RepositoryPath `
    -ErrorAction Stop).Path
# The trusted-computing-base path list is derived from eng/acceptance/contract.json
# read out of the measured commit, and that data is what keeps a digest
# reproducible. The helper that flattens it is code, so it is dot-sourced from
# this checkout rather than read back out of the revision under measurement: a
# tool that evaluates script text supplied by the artifact it is measuring
# cannot attest to that artifact, and a tampered helper could also return a
# truncated path list and shrink the measured TCB.
$tcbHelperPath = Join-Path $PSScriptRoot 'Get-SharpProofTcbPaths.ps1'
if (-not (Test-Path -LiteralPath $tcbHelperPath -PathType Leaf)) {
    throw (
        "The release digest helper '$tcbHelperPath' is missing from this checkout.")
}
. $tcbHelperPath
$approvedMetadataRootFiles =
    [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($path in @(
        'AGENTS.md',
        'CHANGELOG.md',
        'CONTRIBUTING.md',
        'README.md',
        'SECURITY.md',
        'SEMANTICS.md')) {
    $null = $approvedMetadataRootFiles.Add($path)
}

function Invoke-GitLines {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$Operation
    )

    $output = @(& git -C $resolvedRepository @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw (
            "$Operation failed with exit code $LASTEXITCODE. " +
            ($output -join "`n"))
    }
    return $output
}

function Get-GitTreeEntries {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Revision
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-C',
            $resolvedRepository,
            '-c',
            'core.quotePath=false',
            'ls-tree',
            '-r',
            '-z',
            '--full-tree',
            $Revision)) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'Reading tracked release paths did not start Git.'
    }
    try {
        $errorTask = $process.StandardError.ReadToEndAsync()
        $output = [IO.MemoryStream]::new()
        try {
            $copyTask = $process.StandardOutput.BaseStream.CopyToAsync($output)
            $process.WaitForExit()
            $null = $copyTask.GetAwaiter().GetResult()
            $errorText = $errorTask.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) {
                throw (
                    'Reading tracked release paths failed with exit code ' +
                    "$($process.ExitCode). $errorText")
            }
            $bytes = $output.ToArray()
        }
        finally {
            $output.Dispose()
        }
    }
    finally {
        $process.Dispose()
    }

    $encoding = [Text.UTF8Encoding]::new($false, $true)
    $entries = [Collections.Generic.List[object]]::new()
    $seenPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $start = 0
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -ne 0) {
            continue
        }
        if ($index -eq $start) {
            throw 'Git returned an empty tracked release entry.'
        }
        $separator = -1
        for ($entryIndex = $start;
            $entryIndex -lt $index;
            $entryIndex++) {
            if ($bytes[$entryIndex] -eq 9) {
                $separator = $entryIndex
                break
            }
        }
        if ($separator -le $start -or $separator -eq $index - 1) {
            throw 'Git returned a malformed tracked release entry.'
        }
        $metadata = [Text.Encoding]::ASCII.GetString(
            $bytes,
            $start,
            $separator - $start)
        $firstSpace = $metadata.IndexOf([char]' ')
        $secondSpace = if ($firstSpace -ge 0) {
            $metadata.IndexOf([char]' ', $firstSpace + 1)
        }
        else {
            -1
        }
        if ($firstSpace -le 0 -or
            $secondSpace -le $firstSpace + 1 -or
            $metadata.IndexOf([char]' ', $secondSpace + 1) -ge 0) {
            throw "Git returned malformed entry metadata: '$metadata'."
        }
        $mode = $metadata.Substring(0, $firstSpace)
        $type = $metadata.Substring(
            $firstSpace + 1,
            $secondSpace - $firstSpace - 1)
        $objectId = $metadata.Substring($secondSpace + 1)
        if ($mode -notmatch '^[0-7]{6}$') {
            throw "Git returned a noncanonical entry mode: '$mode'."
        }
        if (-not $type.Equals('blob', [StringComparison]::Ordinal)) {
            throw (
                "Tracked release entry type '$type' is unsupported; " +
                'release inputs must be Git blobs.')
        }
        if ($objectId -notmatch '^[0-9a-f]{40}$') {
            throw "Git returned a noncanonical blob identity: '$objectId'."
        }
        $path = $encoding.GetString(
            $bytes,
            $separator + 1,
            $index - $separator - 1)
        if (-not $seenPaths.Add($path)) {
            throw "Git returned duplicate tracked release path '$path'."
        }
        $entries.Add([pscustomobject][ordered]@{
                Mode = $mode
                Type = $type
                ObjectId = $objectId
                Path = $path
            })
        $start = $index + 1
    }
    if ($start -ne $bytes.Length) {
        throw 'Git tracked-entry output is not NUL terminated.'
    }
    return $entries.ToArray()
}

function Get-GitBlobBytes {
    param(
        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[0-9a-f]{40}$')]
        [string]$ObjectId,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $temporaryPath = [IO.Path]::GetTempFileName()
    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = 'git'
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.ArgumentList.Add('-C')
        $startInfo.ArgumentList.Add($resolvedRepository)
        $startInfo.ArgumentList.Add('cat-file')
        $startInfo.ArgumentList.Add('blob')
        $startInfo.ArgumentList.Add($ObjectId)

        $process = [Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) {
            throw "Reading '$Path' did not start Git."
        }
        try {
            $errorTask = $process.StandardError.ReadToEndAsync()
            $stream = [IO.File]::Create($temporaryPath)
            try {
                $copyTask = $process.StandardOutput.BaseStream.CopyToAsync(
                    $stream)
                $process.WaitForExit()
                $null = $copyTask.GetAwaiter().GetResult()
            }
            finally {
                $stream.Dispose()
            }
            $errorText = $errorTask.GetAwaiter().GetResult()
            if ($process.ExitCode -ne 0) {
                throw (
                    "Reading '$Path' blob $ObjectId failed with exit code " +
                    "$($process.ExitCode). $errorText")
            }
        }
        finally {
            $process.Dispose()
        }
        return [IO.File]::ReadAllBytes($temporaryPath)
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            [IO.File]::Delete($temporaryPath)
        }
    }
}

function Test-ApprovedMetadataPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ($approvedMetadataRootFiles.Contains($Path) -or
        $Path.StartsWith('docs/', [StringComparison]::Ordinal)) {
        return $true
    }
    return [IO.Path]::GetFileName($Path).Equals(
        'README.md',
        [StringComparison]::Ordinal)
}

function Get-CanonicalDigest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Domain,

        [Parameter(Mandatory = $true)]
        [object[]]$Entries
    )

    $entriesByPath = [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
    foreach ($entry in $Entries) {
        if (-not $entriesByPath.TryAdd([string]$entry.Path, $entry)) {
            throw "Digest path is duplicated: '$($entry.Path)'."
        }
    }
    $orderedPaths = [string[]]@($entriesByPath.Keys)
    [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
    if ($orderedPaths.Count -eq 0) {
        throw "Digest domain '$Domain' has no files."
    }
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash(
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $hash.AppendData(
            [Text.Encoding]::UTF8.GetBytes("$Domain`0"))
        foreach ($path in $orderedPaths) {
            $entry = $entriesByPath[$path]
            if ([string]::IsNullOrWhiteSpace($path) -or
                $path -match '[\x00\r\n]' -or
                $path.Contains('\') -or
                $path.StartsWith(
                    '/',
                    [StringComparison]::Ordinal) -or
                $path.Split('/') -contains '..') {
                throw "Digest path is not canonical: '$path'."
            }
            $content = Get-GitBlobBytes `
                -ObjectId ([string]$entry.ObjectId) `
                -Path $path
            if ($path -eq 'SharpProof.Release.props') {
                $encoding = [Text.UTF8Encoding]::new($false, $true)
                $text = $encoding.GetString($content)
                $versionPattern =
                    '(<SharpProofPackageVersion>)[^<]*(</SharpProofPackageVersion>)'
                $regexOptions =
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant
                if ([Text.RegularExpressions.Regex]::Matches(
                        $text,
                        $versionPattern,
                        $regexOptions).Count -ne 1) {
                    throw (
                        'SharpProof.Release.props must contain exactly one ' +
                        'package version element for canonical release ' +
                        'hashing.')
                }
                $normalized = [Text.RegularExpressions.Regex]::Replace(
                    $text,
                    $versionPattern,
                    '$1__RELEASE_VERSION__$2',
                    $regexOptions)
                $content = $encoding.GetBytes($normalized)
            }
            $contentDigest = [Security.Cryptography.SHA256]::HashData(
                $content)
            $hash.AppendData(
                [Text.Encoding]::ASCII.GetBytes([string]$entry.Mode))
            $hash.AppendData([byte[]]@(0))
            $hash.AppendData(
                [Text.Encoding]::ASCII.GetBytes([string]$entry.Type))
            $hash.AppendData([byte[]]@(0))
            $hash.AppendData([Text.Encoding]::UTF8.GetBytes($path))
            $hash.AppendData([byte[]]@(0))
            $hash.AppendData($contentDigest)
        }
        return [Convert]::ToHexString(
            $hash.GetHashAndReset()).ToLowerInvariant()
    }
    finally {
        $hash.Dispose()
    }
}

if ($null -eq (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to compute release digests.'
}
$resolvedCommit = (
    Invoke-GitLines `
        -Arguments @('rev-parse', "${Commit}^{commit}") `
        -Operation 'Resolving release digest commit' |
        Select-Object -First 1).Trim()
if ($resolvedCommit -ne $Commit) {
    throw 'Release digest commit does not resolve exactly.'
}
$trackedEntries = @(Get-GitTreeEntries -Revision $Commit)
$trackedEntriesByPath =
    [Collections.Generic.Dictionary[string, object]]::new(
        [StringComparer]::Ordinal)
foreach ($entry in $trackedEntries) {
    if (-not $trackedEntriesByPath.TryAdd(
            [string]$entry.Path,
            $entry)) {
        throw "Tracked release path is duplicated: '$($entry.Path)'."
    }
}
$productionEntries = @(
    $trackedEntries |
        Where-Object {
            -not (Test-ApprovedMetadataPath -Path ([string]$_.Path))
        }
)
$acceptancePath = 'eng/acceptance/contract.json'
[object]$acceptanceEntry = $null
if (-not $trackedEntriesByPath.TryGetValue(
        $acceptancePath,
        [ref]$acceptanceEntry)) {
    throw "Required release path is not tracked: '$acceptancePath'."
}
$acceptanceJson = [Text.UTF8Encoding]::new(
    $false,
    $true).GetString(
        (Get-GitBlobBytes `
            -ObjectId ([string]$acceptanceEntry.ObjectId) `
            -Path $acceptancePath)) |
    ConvertFrom-Json
$tcbPaths = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$tcbEntries = [Collections.Generic.List[object]]::new()
foreach ($canonicalPath in @(Get-SharpProofTcbPaths `
        -Contract $acceptanceJson `
        -IncludeAcceptanceContract)) {
    [object]$tcbEntry = $null
    if (-not $trackedEntriesByPath.TryGetValue(
            $canonicalPath,
            [ref]$tcbEntry)) {
        throw "TCB path is not tracked: '$canonicalPath'."
    }
    if (-not $tcbPaths.Add($canonicalPath)) {
        throw "TCB path is declared more than once: '$canonicalPath'."
    }
    $tcbEntries.Add($tcbEntry)
}

$result = [pscustomobject][ordered]@{
    schemaVersion = 1
    commit = $Commit
    productionDigestSha256 = Get-CanonicalDigest `
        -Domain 'SharpProof.production.v1' `
        -Entries $productionEntries
    productionFileCount = $productionEntries.Count
    trustedComputingBaseDigestSha256 = Get-CanonicalDigest `
        -Domain 'SharpProof.tcb.v1' `
        -Entries $tcbEntries.ToArray()
    trustedComputingBaseFileCount = $tcbPaths.Count
}
$json = $result | ConvertTo-Json -Compress
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Write-Output $json
    return
}

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputPath))
}
$outputDirectory = [IO.Path]::GetDirectoryName($resolvedOutput)
if ([string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw "OutputPath has no parent directory: '$OutputPath'."
}
[IO.Directory]::CreateDirectory($outputDirectory) |
    Out-Null
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + "`n",
    [Text.UTF8Encoding]::new($false))
