Set-StrictMode -Version Latest

function Initialize-SharpProofFuzzEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory
    )

    [IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    foreach ($file in [IO.Directory]::EnumerateFiles($OutputDirectory)) {
        $name = [IO.Path]::GetFileName($file)
        if ($name -ceq 'campaign.json' -or
            $name -ceq '.campaign.json.tmp' -or
            $name -cmatch '^(?:rotating|retained)-[0-9]+\.(?:stdout\.json|stderr\.txt)$') {
            [IO.File]::Delete($file)
        }
    }
}

function Publish-SharpProofFuzzEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Json
    )

    $destination = Join-Path $OutputDirectory 'campaign.json'
    $temporary = Join-Path $OutputDirectory '.campaign.json.tmp'
    try {
        [IO.File]::WriteAllText(
            $temporary,
            $Json,
            [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($temporary, $destination, $true)
    }
    finally {
        if ([IO.File]::Exists($temporary)) {
            [IO.File]::Delete($temporary)
        }
    }
}
