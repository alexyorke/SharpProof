[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpproof-private-regression-' + [Guid]::NewGuid().ToString('N'))
$scripts = Join-Path $scratch 'eng/scripts'
$trust = Join-Path $scratch 'eng/release/private-preview'
$evidence = Join-Path $scratch 'evidence'
[void][IO.Directory]::CreateDirectory($scripts)
[void][IO.Directory]::CreateDirectory($trust)
[void][IO.Directory]::CreateDirectory($evidence)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewEvidence.ps1') -Destination $scripts
$rsa = [Security.Cryptography.RSA]::Create(3072)
$source = '1' * 40
$workflow = '2' * 40
$digest = 'sha256:' + ('3' * 64)
$ids = @('SharpProof', 'SharpProof.Attributes', 'SharpProof.CodeFixes', 'SharpProof.Templates', 'SharpProof.Tool', 'SharpProof.Verifier')
$controls = 0
function Write-Json([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20) + "`n", [Text.UTF8Encoding]::new($false))
}
function Protect-Fixture([object]$Manifest) {
    $manifestPath = Join-Path $evidence 'release-manifest.json'
    Write-Json $manifestPath $Manifest
    [IO.File]::WriteAllBytes((Join-Path $evidence 'release-manifest.sig'), $rsa.SignData([IO.File]::ReadAllBytes($manifestPath), [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1))
    $lines = @($Manifest.files | ForEach-Object { "$($_.sha256)  $($_.fileName)" }) + "$((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant())  release-manifest.json"
    [IO.File]::WriteAllLines((Join-Path $evidence 'SHA256SUMS'), $lines, [Text.UTF8Encoding]::new($false))
}
function Test-Fixture([string]$ExpectedSource = $source, [string]$ExpectedWorkflow = $workflow, [string]$ExpectedDigest = $digest) {
    & (Join-Path $scripts 'Test-SharpProofPrivatePreviewEvidence.ps1') -EvidenceDirectory $evidence -ExpectedSourceCommit $ExpectedSource -ExpectedWorkflowCommit $ExpectedWorkflow -ExpectedImageDigest $ExpectedDigest
}
function Assert-FixtureRejection([string]$Name, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw "Private release control did not reject: $Name" }
    $script:controls++
}
try {
    [IO.File]::WriteAllText((Join-Path $trust 'signing-public-key.pem'), $rsa.ExportSubjectPublicKeyInfoPem() + "`n", [Text.UTF8Encoding]::new($false))
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
    Write-Json (Join-Path $trust 'policy.json') ([ordered]@{ algorithm = 'RSA3072-SHA256-PKCS1-v1.5'; publicKeySpkiSha256 = $fingerprint })
    $files = [Collections.Generic.List[object]]::new()
    $packages = [Collections.Generic.List[object]]::new()
    $archives = [Collections.Generic.List[object]]::new()
    foreach ($id in $ids) {
        $extensions = if ($id -cin @('SharpProof.Attributes', 'SharpProof.Verifier')) { @('.nupkg', '.snupkg') } else { @('.nupkg') }
        foreach ($extension in $extensions) {
            $name = "$id.1.0.0-preview.1$extension"
            $path = Join-Path $evidence $name
            $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
            try {
                $entry = $zip.CreateEntry("$id.nuspec")
                $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                try { $writer.Write("<package><metadata><id>$id</id><version>1.0.0-preview.1</version><repository type=`"git`" url=`"https://github.com/alexyorke/SharpProof-preview`" commit=`"$source`"/></metadata></package>") }
                finally { $writer.Dispose() }
            }
            finally { $zip.Dispose() }
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            $length = (Get-Item -LiteralPath $path).Length
            $files.Add([ordered]@{ fileName = $name; bytes = $length; sha256 = $hash })
            $archives.Add([ordered]@{ fileName = $name; id = $id; symbol = $extension -ceq '.snupkg'; bytes = $length; sha256 = $hash })
            if ($extension -ceq '.nupkg') { $packages.Add([ordered]@{ id = $id; version = '1.0.0-preview.1'; repositoryCommit = $source; fileName = $name; sha256 = $hash }) }
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1; releaseRepository = 'alexyorke/SharpProof-preview'; sourceRepository = 'alexyorke/SharpProof'; sourceCommit = $source; workflowCommit = $workflow
        workflowPath = '.github/workflows/private-preview.yml'; packageVersion = '1.0.0-preview.1'; consumerImage = "ghcr.io/alexyorke/sharpproof-preview@$digest"
        packages = @($packages.ToArray()); archives = @($archives.ToArray()); files = @($files.ToArray())
    }
    Protect-Fixture $manifest
    Test-Fixture
    $controls++
    $authenticationPath = Join-Path $scratch 'candidate-authentication.json'
    $authentication = [ordered]@{ kind = 'Candidate'; status = 'authenticated-extracted'; releaseRepository = 'alexyorke/SharpProof-preview'; workflowCommit = $workflow; workflowPath = '.github/workflows/private-preview.yml'; artifactId = '123'; artifactDigest = '5' * 64; candidateManifestSha256 = (Get-FileHash -LiteralPath (Join-Path $evidence 'release-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant() }
    Write-Json $authenticationPath $authentication
    Remove-Item -LiteralPath (Join-Path $evidence 'release-manifest.sig')
    $unsignedArguments = @{ EvidenceDirectory = $evidence; ExpectedSourceCommit = $source; ExpectedWorkflowCommit = $workflow; ExpectedImageDigest = $digest; UnsignedCandidate = $true; CandidateAuthenticationPath = $authenticationPath }
    & (Join-Path $scripts 'Test-SharpProofPrivatePreviewEvidence.ps1') @unsignedArguments
    $controls++
    Assert-FixtureRejection 'unsigned candidate cannot satisfy signed release verification' { Test-Fixture }
    $authentication.workflowCommit = '4' * 40
    Write-Json $authenticationPath $authentication
    Assert-FixtureRejection 'unsigned candidate wrong originating workflow' { & (Join-Path $scripts 'Test-SharpProofPrivatePreviewEvidence.ps1') @unsignedArguments }
    $authentication.workflowCommit = $workflow
    $authentication.kind = 'Acceptance'
    Write-Json $authenticationPath $authentication
    Assert-FixtureRejection 'acceptance artifact cannot authenticate candidate' { & (Join-Path $scripts 'Test-SharpProofPrivatePreviewEvidence.ps1') @unsignedArguments }
    $authentication.kind = 'Candidate'
    $authentication.artifactDigest = 'unavailable'
    Write-Json $authenticationPath $authentication
    Assert-FixtureRejection 'unsigned candidate missing immutable artifact digest' { & (Join-Path $scripts 'Test-SharpProofPrivatePreviewEvidence.ps1') @unsignedArguments }
    Protect-Fixture $manifest
    Assert-FixtureRejection 'approved source mismatch' { Test-Fixture -ExpectedSource ('4' * 40) }
    Assert-FixtureRejection 'approved workflow mismatch' { Test-Fixture -ExpectedWorkflow ('4' * 40) }
    Assert-FixtureRejection 'candidate image mismatch' { Test-Fixture -ExpectedDigest ('sha256:' + ('4' * 64)) }
    Assert-FixtureRejection 'source trailing newline' { Test-Fixture -ExpectedSource ($source + "`n") }
    $signaturePath = Join-Path $evidence 'release-manifest.sig'
    $signature = [IO.File]::ReadAllBytes($signaturePath)
    $signature[0] = $signature[0] -bxor 1
    [IO.File]::WriteAllBytes($signaturePath, $signature)
    Assert-FixtureRejection 'signature mutation' { Test-Fixture }
    Protect-Fixture $manifest
    $archivePath = Join-Path $evidence $manifest.packages[0].fileName
    $originalArchive = [IO.File]::ReadAllBytes($archivePath)
    [IO.File]::WriteAllText($archivePath, 'changed package bytes')
    Assert-FixtureRejection 'archive mutation' { Test-Fixture }
    [IO.File]::WriteAllBytes($archivePath, $originalArchive)
    [IO.File]::WriteAllText((Join-Path $evidence '.unexpected'), 'unbound file')
    Assert-FixtureRejection 'unbound hidden file' { Test-Fixture }
    Remove-Item -LiteralPath (Join-Path $evidence '.unexpected') -Force
    $originalChecksum = [IO.File]::ReadAllText((Join-Path $evidence 'SHA256SUMS'))
    [IO.File]::WriteAllText((Join-Path $evidence 'SHA256SUMS'), 'changed checksum')
    Assert-FixtureRejection 'checksum mutation' { Test-Fixture }
    [IO.File]::WriteAllText((Join-Path $evidence 'SHA256SUMS'), $originalChecksum)
    foreach ($property in @('releaseRepository', 'sourceRepository', 'packageVersion', 'workflowPath')) {
        $previous = $manifest[$property]
        $manifest[$property] = 'unapproved'
        Protect-Fixture $manifest
        Assert-FixtureRejection "signed invalid $property" { Test-Fixture }
        $manifest[$property] = $previous
    }
    $previousPackages = $manifest.packages
    $manifest.packages = @($manifest.packages[0]) + @($manifest.packages)
    Protect-Fixture $manifest
    Assert-FixtureRejection 'signed duplicate package identity' { Test-Fixture }
    $manifest.packages = $previousPackages
    $previousArchives = $manifest.archives
    $manifest.archives = @($manifest.archives | Where-Object fileName -CNE 'SharpProof.Attributes.1.0.0-preview.1.snupkg')
    Protect-Fixture $manifest
    Assert-FixtureRejection 'missing signed required symbols' { Test-Fixture }
    $manifest.archives = $previousArchives
    $previousName = $manifest.files[0].fileName
    $manifest.files[0].fileName = '../escape.nupkg'
    Protect-Fixture $manifest
    Assert-FixtureRejection 'signed traversal path' { Test-Fixture }
    $manifest.files[0].fileName = $previousName
    Protect-Fixture $manifest
    Write-Json (Join-Path $trust 'policy.json') ([ordered]@{ algorithm = 'RSA3072-SHA256-PKCS1-v1.5'; publicKeySpkiSha256 = '0' * 64 })
    Assert-FixtureRejection 'wrong pinned verifier fingerprint' { Test-Fixture }
    Write-Host "Private preview signed-envelope regressions passed: $controls controls. Fixture key only; no registry, environment, protected-key, package-build, or publication claim."
    & (Join-Path $PSScriptRoot 'Test-SharpProofPrivatePreviewPolicyRegression.ps1')
}
finally {
    $rsa.Dispose()
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -cne [IO.Path]::GetTempPath().TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolved) -cnotmatch '\Asharpproof-private-regression-[a-f0-9]{32}\z') { throw 'Refusing unsafe private regression fixture cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
