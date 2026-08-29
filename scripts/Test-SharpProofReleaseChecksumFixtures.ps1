[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'canonical','bom','utf16le','utf16be','invalid-utf8','crlf','mixed',
        'cr','missing-terminal','double-terminal','upper-digest','separator',
        'spacing','reordered','extra','missing','duplicate',
        'bundle-canonical','bundle-extra','bundle-nested-extra',
        'bundle-alternate-sbom','bundle-symlink','bundle-empty',
        'bundle-missing-manifest','bundle-missing-checksums',
        'bundle-missing-package','bundle-case-collision',
        'bundle-empty-directory','bundle-hardlink-alias',
        'bundle-fifo',
        'bundle-atomic-replacement','bundle-atomic-failure-cleanup',
        'bundle-atomic-recovery')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseChecksums.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-checksums-' + [Guid]::NewGuid().ToString('N'))
$recoveryBackup = $null
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $artifacts = @(
        0..6 | ForEach-Object {
            [pscustomobject][ordered]@{
                fileName = ('artifact-{0}.nupkg' -f $_)
                kind = if ($_ -lt 3) { 'package' }
                    elseif ($_ -lt 6) { 'symbols' }
                    else { 'sbom' }
                sha256 = ([string]$_) * 64
            }
        }
    )
    if ($Mutation.StartsWith('bundle-', [StringComparison]::Ordinal)) {
        $artifacts[6].fileName = 'SharpProof.spdx.json'
        foreach ($artifact in $artifacts) {
            [IO.File]::WriteAllText(
                (Join-Path $root $artifact.fileName),
                $artifact.fileName)
        }
        [IO.File]::WriteAllText(
            (Join-Path $root 'SharpProof.release.json'), '{}')
        [IO.File]::WriteAllText((Join-Path $root 'SHA256SUMS'), 'sums')
        switch ($Mutation) {
            'bundle-extra' {
                [IO.File]::WriteAllText((Join-Path $root 'foreign.txt'), 'x')
            }
            'bundle-nested-extra' {
                $nested = Join-Path $root 'nested'
                [IO.Directory]::CreateDirectory($nested) | Out-Null
                [IO.File]::WriteAllText((Join-Path $nested 'foreign.txt'), 'x')
            }
            'bundle-alternate-sbom' {
                [IO.File]::WriteAllText((Join-Path $root 'other.spdx.json'), '{}')
            }
            'bundle-symlink' {
                New-Item -ItemType SymbolicLink `
                    -Path (Join-Path $root 'alias') `
                    -Target (Join-Path $root $artifacts[0].fileName) | Out-Null
            }
            'bundle-empty' {
                Get-ChildItem -LiteralPath $root -Force |
                    Remove-Item -Recurse -Force
            }
            'bundle-missing-manifest' {
                Remove-Item -LiteralPath (Join-Path $root 'SharpProof.release.json')
            }
            'bundle-missing-checksums' {
                Remove-Item -LiteralPath (Join-Path $root 'SHA256SUMS')
            }
            'bundle-missing-package' {
                Remove-Item -LiteralPath (Join-Path $root $artifacts[0].fileName)
            }
            'bundle-case-collision' {
                [IO.File]::WriteAllText(
                    (Join-Path $root $artifacts[0].fileName.ToUpperInvariant()),
                    'collision')
            }
            'bundle-empty-directory' {
                [IO.Directory]::CreateDirectory((Join-Path $root 'empty')) | Out-Null
            }
            'bundle-hardlink-alias' {
                & ln -- (Join-Path $root $artifacts[0].fileName) `
                    (Join-Path $root 'hardlink-alias.nupkg')
                if ($LASTEXITCODE -ne 0) { throw 'Could not create hardlink fixture.' }
            }
            'bundle-fifo' {
                Remove-Item -LiteralPath (Join-Path $root $artifacts[0].fileName)
                & mkfifo -- (Join-Path $root $artifacts[0].fileName)
                if ($LASTEXITCODE -ne 0) { throw 'Could not create FIFO fixture.' }
            }
        }
        if ($Mutation -in @(
                'bundle-atomic-replacement',
                'bundle-atomic-failure-cleanup',
                'bundle-atomic-recovery')) {
            $staging = $root + '.staging'
            [IO.Directory]::CreateDirectory($staging) | Out-Null
            Get-ChildItem -LiteralPath $root -File | ForEach-Object {
                [IO.File]::Copy(
                    $_.FullName,
                    (Join-Path $staging $_.Name))
            }
            if ($Mutation -eq 'bundle-atomic-failure-cleanup') {
                [IO.File]::WriteAllText((Join-Path $staging 'foreign'), 'x')
                $failed = $false
                try {
                    Publish-SharpProofReleaseBundleAtomically `
                        -StagingDirectory $staging `
                        -DestinationDirectory $root `
                        -Artifacts $artifacts `
                        -Owner 'Fixture atomic bundle'
                }
                catch { $failed = $true }
                if (-not $failed -or [IO.Directory]::Exists($staging)) {
                    throw 'Invalid staging did not fail and clean up.'
                }
            }
            elseif ($Mutation -eq 'bundle-atomic-recovery') {
                $recoveryBackup = Join-Path ([IO.Path]::GetDirectoryName($root)) (
                    '.' + [IO.Path]::GetFileName($root) + '.recovery.backup')
                [IO.Directory]::Move($root, $recoveryBackup)
                Publish-SharpProofReleaseBundleAtomically `
                    -StagingDirectory $staging `
                    -DestinationDirectory $root `
                    -Artifacts $artifacts `
                    -Owner 'Fixture atomic bundle recovery'
            }
            else {
                Publish-SharpProofReleaseBundleAtomically `
                    -StagingDirectory $staging `
                    -DestinationDirectory $root `
                    -Artifacts $artifacts `
                    -Owner 'Fixture atomic bundle'
            }
        }
        if ($Mutation -eq 'bundle-fifo') {
            try {
                Test-SharpProofReleaseBundleTopology `
                    -Directory $root -Artifacts $artifacts `
                    -Owner 'Fixture release bundle'
                throw 'A FIFO was accepted as a release artifact.'
            }
            catch {
                if ($_.Exception.Message -eq
                        'A FIFO was accepted as a release artifact.') {
                    throw
                }
            }
        }
        else {
            Test-SharpProofReleaseBundleTopology `
                -Directory $root -Artifacts $artifacts `
                -Owner 'Fixture release bundle'
        }
        Write-Host "Release bundle fixture passed: $Mutation"
        return
    }
    $path = Join-Path $root 'SHA256SUMS'
    Write-SharpProofReleaseChecksumFile `
        -Path $path `
        -Artifacts $artifacts
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $text = $strictUtf8.GetString([IO.File]::ReadAllBytes($path))
    $lines = @($text.TrimEnd("`n").Split("`n"))
    $bytes = switch ($Mutation) {
        'canonical' { [IO.File]::ReadAllBytes($path); break }
        'bom' { [byte[]]([Text.UTF8Encoding]::new($true).GetPreamble() + $strictUtf8.GetBytes($text)); break }
        'utf16le' { [byte[]]([Text.Encoding]::Unicode.GetPreamble() + [Text.Encoding]::Unicode.GetBytes($text)); break }
        'utf16be' { [byte[]]([Text.Encoding]::BigEndianUnicode.GetPreamble() + [Text.Encoding]::BigEndianUnicode.GetBytes($text)); break }
        'invalid-utf8' { [byte[]](0xff, 0xfe, 0xfd); break }
        'crlf' { $strictUtf8.GetBytes($text.Replace("`n", "`r`n")); break }
        'mixed' { $strictUtf8.GetBytes($lines[0] + "`r`n" + (($lines | Select-Object -Skip 1) -join "`n") + "`n"); break }
        'cr' { $strictUtf8.GetBytes($text.Replace("`n", "`r")); break }
        'missing-terminal' { $strictUtf8.GetBytes($text.Substring(0, $text.Length - 1)); break }
        'double-terminal' { $strictUtf8.GetBytes($text + "`n"); break }
        'upper-digest' { $strictUtf8.GetBytes($lines[0].ToUpperInvariant() + "`n" + (($lines | Select-Object -Skip 1) -join "`n") + "`n"); break }
        'separator' { $strictUtf8.GetBytes($text.Replace('  ', ' ')); break }
        'spacing' { $strictUtf8.GetBytes($text.Replace('  ', '   ')); break }
        'reordered' { $strictUtf8.GetBytes((@($lines[1], $lines[0]) + @($lines | Select-Object -Skip 2) -join "`n") + "`n"); break }
        'extra' { $strictUtf8.GetBytes($text + (('f' * 64) + '  extra.nupkg' + "`n")); break }
        'missing' { $strictUtf8.GetBytes((@($lines | Select-Object -Skip 1) -join "`n") + "`n"); break }
        'duplicate' { $strictUtf8.GetBytes($text + $lines[0] + "`n"); break }
    }
    [IO.File]::WriteAllBytes($path, $bytes)
    Test-SharpProofReleaseChecksumFile `
        -Path $path `
        -Artifacts $artifacts `
        -Owner 'Fixture SHA256SUMS'
    Write-Host "Release checksum fixture passed: $Mutation"
}
finally {
    if ([IO.Directory]::Exists($root)) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
    if ($null -ne $recoveryBackup -and
            [IO.Directory]::Exists($recoveryBackup)) {
        Remove-Item -LiteralPath $recoveryBackup -Recurse -Force
    }
}
