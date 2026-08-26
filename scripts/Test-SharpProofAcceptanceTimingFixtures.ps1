[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'canonical','zero-restore','nonzero-restore','boundary-equality',
        'restore-failure','skipped-phase','too-many-partial','phase-order',
        'phase-overlap','before-start','after-completion','wrong-total')]
    [string]$Mutation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$verify = [IO.File]::ReadAllText(
    (Join-Path $repositoryRoot 'eng/acceptance/Verify.ps1'))
$begin = '# BEGIN ACCEPTANCE TIMELINE AUTHORITY'
$end = '# END ACCEPTANCE TIMELINE AUTHORITY'
$start = $verify.IndexOf($begin, [StringComparison]::Ordinal)
$finish = $verify.IndexOf($end, [StringComparison]::Ordinal)
if ($start -lt 0 -or $finish -le $start) {
    throw 'Acceptance timing authority region is unavailable.'
}
Invoke-Expression $verify.Substring(
    $start + $begin.Length,
    $finish - ($start + $begin.Length))

$names = @(
    'restore','static-validation','build','semantic-tests',
    'package-tests','fuzz','corpus-and-performance')
$outerStart = [DateTime]::Parse(
    '2026-01-01T00:00:00.0000000Z',
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind)
$cursor = 0L
$phases = [Collections.Generic.List[object]]::new()
foreach ($name in $names) {
    $duration = if ($name -ceq 'restore' -and
        $Mutation -eq 'nonzero-restore') { 25L } else { 10L }
    if ($Mutation -in @('zero-restore','boundary-equality') -and
        $name -ceq 'restore') { $duration = 0L }
    $phaseStart = $outerStart.AddMilliseconds($cursor)
    $cursor += $duration
    $phases.Add([pscustomobject]@{
        name = $name
        startedUtc = $phaseStart.ToString('o')
        completedUtc = $outerStart.AddMilliseconds($cursor).ToString('o')
        elapsedMilliseconds = $duration
        status = if ($Mutation -eq 'restore-failure' -and
            $name -ceq 'restore') { 'failed' } else { 'passed' }
    })
    if ($Mutation -eq 'restore-failure') { break }
}
$outerCompleted = $outerStart.AddMilliseconds($cursor)
if ($Mutation -eq 'phase-order') {
    $phases[1].name = 'build'
}
elseif ($Mutation -eq 'boundary-equality') {
    $phases[1].completedUtc = $phases[1].startedUtc
    $phases[1].elapsedMilliseconds = 0
}
elseif ($Mutation -eq 'skipped-phase') {
    $phases[1].status = 'skipped'
}
elseif ($Mutation -eq 'too-many-partial') {
    $phases.Add($phases[-1])
}
elseif ($Mutation -eq 'phase-overlap') {
    $phases[1].startedUtc = $outerStart.AddMilliseconds(5).ToString('o')
    $phases[1].elapsedMilliseconds = 15
}
elseif ($Mutation -eq 'before-start') {
    $phases[0].startedUtc = $outerStart.AddMilliseconds(-1).ToString('o')
    $phases[0].elapsedMilliseconds++
}
elseif ($Mutation -eq 'after-completion') {
    $phases[-1].completedUtc = $outerCompleted.AddMilliseconds(1).ToString('o')
    $phases[-1].elapsedMilliseconds++
}
$total = [long]($outerCompleted - $outerStart).TotalMilliseconds
if ($Mutation -eq 'wrong-total') { $total++ }
Test-AcceptanceTimingTimeline `
    -StartedUtc $outerStart `
    -CompletedUtc $outerCompleted `
    -TotalElapsedMilliseconds $total `
    -Phases @($phases) `
    -ExpectedPhaseNames $names `
    -RequireComplete ($Mutation -notin @('restore-failure','too-many-partial'))
Write-Host "Acceptance timing fixture passed: $Mutation"
