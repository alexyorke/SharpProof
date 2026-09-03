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

$resolvedCatalog = [System.IO.Path]::GetFullPath($CatalogPath)
$resolvedDestination = [System.IO.Path]::GetFullPath($DestinationRoot)
$resolvedDownload = [System.IO.Path]::GetFullPath($DownloadRoot)
$catalog = Get-Content -LiteralPath $resolvedCatalog -Raw | ConvertFrom-Json

if ([int]$catalog.schemaVersion -ne 1 -or [string]$catalog.platform -cne 'linux/amd64') {
    throw 'The native payload preparer requires container toolchain schema 1 for linux/amd64.'
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
            },
            [ordered]@{
                name = 'Microsoft.Z3.dll'
                bytes = [int64]$z3.managedAssemblyBytes
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
