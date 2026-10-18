[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$ToolingArguments = @('doctor')
)

# Host orchestration only: all repository tasks still execute in Linux amd64.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Get-Command git, docker -CommandType Application -ErrorAction Stop | Out-Null
$commonDirectory = & git -C $repositoryRoot rev-parse --path-format=absolute --git-common-dir
if ($LASTEXITCODE -ne 0) { throw 'Run this wrapper from a Git checkout.' }
$gitDirectory = & git -C $repositoryRoot rev-parse --absolute-git-dir
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve Git checkout metadata.' }
$commonDirectory = (Resolve-Path -LiteralPath $commonDirectory).Path
$gitDirectory = (Resolve-Path -LiteralPath $gitDirectory).Path
$arguments = @('compose', '--project-directory', $repositoryRoot, 'run', '--rm')
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
        $arguments += @('--volume', "${commonDirectory}:/git-common:ro",
            '--volume', "${pointerPath}:/workspace/SharpProof/.git:ro")
    }
    $arguments += @('tooling') + $ToolingArguments
    & docker @arguments
    $toolingExitCode = $LASTEXITCODE
}
finally {
    if ($null -ne $pointerPath) { Remove-Item -LiteralPath $pointerPath -Force }
}
exit $toolingExitCode
