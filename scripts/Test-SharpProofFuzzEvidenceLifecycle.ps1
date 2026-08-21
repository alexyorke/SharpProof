[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.FuzzEvidenceLifecycle.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-fuzz-evidence-' + [Guid]::NewGuid().ToString('N'))
try {
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $campaign = Join-Path $root 'campaign.json'
    $unrelated = Join-Path $root 'notes.txt'
    [IO.File]::WriteAllText($campaign, '{"passed":true}')
    [IO.File]::WriteAllText((Join-Path $root 'rotating-1.stdout.json'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'rotating-1.stderr.txt'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'retained-2.stdout.json'), 'old')
    [IO.File]::WriteAllText((Join-Path $root 'retained-2.stderr.txt'), 'old')
    [IO.File]::WriteAllText($unrelated, 'keep')

    Initialize-SharpProofFuzzEvidence -OutputDirectory $root
    if ([IO.File]::Exists($campaign) -or
        @([IO.Directory]::EnumerateFiles($root) | Where-Object {
                [IO.Path]::GetFileName($_) -cmatch
                    '^(?:rotating|retained)-[0-9]+\.(?:stdout\.json|stderr\.txt)$'
            }).Count -ne 0) {
        throw 'Owned stale fuzz evidence survived initialization.'
    }
    if ([IO.File]::ReadAllText($unrelated) -cne 'keep') {
        throw 'Unrelated fuzz output was changed.'
    }

    # A prerequisite or launcher failure after initialization publishes nothing.
    if ([IO.File]::Exists($campaign)) {
        throw 'A failed run retained stable campaign evidence.'
    }

    $first = "{`"schemaVersion`":3,`"passed`":true}`n"
    Publish-SharpProofFuzzEvidence -OutputDirectory $root -Json $first
    if ([IO.File]::ReadAllText($campaign) -cne $first -or
        [IO.File]::Exists((Join-Path $root '.campaign.json.tmp'))) {
        throw 'Successful campaign evidence was not atomically completed.'
    }

    # Retry invalidates the prior pass and can publish a new complete generation.
    Initialize-SharpProofFuzzEvidence -OutputDirectory $root
    $second = "{`"schemaVersion`":3,`"passed`":true,`"retry`":true}`n"
    Publish-SharpProofFuzzEvidence -OutputDirectory $root -Json $second
    if ([IO.File]::ReadAllText($campaign) -cne $second -or
        [IO.File]::ReadAllText($unrelated) -cne 'keep') {
        throw 'Retry did not replace only the owned stable evidence.'
    }

    Write-Host 'Fuzz evidence lifecycle fixtures: 6'
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
}
