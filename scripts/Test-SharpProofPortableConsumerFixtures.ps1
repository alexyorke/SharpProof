[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-TestHostOsFamily {
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::Linux)) {
        return 'linux'
    }
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::Windows)) {
        return 'windows'
    }
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::OSX)) {
        return 'macos'
    }
    throw 'The fixture cannot identify the test host operating system.'
}

function Get-TestSpoofedOsFamily([string]$Actual) {
    if ($Actual -cne 'linux') { return 'linux' }
    return 'windows'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostOsFamily = Get-TestHostOsFamily
$spoofedOsFamily = Get-TestSpoofedOsFamily $hostOsFamily
$fixture = Join-Path ([IO.Path]::GetTempPath()) (
    'SharpProof-portable-consumer-' + [Guid]::NewGuid().ToString('N'))
$fixtureScripts = Join-Path $fixture 'scripts'
$fixtureArtifacts = Join-Path $fixture 'artifacts/release-qualification'
$fixturePackageSource = Join-Path $fixture 'nupkgs'
New-Item -ItemType Directory -Path `
    $fixtureScripts, $fixtureArtifacts, $fixturePackageSource -Force | Out-Null

try {
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/Test-SharpProofPortableConsumer.ps1') `
        -Destination (Join-Path $fixtureScripts 'Test-SharpProofPortableConsumer.ps1')
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot 'scripts/SharpProof.ReleaseConfigurationEvidence.psm1') `
        -Destination (Join-Path $fixtureScripts 'SharpProof.ReleaseConfigurationEvidence.psm1')
    $consumerStub = Join-Path $fixtureScripts 'Test-SharpProofPackageConsumers.ps1'
    @'
[CmdletBinding()]
param(
    [string]$PackageSource,
    [switch]$FrameworkConsumersOnly
)
exit 0
'@ | Set-Content -LiteralPath $consumerStub -Encoding utf8
    $receiptStub = Join-Path $fixtureScripts 'Write-SharpProofQualificationReceipt.ps1'
    @'
[CmdletBinding()]
param(
    [string]$Gate,
    [string]$EvidencePath
)
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$path = Join-Path $root "artifacts/release-qualification/qualification-receipts/$Gate.json"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
[IO.File]::WriteAllText($path, "receipt`n")
exit 0
'@ | Set-Content -LiteralPath $receiptStub -Encoding utf8

    & git -C $fixture init --quiet
    & git -C $fixture config user.email 'fixture@example.invalid'
    & git -C $fixture config user.name 'SharpProof fixture'
    [IO.File]::WriteAllText((Join-Path $fixture 'placeholder.txt'), "fixture`n")
    & git -C $fixture add --all
    & git -C $fixture commit --quiet -m fixture
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the portable fixture repository.' }

    $output = @(& pwsh -NoLogo -NoProfile -File (
            Join-Path $fixtureScripts 'Test-SharpProofPortableConsumer.ps1') `
            -PackageSource $fixturePackageSource -OsFamily $spoofedOsFamily 2>&1)
    $exitCode = $LASTEXITCODE
    $spoofedEvidence = Join-Path $fixtureArtifacts "portable-$spoofedOsFamily.json"
    if ($exitCode -eq 0 -or (Test-Path -LiteralPath $spoofedEvidence)) {
        throw "Cross-OS portable qualification was accepted or published evidence: $($output -join [Environment]::NewLine)"
    }

    $output = @(& pwsh -NoLogo -NoProfile -File (
            Join-Path $fixtureScripts 'Test-SharpProofPortableConsumer.ps1') `
            -PackageSource $fixturePackageSource -OsFamily $hostOsFamily 2>&1)
    $exitCode = $LASTEXITCODE
    $matchingEvidence = Join-Path $fixtureArtifacts "portable-$hostOsFamily.json"
    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $matchingEvidence)) {
        throw "Matching-host portable qualification failed: $($output -join [Environment]::NewLine)"
    }
    $matching = Get-Content -LiteralPath $matchingEvidence -Raw |
        ConvertFrom-Json -ErrorAction Stop
    if ([string]$matching.osFamily -cne $hostOsFamily -or
        [string]$matching.architecture -eq '' -or
        [string]$matching.attemptId -notmatch '^[0-9a-f]{32}$') {
        throw 'Matching-host portable evidence did not record runtime provenance.'
    }
    $matchingReceipt = Join-Path $fixtureArtifacts `
        "qualification-receipts/portable-$hostOsFamily.json"
    if (-not (Test-Path -LiteralPath $matchingReceipt -PathType Leaf)) {
        throw 'Matching-host portable qualification did not publish a receipt.'
    }

    @'
[CmdletBinding()]
param(
    [string]$PackageSource,
    [switch]$FrameworkConsumersOnly
)
exit 1
'@ | Set-Content -LiteralPath $consumerStub -Encoding utf8
    $output = @(& pwsh -NoLogo -NoProfile -File (
            Join-Path $fixtureScripts 'Test-SharpProofPortableConsumer.ps1') `
            -PackageSource $fixturePackageSource -OsFamily $hostOsFamily 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0 -or
        (Test-Path -LiteralPath $matchingEvidence -PathType Leaf) -or
        (Test-Path -LiteralPath $matchingReceipt -PathType Leaf)) {
        throw "Failed portable child preserved a prior passing pair: $($output -join [Environment]::NewLine)"
    }

    @'
[CmdletBinding()]
param(
    [string]$PackageSource,
    [switch]$FrameworkConsumersOnly
)
exit 0
'@ | Set-Content -LiteralPath $consumerStub -Encoding utf8
    @'
[CmdletBinding()]
param(
    [string]$Gate,
    [string]$EvidencePath
)
exit 1
'@ | Set-Content -LiteralPath $receiptStub -Encoding utf8
    $output = @(& pwsh -NoLogo -NoProfile -File (
            Join-Path $fixtureScripts 'Test-SharpProofPortableConsumer.ps1') `
            -PackageSource $fixturePackageSource -OsFamily $hostOsFamily 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0 -or
        (Test-Path -LiteralPath $matchingEvidence -PathType Leaf) -or
        (Test-Path -LiteralPath $matchingReceipt -PathType Leaf)) {
        throw "Failed receipt publication preserved a passing pair: $($output -join [Environment]::NewLine)"
    }

    $receiptFixture = Join-Path $fixture 'receipt-repo'
    $receiptScripts = Join-Path $receiptFixture 'scripts'
    $receiptEvidence = Join-Path $receiptFixture 'portable-spoof.json'
    $receiptOutput = Join-Path $receiptFixture 'qualification-receipts'
    $receiptPackages = Join-Path $receiptFixture 'nupkgs'
    New-Item -ItemType Directory -Path `
        $receiptScripts, $receiptOutput, $receiptPackages -Force | Out-Null
    try {
        foreach ($dependency in @(
                'Write-SharpProofQualificationReceipt.ps1',
                'Test-SharpProofPilotReport.ps1',
                'SharpProof.MutationEvidence.psm1',
                'SharpProof.ReleaseConfigurationEvidence.psm1')) {
            Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts/$dependency") `
                -Destination (Join-Path $receiptScripts $dependency)
        }
        & git -C $receiptFixture init --quiet
        & git -C $receiptFixture config user.email 'fixture@example.invalid'
        & git -C $receiptFixture config user.name 'SharpProof fixture'
        [IO.File]::WriteAllText(
            (Join-Path $receiptFixture 'placeholder.txt'), "fixture`n")
        & git -C $receiptFixture add --all
        & git -C $receiptFixture commit --quiet -m fixture
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not initialize the receipt fixture repository.'
        }
        $packageArtifacts = @(
            'a.nupkg', 'b.nupkg', 'c.nupkg',
            'a.snupkg', 'b.snupkg', 'c.snupkg') | ForEach-Object {
                [IO.File]::WriteAllBytes(
                    (Join-Path $receiptPackages $_), [byte[]](1, 2, 3))
                [ordered]@{
                    fileName = $_
                    bytes = 3
                    sha256 = '0000000000000000000000000000000000000000000000000000000000000000'
                }
            }
        $commit = (& git -C $receiptFixture rev-parse HEAD).Trim()
        $evidence = [ordered]@{
            schemaVersion = 1
            status = 'passed'
            commit = $commit
            osFamily = $spoofedOsFamily
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
            attemptId = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
            packageArtifacts = $packageArtifacts
        }
        [IO.File]::WriteAllText(
            $receiptEvidence,
            (($evidence | ConvertTo-Json -Depth 4) + "`n"),
            [Text.UTF8Encoding]::new($false))
        $output = @(& pwsh -NoLogo -NoProfile -File (
                Join-Path $receiptScripts 'Write-SharpProofQualificationReceipt.ps1') `
                -Gate "portable-$spoofedOsFamily" `
                -EvidencePath $receiptEvidence `
                -ReceiptDirectory $receiptOutput `
                -RepositoryRoot $receiptFixture 2>&1)
        $exitCode = $LASTEXITCODE
        $receiptPath = Join-Path $receiptOutput "portable-$spoofedOsFamily.json"
        if ($exitCode -eq 0 -or (Test-Path -LiteralPath $receiptPath)) {
            throw "Receipt writer accepted a cross-OS portable claim: $($output -join [Environment]::NewLine)"
        }
    }
    finally {
        if (Test-Path -LiteralPath $receiptFixture) {
            Remove-Item -LiteralPath $receiptFixture -Recurse -Force
        }
    }

    Write-Host 'Portable consumer OS provenance fixtures passed.'
}
finally {
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
