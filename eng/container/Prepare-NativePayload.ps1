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

    $manifest = [ordered]@{
        schemaVersion = 1
        platform = [string]$catalog.platform
        version = [string]$z3.version
        sourceUrl = [string]$z3.archiveUrl
        files = @(
            [ordered]@{
                name = 'libz3.so'
                bytes = [int64]$z3.libraryBytes
                sha256 = [string]$z3.librarySha256
            },
            [ordered]@{
                name = 'Microsoft.Z3.dll'
                bytes = [int64]$z3.managedAssemblyBytes
                sha256 = [string]$z3.managedAssemblySha256
            }
        )
    }
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

$windows = $z3.windows
$windowsArchive = Join-Path $resolvedDownload "z3-$($z3.version)-x64-win.zip"
$windowsExtractRoot = Join-Path $resolvedDownload "z3-$($z3.version)-win-extract"
try {
    if (-not (Test-Path -LiteralPath $windowsArchive -PathType Leaf)) {
        Invoke-WebRequest -Uri ([string]$windows.archiveUrl) -OutFile $windowsArchive
    }
    Assert-Sha256 $windowsArchive ([string]$windows.archiveSha256) 'Windows Z3 archive'

    if (Test-Path -LiteralPath $windowsExtractRoot) {
        Remove-Item -LiteralPath $windowsExtractRoot -Recurse -Force
    }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($windowsArchive, $windowsExtractRoot)

    $windowsLibrary = Join-Path $windowsExtractRoot ([string]$windows.archiveLibraryPath)
    $windowsManaged = Join-Path $windowsExtractRoot ([string]$windows.archiveManagedAssemblyPath)
    if (-not (Test-Path -LiteralPath $windowsLibrary -PathType Leaf) -or
        -not (Test-Path -LiteralPath $windowsManaged -PathType Leaf)) {
        throw 'The verified Windows Z3 archive did not contain the catalog-owned payload paths.'
    }

    Assert-Equal ([int64](Get-Item -LiteralPath $windowsLibrary).Length) ([int64]$windows.libraryBytes) 'libz3.dll byte length'
    Assert-Equal ([int64](Get-Item -LiteralPath $windowsManaged).Length) ([int64]$windows.managedAssemblyBytes) 'Windows Microsoft.Z3.dll byte length'
    Assert-Sha256 $windowsLibrary ([string]$windows.librarySha256) 'libz3.dll'
    Assert-Sha256 $windowsManaged ([string]$windows.managedAssemblySha256) 'Windows Microsoft.Z3.dll'

    $windowsPayloadDirectory = Join-Path $resolvedDestination "z3/$($z3.version)/win-x64"
    [System.IO.Directory]::CreateDirectory($windowsPayloadDirectory) | Out-Null
    Copy-Item -LiteralPath $windowsLibrary -Destination (Join-Path $windowsPayloadDirectory 'libz3.dll') -Force
    Copy-Item -LiteralPath $windowsManaged -Destination (Join-Path $windowsPayloadDirectory 'Microsoft.Z3.dll') -Force

    $windowsManifest = [ordered]@{
        schemaVersion = 1
        platform = 'windows/amd64'
        version = [string]$z3.version
        sourceUrl = [string]$windows.archiveUrl
        files = @(
            [ordered]@{
                name = 'libz3.dll'
                bytes = [int64]$windows.libraryBytes
                sha256 = [string]$windows.librarySha256
            },
            [ordered]@{
                name = 'Microsoft.Z3.dll'
                bytes = [int64]$windows.managedAssemblyBytes
                sha256 = [string]$windows.managedAssemblySha256
            }
        )
    }
    $windowsManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $windowsPayloadDirectory 'payload.json') -Encoding utf8NoBOM
}
finally {
    foreach ($temporaryPath in @($windowsExtractRoot, $windowsArchive)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Recurse -Force
        }
    }
}

Write-Output $windowsPayloadDirectory
