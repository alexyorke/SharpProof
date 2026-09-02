[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SharpProof.ReleaseJson.ps1')

$root = Join-Path ([IO.Path]::GetTempPath()) (
    'sharpproof-release-json-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
$passed = 0
$total = 0

function Write-Fixture {
    param([string]$Name, [string]$Text)
    $path = Join-Path $root $Name
    [IO.File]::WriteAllText($path, $Text, [Text.UTF8Encoding]::new($false))
    return $path
}

function Assert-Fixture {
    param(
        [string]$Name,
        [string]$Text,
        [string]$Type,
        [switch]$ExpectRejected
    )
    $script:total++
    $assertion = @{
        Name = $Name
        Write = { Write-Fixture "$Name.json" $Text }
        Validate = {
            param($path)
            $null = Read-SharpProofCanonicalReleaseJson `
                -Path $path -DocumentType $Type
        }
    }
    if ($ExpectRejected) {
        $assertion.ExpectRejected = $true
    }
    Invoke-SharpProofFixtureAssertion @assertion
    $script:passed++
}

function Assert-Accepted {
    param([string]$Name, [string]$Text, [string]$Type)
    Assert-Fixture $Name $Text $Type
}

function Assert-Rejected {
    param([string]$Name, [string]$Text, [string]$Type)
    Assert-Fixture $Name $Text $Type -ExpectRejected
}

try {
    $manifest = [pscustomobject][ordered]@{
        schemaVersion = 2
        packageVersion = '1.0.0-preview.1'
        versionAuthority = [pscustomobject][ordered]@{
            schemaVersion = 1; path = 'SharpProof.Release.props'
            property = 'SharpProofPackageVersion'; version = '1.0.0-preview.1'
        }
        repository = [pscustomobject][ordered]@{
            type = 'git'; url = 'https://example.invalid/repo'; commit = ('b' * 40)
        }
        artifacts = @([pscustomobject][ordered]@{
            fileName = 'SharpProof.1.0.0-preview.1.nupkg'; kind = 'package'
            packageId = 'SharpProof'; bytes = [int64]1
        })
        packagePayloads = @([pscustomobject][ordered]@{
            packageId = 'SharpProof'; entries = @([pscustomobject][ordered]@{
                path = 'analyzers/dotnet/cs/SharpProof.dll'; owner = 'firstParty'
                assemblyName = 'SharpProof'; bytes = [int64]1
            })
        })
        thirdPartyComponents = @([pscustomobject][ordered]@{
            packageId = 'SharpProof.Verifier'; id = 'Z3'; version = '4.12.2'
            license = 'MIT'; entries = @('tools/native/linux-x64/libz3.so')
        })
    }
    $manifestJson = (($manifest | ConvertTo-Json -Depth 8) -replace "`r`n", "`n") + "`n"
    Assert-Accepted manifest-canonical $manifestJson ReleaseManifest
    Assert-Rejected manifest-duplicate-first ($manifestJson.Replace(
        '  "packageVersion": "1.0.0-preview.1",',
        "  `"packageVersion`": `"999.0.0`",`n  `"packageVersion`": `"1.0.0-preview.1`",")) ReleaseManifest
    Assert-Rejected manifest-duplicate-last ($manifestJson.Replace(
        '  "packageVersion": "1.0.0-preview.1",',
        "  `"packageVersion`": `"1.0.0-preview.1`",`n  `"packageVersion`": `"999.0.0`",")) ReleaseManifest
    Assert-Rejected manifest-nested-duplicate ($manifestJson.Replace(
        '    "type": "git",', "    `"type`": `"svn`",`n    `"type`": `"git`",")) ReleaseManifest
    Assert-Rejected manifest-row-duplicate ($manifestJson.Replace(
        '      "kind": "package",', "      `"kind`": `"symbols`",`n      `"kind`": `"package`",")) ReleaseManifest
    Assert-Rejected manifest-unknown ($manifestJson.Replace(
        '  "packageVersion": "1.0.0-preview.1",',
        "  `"unknown`": true,`n  `"packageVersion`": `"1.0.0-preview.1`",")) ReleaseManifest
    Assert-Rejected manifest-case ($manifestJson.Replace('"artifacts":', '"Artifacts":')) ReleaseManifest
    Assert-Rejected manifest-kind-case ($manifestJson.Replace('"kind": "package"', '"kind": "Package"')) ReleaseManifest
    Assert-Rejected manifest-number-string ($manifestJson.Replace('"bytes": 1', '"bytes": "1"')) ReleaseManifest
    Assert-Rejected manifest-scalar-array ($manifestJson.Replace(
        '"artifacts": [', '"artifacts": {').Replace(
        "  ],`n  `"packagePayloads`"", "  },`n  `"packagePayloads`"")) ReleaseManifest
    Assert-Rejected manifest-nested-array ($manifestJson.Replace(
        '"artifacts": [', '"artifacts": [[').Replace(
        "  ],`n  `"packagePayloads`"", "  ]],`n  `"packagePayloads`"")) ReleaseManifest
    Assert-Rejected manifest-reordered ($manifestJson.Replace(
        "  `"schemaVersion`": 2,`n  `"packageVersion`": `"1.0.0-preview.1`",",
        "  `"packageVersion`": `"1.0.0-preview.1`",`n  `"schemaVersion`": 2,")) ReleaseManifest
    Assert-Rejected manifest-whitespace ($manifestJson.Replace('  "schemaVersion"', '    "schemaVersion"')) ReleaseManifest


    [pscustomobject][ordered]@{ passed = $passed; total = $total } |
        ConvertTo-Json -Compress
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
}
