[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Test-SharpProofSamples.ps1'))
$start = $source.IndexOf(
    'function Invoke-CapturedDotNet',
    [StringComparison]::Ordinal)
$end = $source.IndexOf(
    'function Assert-ExitCode',
    $start,
    [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) {
    throw 'The captured dotnet helper could not be isolated.'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof.SamplesFixture.' + [Guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $fixtureRoot 'bin'
$childPidPath = Join-Path $fixtureRoot 'child.pid'
$runnerPath = Join-Path $fixtureRoot 'runner.ps1'
New-Item -ItemType Directory -Path $fakeBin -Force | Out-Null

try {
    $fakeDotnet = @"
#!/bin/sh
case "`$*" in
  *fast*)
    echo fast-output
    exit 0
    ;;
  *failure*)
    echo failure-output >&2
    exit 7
    ;;
  *hang*)
    sleep 5 &
    child=`$!
    printf '%s' "`$child" > '$($childPidPath.Replace("'", "'\\''"))'
    sleep 5
    exit 0
    ;;
  *)
    exit 2
    ;;
esac
"@
    [IO.File]::WriteAllText(
        (Join-Path $fakeBin 'dotnet'),
        $fakeDotnet.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
    & /bin/chmod '+x' -- (Join-Path $fakeBin 'dotnet')
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not make the fake dotnet executable.'
    }

    $functionText = $source.Substring($start, $end - $start)
    $runner = @"
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
`$temporaryRoot = '$($fixtureRoot.Replace("'", "''"))'
`$script:dotnetInvocationOrdinal = 0
$functionText
`$env:PATH = '$($fakeBin.Replace("'", "''"))' + ':' + `$env:PATH
`$fast = Invoke-CapturedDotNet -Arguments @('run', 'fast') -TimeoutSeconds 5
if (`$fast.ExitCode -ne 0 -or -not `$fast.Output.Contains('fast-output', [StringComparison]::Ordinal)) {
    throw 'Fast captured dotnet execution did not preserve success and output.'
}
`$failure = Invoke-CapturedDotNet -Arguments @('run', 'failure') -TimeoutSeconds 5
if (`$failure.ExitCode -ne 7 -or -not `$failure.Output.Contains('failure-output', [StringComparison]::Ordinal)) {
    throw 'Nonzero captured dotnet execution did not preserve failure and output.'
}
`$stopwatch = [Diagnostics.Stopwatch]::StartNew()
`$timeout = Invoke-CapturedDotNet -Arguments @('build', 'hang') -TimeoutSeconds 1
`$stopwatch.Stop()
if (`$timeout.ExitCode -ne 124 -or `$stopwatch.Elapsed.TotalSeconds -ge 4) {
    throw "Captured dotnet timeout was not bounded: exit=`$(`$timeout.ExitCode), elapsed=`$(`$stopwatch.Elapsed.TotalSeconds)."
}
`$childPid = [int]([IO.File]::ReadAllText('$($childPidPath.Replace("'", "''"))'))
for (`$attempt = 0; `$attempt -lt 30; `$attempt++) {
    try {
        `$child = Get-Process -Id `$childPid -ErrorAction Stop
        `$alive = -not `$child.HasExited
        `$child.Dispose()
    }
    catch {
        `$alive = `$false
    }
    if (-not `$alive) {
        break
    }
    Start-Sleep -Milliseconds 100
}
try {
    `$child = Get-Process -Id `$childPid -ErrorAction Stop
    `$alive = -not `$child.HasExited
    `$child.Dispose()
}
catch {
    `$alive = `$false
}
if (`$alive) {
    throw "Timed-out dotnet child `$childPid survived process-tree termination."
}
"@
    [IO.File]::WriteAllText(
        $runnerPath,
        $runner.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
    & pwsh -NoLogo -NoProfile -File $runnerPath
    if ($LASTEXITCODE -ne 0) {
        throw "Sample captured-dotnet fixture failed with exit code $LASTEXITCODE."
    }
    Write-Host 'Sample captured-dotnet fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
