[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CatalogPath,

    [Parameter(Mandatory = $true)]
    [string]$DestinationRoot,

    [string]$DownloadRoot = ([System.IO.Path]::GetTempPath())
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Equal([object]$Actual, [object]$Expected, [string]$Label) {
    if ($Actual -cne $Expected) {
        throw "$Label mismatch. Expected '$Expected', found '$Actual'."
    }
}

function Assert-Sha256([string]$Path, [string]$Expected, [string]$Label) {
    if ($Expected -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Label catalog digest is invalid."
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Equal $actual $Expected "$Label SHA-256"
}

$resolvedCatalog = [System.IO.Path]::GetFullPath($CatalogPath)
$resolvedDestination = [System.IO.Path]::GetFullPath($DestinationRoot)
$resolvedDownload = [System.IO.Path]::GetFullPath($DownloadRoot)
$catalog = Get-Content -LiteralPath $resolvedCatalog -Raw | ConvertFrom-Json

if ([int]$catalog.schemaVersion -ne 2 -or [string]$catalog.platform -cne 'linux/amd64') {
    throw 'The native payload preparer requires container toolchain schema 2 for linux/amd64.'
}

$z3 = $catalog.z3
$archiveName = "z3-$($z3.version)-x64-glibc.zip"
$archivePath = Join-Path $resolvedDownload $archiveName
$extractRoot = Join-Path $resolvedDownload "z3-$($z3.version)-extract"

[System.IO.Directory]::CreateDirectory($resolvedDownload) | Out-Null
try {
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        Invoke-WebRequest -Uri ([string]$z3.archiveUrl) -OutFile $archivePath
    }
    Assert-Sha256 $archivePath ([string]$z3.archiveSha256) 'Z3 archive'

    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractRoot)

    $sourceLibrary = Join-Path $extractRoot ([string]$z3.archiveLibraryPath)
    $sourceManaged = Join-Path $extractRoot ([string]$z3.archiveManagedAssemblyPath)
    if (-not (Test-Path -LiteralPath $sourceLibrary -PathType Leaf) -or
        -not (Test-Path -LiteralPath $sourceManaged -PathType Leaf)) {
        throw 'The verified Z3 archive did not contain the catalog-owned payload paths.'
    }

    $library = Get-Item -LiteralPath $sourceLibrary
    $managed = Get-Item -LiteralPath $sourceManaged
    Assert-Equal ([int64]$library.Length) ([int64]$z3.libraryBytes) 'libz3.so byte length'
    Assert-Equal ([int64]$managed.Length) ([int64]$z3.managedAssemblyBytes) 'Microsoft.Z3.dll byte length'
    Assert-Sha256 $sourceLibrary ([string]$z3.librarySha256) 'libz3.so'
    Assert-Sha256 $sourceManaged ([string]$z3.managedAssemblySha256) 'Microsoft.Z3.dll'

    $payloadDirectory = Join-Path $resolvedDestination "z3/$($z3.version)/linux-x64"
    [System.IO.Directory]::CreateDirectory($payloadDirectory) | Out-Null
    Copy-Item -LiteralPath $sourceLibrary -Destination (Join-Path $payloadDirectory 'libz3.so') -Force
    Copy-Item -LiteralPath $sourceManaged -Destination (Join-Path $payloadDirectory 'Microsoft.Z3.dll') -Force

    $manifest = ([ordered]@{
            schemaVersion = 1
            platform      = [string]$catalog.platform
            version       = [string]$z3.version
            sourceUrl     = [string]$z3.archiveUrl
            files         = @(
                ([ordered]@{
                    name   = 'libz3.so'
                    bytes  = [int64]$z3.libraryBytes
                    sha256 = [string]$z3.librarySha256
                }).AsReadOnly(),
                ([ordered]@{
                    name   = 'Microsoft.Z3.dll'
                    bytes  = [int64]$z3.managedAssemblyBytes
                    sha256 = [string]$z3.managedAssemblySha256
                }).AsReadOnly()
            )
        }).AsReadOnly()
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payloadDirectory 'payload.json') -Encoding utf8NoBOM
}
finally {
    foreach ($temporaryPath in @($extractRoot, $archivePath)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Recurse -Force
        }
    }
}

Write-Output $payloadDirectory

$linuxArm64 = $z3.linuxArm64
$linuxArm64Archive = Join-Path $resolvedDownload "z3-$($z3.version)-linux-arm64.whl"
$linuxArm64StagingLibrary = Join-Path $resolvedDownload "z3-$($z3.version)-linux-arm64-libz3.so"
try {
    if (-not (Test-Path -LiteralPath $linuxArm64Archive -PathType Leaf)) {
        Invoke-WebRequest -Uri ([string]$linuxArm64.archiveUrl) -OutFile $linuxArm64Archive
    }
    Assert-Sha256 $linuxArm64Archive ([string]$linuxArm64.archiveSha256) 'Linux ARM64 Z3 wheel'
    $linuxArm64Zip = [System.IO.Compression.ZipFile]::OpenRead($linuxArm64Archive)
    try {
        # The official wheel also contains an older libz3.so.4.10. Extract only
        # the exact catalog-owned entry, never a wildcard or a directory tree.
        $linuxArm64Entry = $linuxArm64Zip.GetEntry([string]$linuxArm64.archiveLibraryPath)
        if ($null -eq $linuxArm64Entry) {
            throw 'The verified Linux ARM64 Z3 wheel is missing its catalog-owned native entry.'
        }
        Assert-Equal ([int64]$linuxArm64Entry.Length) ([int64]$linuxArm64.libraryBytes) 'Linux ARM64 libz3.so byte length'
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($linuxArm64Entry, $linuxArm64StagingLibrary, $true)
    }
    finally { $linuxArm64Zip.Dispose() }
    Assert-Sha256 $linuxArm64StagingLibrary ([string]$linuxArm64.librarySha256) 'Linux ARM64 libz3.so'
    $linuxArm64PayloadDirectory = Join-Path $resolvedDestination "z3/$($z3.version)/linux-arm64"
    [System.IO.Directory]::CreateDirectory($linuxArm64PayloadDirectory) | Out-Null
    Copy-Item -LiteralPath $linuxArm64StagingLibrary -Destination (Join-Path $linuxArm64PayloadDirectory 'libz3.so') -Force
    ([ordered]@{
        schemaVersion     = 1
        platform          = 'linux/arm64'
        version           = [string]$z3.version
        sourceUrl         = [string]$linuxArm64.archiveUrl
        sourceMetadataUrl = [string]$linuxArm64.sourceMetadataUrl
        files             = @(([ordered]@{
                    name   = 'libz3.so'
                    bytes  = [int64]$linuxArm64.libraryBytes
                    sha256 = [string]$linuxArm64.librarySha256
                }).AsReadOnly())
    }).AsReadOnly() | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $linuxArm64PayloadDirectory 'payload.json') -Encoding utf8NoBOM
}
finally {
    foreach ($temporaryPath in @($linuxArm64StagingLibrary, $linuxArm64Archive)) {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}
Write-Output $linuxArm64PayloadDirectory

# Windows native DLLs are built and pinned in the Docker cross-build stage.
# Consume only that verified closure; never download the upstream VC-dependent DLL.
$windowsBuildRoot = '/opt/sharpproof/windows-native'
foreach ($architecture in @('x64', 'arm64')) {
    $rid = "win-$architecture"
    $windows = if ($architecture -ceq 'x64') { $z3.windows } else { $z3.windowsArm64 }
    $sourceDirectory = Join-Path $windowsBuildRoot $rid
    $windowsLibrary = Join-Path $sourceDirectory 'libz3.dll'
    $receiptPath = Join-Path $sourceDirectory 'payload-receipt.json'
    if (-not (Test-Path -LiteralPath $windowsLibrary -PathType Leaf) -or
        -not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        throw "The pinned Windows cross-build payload is missing: $rid"
    }
    Assert-Equal ([int64](Get-Item -LiteralPath $windowsLibrary).Length) ([int64]$windows.libraryBytes) "$rid libz3.dll byte length"
    Assert-Sha256 $windowsLibrary ([string]$windows.librarySha256) "$rid libz3.dll"
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    Assert-Equal ([string]$receipt.rid) $rid "$rid receipt RID"
    Assert-Equal ([int64]$receipt.libraryBytes) ([int64]$windows.libraryBytes) "$rid receipt native byte length"
    Assert-Equal ([string]$receipt.librarySha256) ([string]$windows.librarySha256) "$rid receipt native digest"
    Assert-Equal ([string]$receipt.sourceArchiveSha256) ([string]$z3.windowsCrossBuild.sourceArchiveSha256) "$rid receipt source digest"
    Assert-Equal ([string]$receipt.compilerArchiveSha256) ([string]$z3.windowsCrossBuild.compilerArchiveSha256) "$rid receipt compiler digest"
    Assert-Equal ([string]$receipt.sourceCommit) ([string]$z3.windowsCrossBuild.sourceCommit) "$rid receipt source commit"
    Assert-Equal ([bool]$receipt.sourceModified) $false "$rid source modification status"
    Assert-Equal ([int64]$receipt.sourceDateEpoch) ([int64]$z3.windowsCrossBuild.sourceDateEpoch) "$rid reproducibility epoch"
    Assert-Equal ([int64]$receipt.peTimestamp) ([int64]0) "$rid deterministic PE timestamp"
    $expectedMachine = if ($architecture -ceq 'x64') { '0x8664' } else { '0xaa64' }
    Assert-Equal ([string]$receipt.peMachine) $expectedMachine "$rid PE machine"
    $windowsPayloadDirectory = Join-Path $resolvedDestination "z3/$($z3.version)/$rid"
    [System.IO.Directory]::CreateDirectory($windowsPayloadDirectory) | Out-Null
    Copy-Item -LiteralPath $windowsLibrary -Destination (Join-Path $windowsPayloadDirectory 'libz3.dll') -Force
    Copy-Item -LiteralPath $receiptPath -Destination (Join-Path $windowsPayloadDirectory 'payload.json') -Force
    # Both architectures use the canonical managed assembly already verified above.
    Copy-Item -LiteralPath (Join-Path $resolvedDestination "z3/$($z3.version)/linux-x64/Microsoft.Z3.dll") -Destination (Join-Path $windowsPayloadDirectory 'Microsoft.Z3.dll') -Force
    Write-Output $windowsPayloadDirectory
}

foreach ($architecture in @('arm64', 'x64')) {
    $mac = $catalog.macos.$architecture
    $rid = "osx-$architecture"
    $archivePath = Join-Path $resolvedDownload "z3-$($z3.version)-$rid.zip"
    $extractRoot = Join-Path $resolvedDownload "z3-$($z3.version)-$rid-extract"
    try {
        if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
            Invoke-WebRequest -Uri ([string]$mac.archiveUrl) -OutFile $archivePath
        }
        Assert-Sha256 $archivePath ([string]$mac.archiveSha256) "$rid Z3 archive"
        if (Test-Path -LiteralPath $extractRoot) {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force
        }
        [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractRoot)
        $sourceLibrary = Join-Path $extractRoot ([string]$mac.archiveLibraryPath)
        $sourceManaged = Join-Path $extractRoot ([string]$mac.archiveManagedAssemblyPath)
        if (-not (Test-Path -LiteralPath $sourceLibrary -PathType Leaf) -or
            -not (Test-Path -LiteralPath $sourceManaged -PathType Leaf)) {
            throw "The verified $rid Z3 archive did not contain the catalog-owned payload paths."
        }
        Assert-Equal ([int64](Get-Item -LiteralPath $sourceLibrary).Length) ([int64]$mac.libraryBytes) "$rid libz3.dylib byte length"
        Assert-Equal ([int64](Get-Item -LiteralPath $sourceManaged).Length) ([int64]$mac.managedAssemblyBytes) "$rid Microsoft.Z3.dll byte length"
        Assert-Sha256 $sourceLibrary ([string]$mac.librarySha256) "$rid libz3.dylib"
        Assert-Sha256 $sourceManaged ([string]$mac.managedAssemblySha256) "$rid Microsoft.Z3.dll"
        $destination = Join-Path $resolvedDestination "z3/$($z3.version)/$rid"
        [System.IO.Directory]::CreateDirectory($destination) | Out-Null
        Copy-Item -LiteralPath $sourceLibrary -Destination (Join-Path $destination 'libz3.dylib') -Force
        Copy-Item -LiteralPath $sourceManaged -Destination (Join-Path $destination 'Microsoft.Z3.dll') -Force
    }
    finally {
        foreach ($temporaryPath in @($extractRoot, $archivePath)) {
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -LiteralPath $temporaryPath -Recurse -Force
            }
        }
    }
    Write-Output $destination
}
