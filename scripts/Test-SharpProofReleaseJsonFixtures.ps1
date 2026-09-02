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

function Assert-Accepted {
    param([string]$Name, [string]$Text, [string]$Type)
    $script:total++
    $path = Write-Fixture "$Name.json" $Text
    $null = Read-SharpProofCanonicalReleaseJson -Path $path -DocumentType $Type
    $script:passed++
}

function Assert-Rejected {
    param([string]$Name, [string]$Text, [string]$Type)
    $script:total++
    $path = Write-Fixture "$Name.json" $Text
    try {
        $null = Read-SharpProofCanonicalReleaseJson -Path $path -DocumentType $Type
    }
    catch {
        $script:passed++
        return
    }
    throw "Fixture '$Name' was accepted."
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

    $spdx = [pscustomobject][ordered]@{
        spdxVersion = 'SPDX-2.3'; dataLicense = 'CC0-1.0'; SPDXID = 'SPDXRef-DOCUMENT'
        name = 'SharpProof-1.0.0-preview.1'; documentNamespace = 'https://example.invalid/sbom'
        creationInfo = [pscustomobject][ordered]@{
            created = '2026-01-01T00:00:00Z'; creators = @('Tool: SharpProof release evidence')
            comment = 'Timestamp is derived from the source commit for reproducibility.'
        }
        documentDescribes = @('SPDXRef-Package-SharpProof')
        packages = @([pscustomobject][ordered]@{
            name = 'SharpProof'; SPDXID = 'SPDXRef-Package-SharpProof'
            versionInfo = '1.0.0-preview.1'; downloadLocation = 'NOASSERTION'
            filesAnalyzed = $false
            licenseConcluded = 'MIT'; licenseDeclared = 'MIT'; copyrightText = 'NOASSERTION'
            externalRefs = @([pscustomobject][ordered]@{
                referenceCategory = 'PACKAGE-MANAGER'; referenceType = 'purl'
                referenceLocator = 'pkg:nuget/SharpProof@1.0.0-preview.1'
            })
        })
        relationships = @([pscustomobject][ordered]@{
            spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'
            relatedSpdxElement = 'SPDXRef-Package-SharpProof'
        })
    }
    $spdxJson = (($spdx | ConvertTo-Json -Depth 10) -replace "`r`n", "`n") + "`n"
    Assert-Accepted spdx-canonical $spdxJson Spdx
    Assert-Rejected spdx-duplicate-top ($spdxJson.Replace(
        '  "spdxVersion": "SPDX-2.3",',
        "  `"spdxVersion`": `"SPDX-9.9`",`n  `"spdxVersion`": `"SPDX-2.3`",")) Spdx
    Assert-Rejected spdx-duplicate-nested ($spdxJson.Replace(
        '    "created": "2026-01-01T00:00:00Z",',
        "    `"created`": `"forged`",`n    `"created`": `"2026-01-01T00:00:00Z`",")) Spdx
    Assert-Rejected spdx-case-field ($spdxJson.Replace('"SPDXID":', '"spdxId":')) Spdx
    Assert-Rejected spdx-vocabulary-case ($spdxJson.Replace('"spdxVersion": "SPDX-2.3"', '"spdxVersion": "spdx-2.3"')) Spdx
    Assert-Rejected spdx-relationship-case ($spdxJson.Replace('"relationshipType": "DESCRIBES"', '"relationshipType": "describes"')) Spdx
    Assert-Rejected spdx-scalar-array ($spdxJson.Replace(
        '"documentDescribes": [', '"documentDescribes": "SPDXRef-Package-SharpProof", "discarded": [')) Spdx
    Assert-Rejected spdx-nested-array ($spdxJson.Replace(
        '"relationships": [', '"relationships": [[').Replace("  ]`n}", "  ]]`n}")) Spdx
    Assert-Rejected spdx-unknown-row ($spdxJson.Replace(
        '      "relationshipType": "DESCRIBES",',
        "      `"relationshipType`": `"DESCRIBES`",`n      `"foreign`": true,")) Spdx
    Assert-Rejected spdx-reordered ($spdxJson.Replace(
        "  `"spdxVersion`": `"SPDX-2.3`",`n  `"dataLicense`": `"CC0-1.0`",",
        "  `"dataLicense`": `"CC0-1.0`",`n  `"spdxVersion`": `"SPDX-2.3`",")) Spdx
    Assert-Rejected spdx-whitespace ($spdxJson.Replace('  "spdxVersion"', '    "spdxVersion"')) Spdx

    [pscustomobject][ordered]@{ passed = $passed; total = $total } |
        ConvertTo-Json -Compress
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
}
