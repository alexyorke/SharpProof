[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.File]::ReadAllText(
    (Join-Path $PSScriptRoot 'Test-SharpProofPackageConsumers.ps1'))
$start = $source.IndexOf(
    'function Invoke-ConsumerDotNet',
    [StringComparison]::Ordinal)
$end = $source.IndexOf(
    'function Assert-SharpProofAnalyzerItems',
    $start,
    [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) {
    throw 'The package-consumer helper could not be isolated.'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof.PackageConsumerTimeout.' + [Guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $fixtureRoot 'bin'
$runnerPath = Join-Path $fixtureRoot 'runner.ps1'
New-Item -ItemType Directory -Path $fakeBin -Force | Out-Null

try {
    $fakeDotnet = @'
#!/bin/sh
sleep 5
exit 0
'@
    [IO.File]::WriteAllText(
        (Join-Path $fakeBin 'dotnet'),
        $fakeDotnet.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
    & /bin/chmod '+x' -- (Join-Path $fakeBin 'dotnet')
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not make the fake dotnet executable.'
    }
    Copy-Item -LiteralPath (
        Join-Path $PSScriptRoot 'Invoke-SharpProofDotnet.ps1') `
        -Destination (Join-Path $fixtureRoot 'Invoke-SharpProofDotnet.ps1')

    $functionText = $source.Substring($start, $end - $start)
    $runner = @"
Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'
$functionText
`$env:SHARPPROOF_CONTAINER = '1'
`$env:PATH = '$($fakeBin.Replace("'", "''"))' + ':' + `$env:PATH
`$stopwatch = [Diagnostics.Stopwatch]::StartNew()
try {
    Invoke-ConsumerDotNet -WorkingDirectory '$($fixtureRoot.Replace("'", "''"))' -Arguments @('build', 'hang') -RepositoryRoot '$($fixtureRoot.Replace("'", "''"))' -TimeoutSeconds 1
    throw 'The package-consumer helper unexpectedly completed a hanging command.'
}
catch {
    `$stopwatch.Stop()
    if (`$_.Exception.Message -notmatch 'timed out after 1 seconds') {
        throw
    }
    if (`$stopwatch.Elapsed.TotalSeconds -ge 4) {
        throw "The package-consumer helper exceeded its timeout: `$(`$stopwatch.Elapsed.TotalSeconds) seconds."
    }
}
"@
    [IO.File]::WriteAllText(
        $runnerPath,
        $runner.Replace("`r`n", "`n"),
        [Text.UTF8Encoding]::new($false))
    & pwsh -NoLogo -NoProfile -File $runnerPath
    if ($LASTEXITCODE -ne 0) {
        throw "Package-consumer timeout fixture failed with exit code $LASTEXITCODE."
    }
    Write-Host 'Package-consumer timeout fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
