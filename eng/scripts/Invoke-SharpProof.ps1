[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$ToolingArguments = @('doctor')
)

# Host orchestration only: all repository tasks still execute in Linux amd64.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Get-Command git, docker -CommandType Application -ErrorAction Stop | Out-Null
$commonDirectory = & git -C $repositoryRoot rev-parse --path-format=absolute --git-common-dir
if ($LASTEXITCODE -ne 0) { throw 'Run this wrapper from a Git checkout.' }
$gitDirectory = & git -C $repositoryRoot rev-parse --absolute-git-dir
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve Git checkout metadata.' }
$commonDirectory = (Resolve-Path -LiteralPath $commonDirectory).Path
$gitDirectory = (Resolve-Path -LiteralPath $gitDirectory).Path
$arguments = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
$arguments.AddRange([string[]]@('compose', '--project-directory', $repositoryRoot, 'run', '--rm'))
$pointerPath = $null
try {
    if ($gitDirectory -ne $commonDirectory) {
        $commonPrefix = $commonDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $comparison = if ($env:OS -eq 'Windows_NT') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
        if (-not $gitDirectory.StartsWith($commonPrefix, $comparison)) {
            throw 'Worktree metadata must be contained in the resolved Git common directory.'
        }
        $relative = $gitDirectory.Substring($commonPrefix.Length).Replace('\', '/')
        $pointerPath = [IO.Path]::GetTempFileName()
        [IO.File]::WriteAllText($pointerPath, "gitdir: /git-common/$relative`n", [Text.UTF8Encoding]::new($false))
        $arguments.AddRange([string[]]@('--volume', "${commonDirectory}:/git-common:ro",
                '--volume', "${pointerPath}:/workspace/SharpProof/.git:ro"))
    }
    $service = if ($ToolingArguments[0] -eq 'lint') { 'lint' } else { 'tooling' }
    if ($service -eq 'lint') {
        if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
            $arguments.AddRange([string[]]@('--env', 'GITHUB_TOKEN'))
        }
        # Older Compose releases lose additional service contexts in run --build.
        & docker compose --project-directory $repositoryRoot build lint
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $arguments.Add($service)
    $arguments.AddRange($ToolingArguments)
    [string[]]$nativeArguments = @($arguments.ToImmutable())
    & docker @nativeArguments
    $toolingExitCode = $LASTEXITCODE
}
finally {
    if ($null -ne $pointerPath) { Remove-Item -LiteralPath $pointerPath -Force }
}
exit $toolingExitCode
