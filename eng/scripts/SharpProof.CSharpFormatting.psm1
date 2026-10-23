Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FormattingDigest([string[]]$Values) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Values -join "`n") + "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-FormattingIdentity([string]$RepositoryRoot) {
    $commit = [string](& git -C $RepositoryRoot rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Formatting source commit could not be read.' }
    $tree = [string](& git -C $RepositoryRoot rev-parse 'HEAD^{tree}')
    if ($LASTEXITCODE -ne 0) { throw 'Formatting source tree could not be read.' }
    & git -C $RepositoryRoot diff --quiet --ignore-submodules=none --
    if ($LASTEXITCODE -ne 0) { throw 'Formatting shards require clean tracked source.' }
    & git -C $RepositoryRoot diff --cached --quiet --ignore-submodules=none HEAD --
    if ($LASTEXITCODE -ne 0) { throw 'Formatting shards require a clean source index.' }
    if ($commit -cne $env:GITHUB_SHA -or $env:GITHUB_RUN_ID -notmatch '^[1-9]\d*$' -or
        $env:GITHUB_RUN_ATTEMPT -notmatch '^[1-9]\d*$') {
        throw 'Formatting shards require their exact source and workflow identity.'
    }
    return ([ordered]@{ commit = $commit; tree = $tree; runId = $env:GITHUB_RUN_ID;
            runAttempt = $env:GITHUB_RUN_ATTEMPT
        }).AsReadOnly()
}

function Get-FormattingRelativePath([string]$RepositoryRoot, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path) -or
        $Path.Replace('\', '/').Split('/') -contains '..') {
        throw 'Formatting inventory contains an unsafe source path.'
    }
    $relative = $Path.Replace('\', '/')
    $full = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relative))
    $prefix = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::Ordinal) -or -not (Test-Path -LiteralPath $full -PathType Leaf)) {
        throw 'Formatting inventory contains an absent or external source path.'
    }
    return $relative
}

function Get-SharpProofCSharpFormattingPlan {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [ValidateSet(1, 2)][int]$ShardCount = 1,
        [ValidateRange(0, 1)][int]$ShardIndex = 0
    )
    if ($ShardIndex -ge $ShardCount) { throw 'Formatting shard index is outside its count.' }
    $identity = Get-FormattingIdentity $RepositoryRoot
    $solutionPath = Join-Path $RepositoryRoot 'SharpProof.slnx'
    $solution = [xml](Get-Content -LiteralPath $solutionPath -Raw)
    $projectSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($project in $solution.SelectNodes('//Project')) {
        $path = Get-FormattingRelativePath $RepositoryRoot ([string]$project.Path)
        if (-not $path.EndsWith('.csproj', [StringComparison]::Ordinal) -or -not $projectSet.Add($path)) {
            throw 'Formatting solution roots are duplicated or unsupported.'
        }
    }
    $projects = [string[]]@($projectSet)
    [Array]::Sort($projects, [StringComparer]::Ordinal)
    $worker = 'src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj'
    if ($projects.Length -lt 2 -or -not $projectSet.Contains($worker)) {
        throw 'Formatting solution does not contain its complete worker/rest partition.'
    }
    $selected = [string[]]@($projects | Where-Object {
            $ShardCount -eq 1 -or (($ShardIndex -eq 0) -eq ($_ -ceq $worker))
        })
    $paths = [string[]]@(& git -C $RepositoryRoot -c core.quotepath=false ls-files -- '*.cs')
    if ($LASTEXITCODE -ne 0 -or $paths.Length -eq 0) { throw 'Formatting requires tracked C# source.' }
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $sources = [Collections.Generic.List[object]]::new()
    foreach ($path in $paths) {
        $relative = Get-FormattingRelativePath $RepositoryRoot $path
        $hash = (Get-FileHash -LiteralPath (Join-Path $RepositoryRoot $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
        $sources.Add(([ordered]@{ path = $relative; sha256 = $hash }).AsReadOnly())
    }
    return ([ordered]@{
            schemaVersion = 1; planVersion = 'worker-rest-v1'; formatFlagsVersion = 'all-tracked-warn-generated-v1'
            identity = $identity; shardCount = $ShardCount; shardIndex = $ShardIndex
            solutionSha256     = (Get-FileHash -LiteralPath $solutionPath -Algorithm SHA256).Hash.ToLowerInvariant()
            rootProjects = $projects; selectedProjects = $selected
            rootProjectsSha256 = Get-FormattingDigest $projects
            sourceFiles        = $sources.ToArray()
            sourceSha256       = Get-FormattingDigest @($sources | ForEach-Object { $_.path + "`0" + $_.sha256 })
        }).AsReadOnly()
}

function Write-SharpProofCSharpFormattingReceipt {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][Collections.IDictionary]$Plan
    )
    $current = Get-SharpProofCSharpFormattingPlan -RepositoryRoot $RepositoryRoot `
        -ShardCount ([int]$Plan.shardCount) -ShardIndex ([int]$Plan.shardIndex)
    if (($Plan | ConvertTo-Json -Depth 8 -Compress) -cne ($current | ConvertTo-Json -Depth 8 -Compress)) {
        throw 'Formatting source or selection changed during verification.'
    }
    $receiptRoot = Join-Path $RepositoryRoot 'artifacts/formatting'
    [void][IO.Directory]::CreateDirectory($receiptRoot)
    $current | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath (Join-Path $receiptRoot "shard-$($Plan.shardIndex).json") -Encoding utf8NoBOM
}

function Assert-SharpProofCSharpFormattingShards {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Collections.IDictionary]$ArtifactAttempts
    )
    if ($null -eq $ArtifactAttempts) {
        if ([string]::IsNullOrWhiteSpace($env:SHARPPROOF_FORMAT_ARTIFACT_ATTEMPTS)) {
            throw 'Formatting aggregate requires every successful producer attempt.'
        }
        $ArtifactAttempts = ConvertFrom-Json -InputObject $env:SHARPPROOF_FORMAT_ARTIFACT_ATTEMPTS -AsHashtable
    }
    if ($ArtifactAttempts.Count -ne 2 -or -not $ArtifactAttempts.Contains('0') -or -not $ArtifactAttempts.Contains('1')) {
        throw 'Formatting aggregate requires exactly two producer attempts.'
    }
    $receiptRoot = Join-Path $RepositoryRoot 'artifacts/formatting'
    $files = @(Get-ChildItem -LiteralPath $receiptRoot -File -Filter 'shard-*.json')
    if ($files.Count -ne 2) { throw 'Formatting aggregate requires exactly two receipts.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sourceCount = 0
    foreach ($index in 0..1) {
        $expected = Get-SharpProofCSharpFormattingPlan -RepositoryRoot $RepositoryRoot -ShardCount 2 -ShardIndex $index
        $attempt = [string]$ArtifactAttempts[[string]$index]
        if ($attempt -notmatch '^[1-9]\d*$' -or [long]$attempt -gt [long]$env:GITHUB_RUN_ATTEMPT) {
            throw 'Formatting producer attempt is invalid or from the future.'
        }
        $receipt = Get-Content -LiteralPath (Join-Path $receiptRoot "shard-$index.json") -Raw | ConvertFrom-Json -AsHashtable
        $expectedValue = [ordered]@{}
        foreach ($key in $expected.Keys) { $expectedValue[$key] = $expected[$key] }
        $expectedIdentity = [ordered]@{}
        foreach ($key in $expected.identity.Keys) { $expectedIdentity[$key] = $expected.identity[$key] }
        $expectedIdentity.runAttempt = $attempt
        $expectedValue.identity = $expectedIdentity
        if (($receipt | ConvertTo-Json -Depth 8 -Compress) -cne ($expectedValue | ConvertTo-Json -Depth 8 -Compress)) {
            throw 'Formatting receipt differs from the exact source, files, roots, flags, or producer attempt.'
        }
        foreach ($project in $receipt.selectedProjects) {
            if (-not $seen.Add([string]$project)) { throw 'Formatting roots are owned by multiple shards.' }
        }
        $sourceCount = @($receipt.sourceFiles).Count
    }
    if ($seen.Count -ne $expected.rootProjects.Length -or @($expected.rootProjects | Where-Object { -not $seen.Contains($_) }).Count -ne 0) {
        throw 'Formatting shards do not cover the exact original solution roots.'
    }
    ([ordered]@{ schemaVersion = 1; planVersion = $expected.planVersion; identity = $expected.identity
        artifactAttempts = $ArtifactAttempts; roots = $seen.Count; sourceFiles = $sourceCount
        sourceSha256 = $expected.sourceSha256; rootProjectsSha256 = $expected.rootProjectsSha256
    }).AsReadOnly() | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $receiptRoot 'formatting-validation.json') -Encoding utf8NoBOM
}

Export-ModuleMember -Function Get-SharpProofCSharpFormattingPlan, Write-SharpProofCSharpFormattingReceipt, Assert-SharpProofCSharpFormattingShards
