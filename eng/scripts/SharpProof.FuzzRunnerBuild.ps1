Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'SharpProof.FuzzEvidenceLifecycle.ps1')
. (Join-Path $PSScriptRoot 'Resolve-SharpProofContainedPath.ps1')

function Get-SharpProofFreshFuzzRunnerFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][DateTime]$BuildStartedUtc
    )

    if ($BuildStartedUtc.Kind -ne [DateTimeKind]::Utc -or
        $BuildStartedUtc -gt [DateTime]::UtcNow) {
        throw 'The prepared fuzz runner requires a past UTC build start.'
    }
    $outputRoot = Resolve-SharpProofContainedPath -Root $RepositoryRoot `
        -Path 'artifacts/bin/SharpProof.Fuzz/Release/net10.0' `
        -ParameterName 'Prepared fuzz runner output'
    foreach ($name in @('SharpProof.Fuzz.dll', 'SharpProof.Fuzz.deps.json',
            'SharpProof.Fuzz.runtimeconfig.json')) {
        $required = [IO.FileInfo]::new((Join-Path $outputRoot $name))
        if (-not $required.Exists -or $required.Length -eq 0 -or
            $required.LastWriteTimeUtc -lt $BuildStartedUtc) {
            throw "The prepared fuzz runner is missing, empty, or stale: $name"
        }
    }

    $files = [Collections.Immutable.ImmutableArray]::CreateBuilder[object]()
    foreach ($item in @(Get-ChildItem -LiteralPath $outputRoot -Recurse -Force |
                Sort-Object -Property FullName -CaseSensitive)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'The prepared fuzz runner output must not contain links.'
        }
        if ($item.PSIsContainer) { continue }
        $length = $item.Length
        $written = $item.LastWriteTimeUtc
        $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $item.Refresh()
        if ($item.Length -ne $length -or $item.LastWriteTimeUtc -ne $written) {
            throw 'The prepared fuzz runner output changed while hashing.'
        }
        $files.Add(([ordered]@{
                    path   = [IO.Path]::GetRelativePath($outputRoot, $item.FullName).Replace('\', '/')
                    length = $length
                    sha256 = $hash
                }).AsReadOnly())
    }
    return , $files.ToImmutable()
}

function Assert-SharpProofFuzzRunnerBuildSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$SourceCommit
    )

    if ($SourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
        (Get-SharpProofCleanFuzzSourceCommit -RepositoryRoot $RepositoryRoot) -cne $SourceCommit) {
        throw 'The prepared fuzz runner source commit does not match the clean workspace.'
    }
}

# This receipt stays in memory between the successful normal solution build
# and its campaign in the same private task workspace. It is not an artifact
# import protocol or permission to reuse output from another checkout.
function New-SharpProofFuzzRunnerBuildReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][DateTime]$BuildStartedUtc
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-SharpProofFuzzRunnerBuildSource -RepositoryRoot $root -SourceCommit $SourceCommit
    $files = Get-SharpProofFreshFuzzRunnerFiles -RepositoryRoot $root -BuildStartedUtc $BuildStartedUtc
    Assert-SharpProofFuzzRunnerBuildSource -RepositoryRoot $root -SourceCommit $SourceCommit
    return ([ordered]@{
            repositoryRoot  = $root
            sourceCommit    = $SourceCommit
            buildStartedUtc = $BuildStartedUtc
            files           = $files
        }).AsReadOnly()
}

function Assert-SharpProofFuzzRunnerBuildReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][Collections.IDictionary]$Receipt
    )

    Assert-SharpProofExactJsonProperties -Actual @($Receipt.Keys) `
        -Expected @('repositoryRoot', 'sourceCommit', 'buildStartedUtc', 'files') `
        -Description 'Prepared fuzz runner receipt'
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    if ($Receipt.repositoryRoot -cne $root -or $Receipt.sourceCommit -cne $SourceCommit -or
        $Receipt.buildStartedUtc -isnot [DateTime]) {
        throw 'The prepared fuzz runner receipt belongs to a different source workspace or build.'
    }
    Assert-SharpProofFuzzRunnerBuildSource -RepositoryRoot $root -SourceCommit $SourceCommit
    $actual = Get-SharpProofFreshFuzzRunnerFiles -RepositoryRoot $root -BuildStartedUtc $Receipt.buildStartedUtc
    if ($Receipt.files.Count -ne $actual.Length) {
        throw 'The prepared fuzz runner runtime file inventory changed.'
    }
    for ($index = 0; $index -lt $actual.Length; $index++) {
        $expected = $Receipt.files[$index]
        $observed = $actual[$index]
        if ($expected.path -cne $observed.path -or $expected.length -ne $observed.length -or
            $expected.sha256 -cne $observed.sha256) {
            throw 'The prepared fuzz runner runtime file identity changed.'
        }
    }
    Assert-SharpProofFuzzRunnerBuildSource -RepositoryRoot $root -SourceCommit $SourceCommit
}
