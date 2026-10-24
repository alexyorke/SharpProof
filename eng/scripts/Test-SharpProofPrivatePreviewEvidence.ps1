[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{40}\z')][string]$ExpectedSourceCommit,
    [Parameter(Mandatory)][ValidatePattern('\A[a-f0-9]{40}\z')][string]$ExpectedWorkflowCommit,
    [Parameter(Mandatory)][ValidatePattern('\Asha256:[a-f0-9]{64}\z')][string]$ExpectedImageDigest,
    [switch]$UnsignedCandidate,
    [string]$CandidateAuthenticationPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$trust = Join-Path $PSScriptRoot '../release/private-preview'
$policy = Get-Content -LiteralPath (Join-Path $trust 'policy.json') -Raw | ConvertFrom-Json
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem([IO.File]::ReadAllText((Join-Path $trust 'signing-public-key.pem')))
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
    if ($rsa.KeySize -ne 3072 -or $policy.algorithm -cne 'RSA3072-SHA256-PKCS1-v1.5' -or
        $policy.publicKeySpkiSha256 -cnotmatch '\A[a-f0-9]{64}\z' -or $fingerprint -cne $policy.publicKeySpkiSha256) { throw 'Pinned private release verifier key or policy mismatch.' }
    $manifestPath = Join-Path $root 'release-manifest.json'
    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    if ($manifestBytes.Length -gt 4MB) { throw 'Private release manifest exceeds its bound.' }
    if ($UnsignedCandidate) {
        $authentication = Get-Content -LiteralPath $CandidateAuthenticationPath -Raw | ConvertFrom-Json
        if ($authentication.kind -cne 'Candidate' -or $authentication.status -cne 'authenticated-extracted' -or
            $authentication.releaseRepository -cne 'alexyorke/SharpProof-preview' -or $authentication.workflowCommit -cne $ExpectedWorkflowCommit -or
            $authentication.workflowPath -cne '.github/workflows/private-preview.yml' -or
            $authentication.artifactId -cnotmatch '\A[1-9][0-9]*\z' -or $authentication.artifactDigest -cnotmatch '\A[a-f0-9]{64}\z' -or
            $authentication.candidateManifestSha256 -cne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant()) { throw 'Unsigned candidate requires authenticated immutable private Actions artifact provenance.' }
    }
    elseif (-not $rsa.VerifyData($manifestBytes, [IO.File]::ReadAllBytes((Join-Path $root 'release-manifest.sig')), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Private release signature is invalid.' }
}
finally { $rsa.Dispose() }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$expectedIds = @('SharpProof', 'SharpProof.Attributes', 'SharpProof.CodeFixes', 'SharpProof.Templates', 'SharpProof.Tool', 'SharpProof.Verifier')
if ($manifest.schemaVersion -ne 1 -or $manifest.releaseRepository -cne 'alexyorke/SharpProof-preview' -or
    $manifest.sourceRepository -cne 'alexyorke/SharpProof' -or $manifest.sourceCommit -cne $ExpectedSourceCommit -or
    $manifest.workflowCommit -cne $ExpectedWorkflowCommit -or $manifest.workflowPath -cne '.github/workflows/private-preview.yml' -or
    $manifest.packageVersion -cne '1.0.0-preview.1' -or $manifest.consumerImage -cne "ghcr.io/alexyorke/sharpproof-preview@$ExpectedImageDigest" -or
    (@($manifest.packages.id | Sort-Object) -join '|') -cne ($expectedIds -join '|')) { throw 'Private release identity, source, workflow, version, image, or package set differs from approval.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $manifest.files) {
    if ($file.fileName -cnotmatch '\A[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*\z' -or '..' -cin $file.fileName.Split('/') -or
        -not $seen.Add($file.fileName) -or $file.sha256 -cnotmatch '\A[a-f0-9]{64}\z') { throw 'Unsafe or duplicate signed evidence filename or invalid hash.' }
    $path = Join-Path $root $file.fileName
    $item = Get-Item -LiteralPath $path
    if ($item.LinkType -or $item.Length -ne $file.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw "Signed evidence bytes changed: $($file.fileName)" }
}
foreach ($package in $manifest.packages) {
    if ($package.version -cne '1.0.0-preview.1' -or $package.repositoryCommit -cne $ExpectedSourceCommit -or
        $package.fileName -cne "$($package.id).1.0.0-preview.1.nupkg" -or -not $seen.Contains($package.fileName)) { throw 'Signed package inventory is inconsistent.' }
    $entry = @($manifest.files | Where-Object fileName -CEQ $package.fileName)
    if ($entry.Count -ne 1 -or $entry[0].sha256 -cne $package.sha256) { throw 'Signed package hash differs from the signed evidence inventory.' }
}
$archiveNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($archive in $manifest.archives) {
    $extension = if ($archive.symbol) { '.snupkg' } else { '.nupkg' }
    if ($archive.id -cnotin $expectedIds -or $archive.fileName -cne "$($archive.id).1.0.0-preview.1$extension" -or
        ($archive.symbol -and $archive.id -cnotin @('SharpProof.Attributes', 'SharpProof.Verifier', 'SharpProof.Tool')) -or
        -not $archiveNames.Add($archive.fileName)) { throw 'Unexpected or duplicate signed archive identity.' }
    $file = @($manifest.files | Where-Object fileName -CEQ $archive.fileName)
    if ($file.Count -ne 1 -or $file[0].sha256 -cne $archive.sha256 -or $file[0].bytes -ne $archive.bytes) { throw 'Archive inventory differs from signed file bytes.' }
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root $archive.fileName))
    try {
        $nuspecs = @($zip.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
        if ($nuspecs.Count -ne 1 -or $nuspecs[0].Length -gt 1MB) { throw 'Archive must contain one bounded NuGet identity document.' }
        $reader = [IO.StreamReader]::new($nuspecs[0].Open())
        try { [xml]$document = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $metadata = $document.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        $ids = @($metadata.SelectNodes('*[local-name()="id"]'))
        $versions = @($metadata.SelectNodes('*[local-name()="version"]'))
        $repositories = @($metadata.SelectNodes('*[local-name()="repository"]'))
        if ($ids.Count -ne 1 -or $ids[0].InnerText -cne $archive.id -or $versions.Count -ne 1 -or $versions[0].InnerText -cne '1.0.0-preview.1' -or
            $repositories.Count -ne 1 -or $repositories[0].GetAttribute('type') -cne 'git' -or
            $repositories[0].GetAttribute('url') -cne 'https://github.com/alexyorke/SharpProof-preview' -or
            $repositories[0].GetAttribute('commit') -cne $ExpectedSourceCommit) { throw 'Signed archive NuGet identity or private mirrored source differs from approval.' }
    }
    finally { $zip.Dispose() }
}
foreach ($name in @($manifest.packages.fileName) + @('SharpProof.Attributes.1.0.0-preview.1.snupkg', 'SharpProof.Verifier.1.0.0-preview.1.snupkg')) {
    if (-not $archiveNames.Contains($name)) { throw 'A required package or exact symbol archive is missing from signed evidence.' }
}
$actualArchiveNames = @($manifest.files | Where-Object { $_.fileName.EndsWith('.nupkg') -or $_.fileName.EndsWith('.snupkg') } | ForEach-Object fileName | Sort-Object)
if (($actualArchiveNames -join '|') -cne ((@($archiveNames) | Sort-Object) -join '|')) { throw 'Signed evidence contains an unclassified archive.' }
$expectedFiles = @($seen) + @('release-manifest.json', 'SHA256SUMS')
if (-not $UnsignedCandidate) { $expectedFiles += 'release-manifest.sig' }
$actualFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') })
$expectedFileList = ($expectedFiles | Sort-Object) -join '|'
$actualFileList = ($actualFiles | Sort-Object) -join '|'
if ($expectedFileList -cne $actualFileList) { throw 'Private release directory has missing or unbound files.' }
$checksumLines = @($manifest.files | ForEach-Object { "$($_.sha256)  $($_.fileName)" }) + "$((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant())  release-manifest.json"
if ([IO.File]::ReadAllText((Join-Path $root 'SHA256SUMS')) -cne (($checksumLines -join "`n") + "`n")) { throw 'Private release checksum inventory differs from the signed manifest.' }
if ($UnsignedCandidate) { Write-Host 'Verified authenticated unsigned candidate inventory; signing and publication remain separate gates.' }
else { Write-Host 'Verified pinned private signature, both approved commits, six identities, exact image, all archive/symbol/SBOM hashes, and complete evidence inventory.' }
