Set-StrictMode -Version Latest

function Assert-SharpProofJsonObject {
    param(
        [Parameter(Mandatory = $true)]$Element,
        [Parameter(Mandatory = $true)][string[]]$Properties,
        [Parameter(Mandatory = $true)][string]$Owner
    )
    if ($Element.ValueKind -ne [Text.Json.JsonValueKind]::Object) {
        throw "$Owner must be an object."
    }
    $actual = [Collections.Generic.List[string]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($property in $Element.EnumerateObject()) {
        if (-not $seen.Add($property.Name)) {
            throw "$Owner contains duplicate property '$($property.Name)'."
        }
        $actual.Add($property.Name)
    }
    if (($actual -join "`0") -cne ($Properties -join "`0")) {
        throw "$Owner does not have the exact canonical property set and order."
    }
}

function Assert-SharpProofJsonKind {
    param($Element, [Text.Json.JsonValueKind]$Kind, [string]$Owner)
    if ($Element.ValueKind -ne $Kind) {
        throw "$Owner has an invalid JSON token type."
    }
}

function Assert-SharpProofJsonArray {
    param($Element, [Text.Json.JsonValueKind]$ItemKind, [string]$Owner)
    Assert-SharpProofJsonKind $Element ([Text.Json.JsonValueKind]::Array) $Owner
    $index = 0
    foreach ($item in $Element.EnumerateArray()) {
        Assert-SharpProofJsonKind $item $ItemKind "$Owner[$index]"
        $index++
    }
}

function Assert-SharpProofJsonInteger {
    param($Element, [string]$Owner)
    Assert-SharpProofJsonKind $Element ([Text.Json.JsonValueKind]::Number) $Owner
    $value = 0L
    if (-not $Element.TryGetInt64([ref]$value)) {
        throw "$Owner must be an integer JSON number."
    }
}

function Assert-SharpProofJsonStringValue {
    param($Element, [string[]]$Allowed, [string]$Owner)
    Assert-SharpProofJsonKind $Element ([Text.Json.JsonValueKind]::String) $Owner
    $value = $Element.GetString()
    if ($Allowed -cnotcontains $value) {
        throw "$Owner has a noncanonical vocabulary value."
    }
}

function Assert-SharpProofReleaseManifestShape {
    param($Root)
    Assert-SharpProofJsonObject $Root @(
        'schemaVersion', 'packageVersion', 'versionAuthority', 'repository',
        'hashAlgorithm', 'artifacts', 'packagePayloads', 'thirdPartyComponents'
    ) 'Release manifest'
    Assert-SharpProofJsonInteger $Root.GetProperty('schemaVersion') 'Release manifest schemaVersion'
    Assert-SharpProofJsonKind $Root.GetProperty('packageVersion') String 'Release manifest packageVersion'
    Assert-SharpProofJsonStringValue $Root.GetProperty('hashAlgorithm') @('SHA256') 'Release manifest hashAlgorithm'

    $authority = $Root.GetProperty('versionAuthority')
    Assert-SharpProofJsonObject $authority @(
        'schemaVersion', 'path', 'property', 'version', 'sha256') 'Release manifest versionAuthority'
    Assert-SharpProofJsonInteger $authority.GetProperty('schemaVersion') 'Release manifest versionAuthority.schemaVersion'
    foreach ($name in @('path', 'property', 'version', 'sha256')) {
        Assert-SharpProofJsonKind $authority.GetProperty($name) String "Release manifest versionAuthority.$name"
    }
    $repository = $Root.GetProperty('repository')
    Assert-SharpProofJsonObject $repository @('type', 'url', 'commit') 'Release manifest repository'
    foreach ($name in @('type', 'url', 'commit')) {
        Assert-SharpProofJsonKind $repository.GetProperty($name) String "Release manifest repository.$name"
    }
    Assert-SharpProofJsonStringValue $repository.GetProperty('type') @('git') 'Release manifest repository.type'

    $artifacts = $Root.GetProperty('artifacts')
    Assert-SharpProofJsonArray $artifacts Object 'Release manifest artifacts'
    $index = 0
    foreach ($row in $artifacts.EnumerateArray()) {
        Assert-SharpProofJsonObject $row @(
            'fileName', 'kind', 'packageId', 'bytes', 'sha256') "Release manifest artifacts[$index]"
        foreach ($name in @('fileName', 'kind', 'sha256')) {
            Assert-SharpProofJsonKind $row.GetProperty($name) String "Release manifest artifacts[$index].$name"
        }
        Assert-SharpProofJsonStringValue $row.GetProperty('kind') @(
            'package', 'symbols', 'sbom') "Release manifest artifacts[$index].kind"
        $packageIdKind = $row.GetProperty('packageId').ValueKind
        if ($packageIdKind -notin @([Text.Json.JsonValueKind]::String, [Text.Json.JsonValueKind]::Null)) {
            throw "Release manifest artifacts[$index].packageId has an invalid JSON token type."
        }
        Assert-SharpProofJsonInteger $row.GetProperty('bytes') "Release manifest artifacts[$index].bytes"
        $index++
    }

    $payloads = $Root.GetProperty('packagePayloads')
    Assert-SharpProofJsonArray $payloads Object 'Release manifest packagePayloads'
    $index = 0
    foreach ($payload in $payloads.EnumerateArray()) {
        Assert-SharpProofJsonObject $payload @('packageId', 'entries') "Release manifest packagePayloads[$index]"
        Assert-SharpProofJsonKind $payload.GetProperty('packageId') String "Release manifest packagePayloads[$index].packageId"
        $entries = $payload.GetProperty('entries')
        Assert-SharpProofJsonArray $entries Object "Release manifest packagePayloads[$index].entries"
        $entryIndex = 0
        foreach ($entry in $entries.EnumerateArray()) {
            Assert-SharpProofJsonObject $entry @(
                'path', 'owner', 'assemblyName', 'bytes', 'sha256') "Release manifest packagePayloads[$index].entries[$entryIndex]"
            foreach ($name in @('path', 'owner', 'sha256')) {
                Assert-SharpProofJsonKind $entry.GetProperty($name) String "Release manifest packagePayloads[$index].entries[$entryIndex].$name"
            }
            Assert-SharpProofJsonStringValue $entry.GetProperty('owner') @(
                'firstParty', 'thirdParty') "Release manifest packagePayloads[$index].entries[$entryIndex].owner"
            $assemblyKind = $entry.GetProperty('assemblyName').ValueKind
            if ($assemblyKind -notin @([Text.Json.JsonValueKind]::String, [Text.Json.JsonValueKind]::Null)) {
                throw "Release manifest packagePayloads[$index].entries[$entryIndex].assemblyName has an invalid JSON token type."
            }
            Assert-SharpProofJsonInteger $entry.GetProperty('bytes') "Release manifest packagePayloads[$index].entries[$entryIndex].bytes"
            $entryIndex++
        }
        $index++
    }

    $components = $Root.GetProperty('thirdPartyComponents')
    Assert-SharpProofJsonArray $components Object 'Release manifest thirdPartyComponents'
    $index = 0
    foreach ($component in $components.EnumerateArray()) {
        Assert-SharpProofJsonObject $component @(
            'packageId', 'id', 'version', 'license', 'entries') "Release manifest thirdPartyComponents[$index]"
        foreach ($name in @('packageId', 'id', 'version', 'license')) {
            Assert-SharpProofJsonKind $component.GetProperty($name) String "Release manifest thirdPartyComponents[$index].$name"
        }
        Assert-SharpProofJsonArray $component.GetProperty('entries') String "Release manifest thirdPartyComponents[$index].entries"
        $index++
    }
}

function Assert-SharpProofSpdxShape {
    param($Root)
    Assert-SharpProofJsonObject $Root @(
        'spdxVersion', 'dataLicense', 'SPDXID', 'name', 'documentNamespace',
        'creationInfo', 'documentDescribes', 'packages', 'relationships') 'SPDX document'
    foreach ($name in @('spdxVersion', 'dataLicense', 'SPDXID', 'name', 'documentNamespace')) {
        Assert-SharpProofJsonKind $Root.GetProperty($name) String "SPDX document $name"
    }
    Assert-SharpProofJsonStringValue $Root.GetProperty('spdxVersion') @('SPDX-2.3') 'SPDX document spdxVersion'
    Assert-SharpProofJsonStringValue $Root.GetProperty('dataLicense') @('CC0-1.0') 'SPDX document dataLicense'
    Assert-SharpProofJsonStringValue $Root.GetProperty('SPDXID') @('SPDXRef-DOCUMENT') 'SPDX document SPDXID'
    $creation = $Root.GetProperty('creationInfo')
    Assert-SharpProofJsonObject $creation @('created', 'creators', 'comment') 'SPDX creationInfo'
    Assert-SharpProofJsonKind $creation.GetProperty('created') String 'SPDX creationInfo.created'
    Assert-SharpProofJsonArray $creation.GetProperty('creators') String 'SPDX creationInfo.creators'
    Assert-SharpProofJsonKind $creation.GetProperty('comment') String 'SPDX creationInfo.comment'
    Assert-SharpProofJsonArray $Root.GetProperty('documentDescribes') String 'SPDX documentDescribes'

    $packages = $Root.GetProperty('packages')
    Assert-SharpProofJsonArray $packages Object 'SPDX packages'
    $index = 0
    foreach ($package in $packages.EnumerateArray()) {
        $names = @($package.EnumerateObject() | ForEach-Object Name)
        $firstParty = $names -contains 'checksums'
        $expected = @('name', 'SPDXID', 'versionInfo', 'downloadLocation', 'filesAnalyzed')
        if ($firstParty) { $expected += 'checksums' }
        $expected += @('licenseConcluded', 'licenseDeclared', 'copyrightText', 'externalRefs')
        Assert-SharpProofJsonObject $package $expected "SPDX packages[$index]"
        foreach ($name in @('name', 'SPDXID', 'versionInfo', 'downloadLocation', 'licenseConcluded', 'licenseDeclared', 'copyrightText')) {
            Assert-SharpProofJsonKind $package.GetProperty($name) String "SPDX packages[$index].$name"
        }
        Assert-SharpProofJsonStringValue $package.GetProperty('downloadLocation') @(
            'NOASSERTION') "SPDX packages[$index].downloadLocation"
        Assert-SharpProofJsonStringValue $package.GetProperty('copyrightText') @(
            'NOASSERTION') "SPDX packages[$index].copyrightText"
        Assert-SharpProofJsonKind $package.GetProperty('filesAnalyzed') False "SPDX packages[$index].filesAnalyzed"
        if ($firstParty) {
            $checksums = $package.GetProperty('checksums')
            Assert-SharpProofJsonArray $checksums Object "SPDX packages[$index].checksums"
            $checksumIndex = 0
            foreach ($checksum in $checksums.EnumerateArray()) {
                Assert-SharpProofJsonObject $checksum @('algorithm', 'checksumValue') "SPDX packages[$index].checksums[$checksumIndex]"
                Assert-SharpProofJsonKind $checksum.GetProperty('algorithm') String "SPDX packages[$index].checksums[$checksumIndex].algorithm"
                Assert-SharpProofJsonKind $checksum.GetProperty('checksumValue') String "SPDX packages[$index].checksums[$checksumIndex].checksumValue"
                Assert-SharpProofJsonStringValue $checksum.GetProperty('algorithm') @(
                    'SHA256') "SPDX packages[$index].checksums[$checksumIndex].algorithm"
                $checksumIndex++
            }
        }
        $refs = $package.GetProperty('externalRefs')
        Assert-SharpProofJsonArray $refs Object "SPDX packages[$index].externalRefs"
        $refIndex = 0
        foreach ($ref in $refs.EnumerateArray()) {
            Assert-SharpProofJsonObject $ref @(
                'referenceCategory', 'referenceType', 'referenceLocator') "SPDX packages[$index].externalRefs[$refIndex]"
            foreach ($name in @('referenceCategory', 'referenceType', 'referenceLocator')) {
                Assert-SharpProofJsonKind $ref.GetProperty($name) String "SPDX packages[$index].externalRefs[$refIndex].$name"
            }
            Assert-SharpProofJsonStringValue $ref.GetProperty('referenceCategory') @(
                'PACKAGE-MANAGER') "SPDX packages[$index].externalRefs[$refIndex].referenceCategory"
            Assert-SharpProofJsonStringValue $ref.GetProperty('referenceType') @(
                'purl') "SPDX packages[$index].externalRefs[$refIndex].referenceType"
            $refIndex++
        }
        $index++
    }
    $relationships = $Root.GetProperty('relationships')
    Assert-SharpProofJsonArray $relationships Object 'SPDX relationships'
    $index = 0
    foreach ($relationship in $relationships.EnumerateArray()) {
        Assert-SharpProofJsonObject $relationship @(
            'spdxElementId', 'relationshipType', 'relatedSpdxElement') "SPDX relationships[$index]"
        foreach ($name in @('spdxElementId', 'relationshipType', 'relatedSpdxElement')) {
            Assert-SharpProofJsonKind $relationship.GetProperty($name) String "SPDX relationships[$index].$name"
        }
        Assert-SharpProofJsonStringValue $relationship.GetProperty('relationshipType') @(
            'DESCRIBES', 'CONTAINS', 'DEPENDS_ON') "SPDX relationships[$index].relationshipType"
        $index++
    }
}

function Read-SharpProofCanonicalReleaseJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]
        [ValidateSet('ReleaseManifest', 'Spdx')][string]$DocumentType
    )
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $bytes = [IO.File]::ReadAllBytes($resolved)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "$DocumentType JSON must be UTF-8 without a BOM."
    }
    $encoding = [Text.UTF8Encoding]::new($false, $true)
    $text = $encoding.GetString($bytes)
    try {
        $document = [Text.Json.JsonDocument]::Parse($text)
    }
    catch {
        throw "$DocumentType JSON is not strict JSON: $($_.Exception.Message)"
    }
    try {
        if ($DocumentType -eq 'ReleaseManifest') {
            Assert-SharpProofReleaseManifestShape $document.RootElement
            $depth = 8
        }
        else {
            Assert-SharpProofSpdxShape $document.RootElement
            $depth = 10
        }
    }
    finally {
        $document.Dispose()
    }
    $value = $text | ConvertFrom-Json -DateKind String
    $canonical = (($value | ConvertTo-Json -Depth $depth) -replace "`r`n", "`n") + "`n"
    if ($text -cne $canonical) {
        throw "$DocumentType JSON bytes are not canonical."
    }
    return $value
}
