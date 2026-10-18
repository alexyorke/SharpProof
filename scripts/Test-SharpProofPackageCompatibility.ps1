[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $IsLinux -or $env:SHARPPROOF_CONTAINER -cne '1') {
    throw 'Package compatibility requires the canonical Linux amd64 lint container.'
}
$baselineRevision = (Get-Content (Join-Path $repositoryRoot 'eng/lint/api-baseline.txt') -Raw).Trim()
if ($baselineRevision -notmatch '^[a-f0-9]{40}$') { throw 'API baseline must be an immutable Git commit.' }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$scratchRoot = Join-Path $temporaryRoot ('sharpproof-api-' + [Guid]::NewGuid().ToString('N'))
$baselineRoot = Join-Path $scratchRoot 'baseline'
$packageRoot = Join-Path $scratchRoot 'packages'
[IO.Directory]::CreateDirectory($packageRoot) | Out-Null
$previousDotnetHome = $env:DOTNET_CLI_HOME
# Keep first-use configuration private and writable even with existing cache volumes.
$env:DOTNET_CLI_HOME = Join-Path $scratchRoot 'dotnet-home'

function Invoke-CompatibilityNative([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE." }
}

# Attributes is the packaged public lib/ API. Analyzer/tool payloads have custom
# layouts and are covered by the existing package consumer and layout gates.
try {
    $currentRevision = (& git -C $repositoryRoot rev-parse --verify 'HEAD^{commit}').Trim()
    if ($LASTEXITCODE -ne 0 -or $currentRevision -cnotmatch '\A[a-f0-9]{40}\z') {
        throw 'Cannot identify the immutable current source commit.'
    }
    $project = 'SharpProof.Attributes/SharpProof.Attributes.csproj'
    Invoke-CompatibilityNative 'git' @('clone', '--quiet', '--shared', '--no-checkout', $repositoryRoot, $baselineRoot)
    Invoke-CompatibilityNative 'git' @('-C', $baselineRoot, 'checkout', '--quiet', '--detach', $baselineRevision)
    $origin = & git -C $repositoryRoot remote get-url origin
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the baseline source repository.' }
    Invoke-CompatibilityNative 'git' @('-C', $baselineRoot, 'remote', 'set-url', 'origin', $origin)
    foreach ($entry in @(
            @{ root = $baselineRoot; output = (Join-Path $packageRoot 'baseline'); validation = 'false'; revision = $baselineRevision },
            @{ root = $repositoryRoot; output = (Join-Path $packageRoot 'current'); validation = 'true'; revision = $currentRevision }
        )) {
        $entryRevision = (& git -C $entry.root rev-parse --verify 'HEAD^{commit}').Trim()
        if ($LASTEXITCODE -ne 0 -or $entryRevision -cnotmatch '\A[a-f0-9]{40}\z' -or $entryRevision -cne $entry.revision) {
            throw 'Compatibility source checkout differs from its immutable expected commit.'
        }
        Invoke-CompatibilityNative 'dotnet' @(
            'restore', (Join-Path $entry.root $project), '--locked-mode',
            '--configfile', (Join-Path $repositoryRoot 'NuGet.Config'))
        Invoke-CompatibilityNative 'dotnet' @(
            'pack', (Join-Path $entry.root $project), '--configuration', 'Release', '--no-restore',
            '--output', $entry.output, '-p:RunAnalyzersDuringBuild=false',
            "-p:EnablePackageValidation=$($entry.validation)",
            "-p:RepositoryCommit=$entryRevision", "-p:SourceRevisionId=$entryRevision")
        $packedRevision = (& git -C $entry.root rev-parse --verify 'HEAD^{commit}').Trim()
        if ($LASTEXITCODE -ne 0 -or $packedRevision -cne $entryRevision) {
            throw 'Compatibility source commit changed while packing.'
        }
    }
    $baselinePackages = @(Get-ChildItem (Join-Path $packageRoot 'baseline') -Filter '*.nupkg')
    $currentPackages = @(Get-ChildItem (Join-Path $packageRoot 'current') -Filter '*.nupkg')
    if ($baselinePackages.Count -ne 1 -or $currentPackages.Count -ne 1) {
        throw 'Expected exactly one baseline and current public API package.'
    }
    Invoke-CompatibilityNative 'apicompat' @(
        'package', $currentPackages[0].FullName, '--baseline-package', $baselinePackages[0].FullName,
        '--enable-rule-cannot-change-parameter-name')
    Write-Host "Public package compatibility passed against $baselineRevision."
}
finally {
    $env:DOTNET_CLI_HOME = $previousDotnetHome
    $resolvedScratch = [IO.Path]::GetFullPath($scratchRoot)
    if ([IO.Path]::GetDirectoryName($resolvedScratch) -cne $temporaryRoot -or
        [IO.Path]::GetFileName($resolvedScratch) -cnotmatch '\Asharpproof-api-[a-f0-9]{32}\z') {
        throw 'Refusing to remove an unexpected compatibility scratch directory.'
    }
    if ([IO.Directory]::Exists($resolvedScratch)) {
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}
