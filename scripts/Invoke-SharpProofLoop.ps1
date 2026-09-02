[CmdletBinding()]
param(
    [Parameter(
        Mandatory = $true,
        Position = 0,
        ValueFromRemainingArguments = $true)]
    [string[]]$LoopArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($LoopArguments.Count -eq 0) {
    throw 'A SharpProof loop command is required.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gitPath = (Get-Command git -CommandType Application -ErrorAction Stop |
        Select-Object -First 1).Source
Get-Command docker -CommandType Application -ErrorAction Stop | Out-Null

function Get-GitUntrackedPaths {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $gitPath
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-C',
            $repositoryRoot,
            'ls-files',
            '-z',
            '--others',
            '--exclude-standard',
            '--')) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $output = [IO.MemoryStream]::new()
    try {
        if (-not $process.Start()) {
            throw 'Could not start Git untracked-file discovery.'
        }
        $copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
        $errorOutput = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $null = $copy.GetAwaiter().GetResult()
        $stderr = $errorOutput.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw (
                'Git untracked-file discovery failed with exit code ' +
                "$($process.ExitCode): $stderr")
        }
    }
    finally {
        $process.Dispose()
    }

    $bytes = $output.ToArray()
    $output.Dispose()
    if ($bytes.Length -eq 0) {
        return @()
    }
    if ($bytes[$bytes.Length - 1] -ne 0) {
        throw 'Git returned a non-terminated untracked-file inventory.'
    }

    $paths = [Collections.Generic.List[string]]::new()
    $start = 0
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -ne 0) {
            continue
        }
        $length = $index - $start
        if ($length -gt 0) {
            $paths.Add([Text.Encoding]::UTF8.GetString(
                $bytes,
                $start,
                $length))
        }
        $start = $index + 1
    }
    return $paths.ToArray()
}

function Get-RequiredHead {
    $head = @(& $gitPath -C $repositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0 -or $head.Count -ne 1) {
        throw 'Could not resolve the host checkout HEAD.'
    }
    $value = ([string]$head[0]).Trim()
    if ($value -notmatch '^[0-9a-f]{40,64}$') {
        throw "The host checkout returned an invalid HEAD: '$value'."
    }
    return $value
}

function Test-ExactFileBytes {
    param(
        [Parameter(Mandatory = $true)][string]$LeftPath,
        [Parameter(Mandatory = $true)][string]$RightPath
    )

    if (-not [IO.File]::Exists($LeftPath) -or
        -not [IO.File]::Exists($RightPath)) {
        return $false
    }
    $left = [IO.File]::ReadAllBytes($LeftPath)
    $right = [IO.File]::ReadAllBytes($RightPath)
    if ($left.Length -ne $right.Length) {
        return $false
    }
    for ($index = 0; $index -lt $left.Length; $index++) {
        if ($left[$index] -ne $right[$index]) {
            return $false
        }
    }
    return $true
}

$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
[IO.Directory]::CreateDirectory($artifactsRoot) | Out-Null
$snapshotName = '.sharpproof-loop-input-' + [Guid]::NewGuid().ToString('N')
$snapshotRoot = Join-Path $artifactsRoot $snapshotName
$snapshotFiles = Join-Path $snapshotRoot 'files'
$sourcePatch = Join-Path $snapshotRoot 'source.patch'
$verificationPatch = Join-Path $snapshotRoot 'source.verify.patch'
$sourceManifest = Join-Path $snapshotRoot 'source-files'
$pathComparison = if ($IsWindows) {
    [StringComparison]::OrdinalIgnoreCase
}
else {
    [StringComparison]::Ordinal
}
$repositoryPrefix =
    $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$snapshotPrefix =
    ([IO.Path]::GetFullPath($artifactsRoot)).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$exitCode = 1

try {
    [IO.Directory]::CreateDirectory($snapshotFiles) | Out-Null
    $head = Get-RequiredHead
    [IO.File]::WriteAllText(
        (Join-Path $snapshotRoot 'head'),
        $head + "`n",
        [Text.UTF8Encoding]::new($false))

    & $gitPath -C $repositoryRoot diff `
        --binary `
        --full-index `
        --no-ext-diff `
        "--output=$sourcePatch" `
        HEAD `
        -- `
        .
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not capture the host tracked-source patch.'
    }

    $untrackedPaths = @(Get-GitUntrackedPaths)
    $manifest = [IO.File]::Open(
        $sourceManifest,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    try {
        foreach ($relativePath in $untrackedPaths) {
            $normalized = $relativePath.Replace('\', '/')
            if ([IO.Path]::IsPathRooted($normalized) -or
                $normalized.Split('/') -contains '..') {
                throw "Git returned an unsafe untracked path: '$relativePath'."
            }
            $sourcePath = [IO.Path]::GetFullPath(
                (Join-Path $repositoryRoot $relativePath))
            if (-not $sourcePath.StartsWith(
                    $repositoryPrefix,
                    $pathComparison)) {
                throw "Git returned an escaped untracked path: '$relativePath'."
            }
            $sourceItem = Get-Item -LiteralPath $sourcePath -Force
            if ($sourceItem.PSIsContainer -or $null -ne $sourceItem.LinkType) {
                throw (
                    'The fast loop snapshot accepts regular untracked files ' +
                    "only: '$relativePath'.")
            }
            $destination = Join-Path $snapshotFiles $relativePath
            [IO.Directory]::CreateDirectory(
                [IO.Path]::GetDirectoryName($destination)) | Out-Null
            [IO.File]::Copy($sourcePath, $destination, $false)

            $encodedPath = [Text.Encoding]::UTF8.GetBytes($normalized)
            $manifest.Write($encodedPath, 0, $encodedPath.Length)
            $manifest.WriteByte(0)
        }
    }
    finally {
        $manifest.Dispose()
    }

    $verificationHead = Get-RequiredHead
    & $gitPath -C $repositoryRoot diff `
        --binary `
        --full-index `
        --no-ext-diff `
        "--output=$verificationPatch" `
        HEAD `
        -- `
        .
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not verify the host tracked-source patch.'
    }
    $verificationUntracked = @(Get-GitUntrackedPaths)
    if ($verificationHead -cne $head -or
        @(Compare-Object `
            -ReferenceObject $untrackedPaths `
            -DifferenceObject $verificationUntracked `
            -SyncWindow 0).Count -ne 0 -or
        -not (Test-ExactFileBytes `
            -LeftPath $sourcePatch `
            -RightPath $verificationPatch)) {
        throw 'The host source changed while its loop snapshot was captured.'
    }
    foreach ($relativePath in $untrackedPaths) {
        if (-not (Test-ExactFileBytes `
                -LeftPath (Join-Path $repositoryRoot $relativePath) `
                -RightPath (Join-Path $snapshotFiles $relativePath))) {
            throw (
                'An untracked source file changed while its loop snapshot ' +
                "was captured: '$relativePath'.")
        }
    }

    $containerSnapshot =
        '/workspace/LoopArtifacts/' + $snapshotName
    & docker compose exec -T `
        -e "SHARPPROOF_LOOP_SNAPSHOT_ROOT=$containerSnapshot" `
        loop `
        sharpproof-loop `
        @LoopArguments
    $exitCode = $LASTEXITCODE
}
finally {
    $resolvedSnapshot = [IO.Path]::GetFullPath($snapshotRoot)
    if ($resolvedSnapshot.StartsWith($snapshotPrefix, $pathComparison) -and
        [IO.Path]::GetFileName($resolvedSnapshot).StartsWith(
            '.sharpproof-loop-input-',
            [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedSnapshot -Recurse -Force `
            -ErrorAction SilentlyContinue
    }
}

exit $exitCode
