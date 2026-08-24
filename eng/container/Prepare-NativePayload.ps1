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

function Get-LowerSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

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
[System.IO.Directory]::CreateDirectory($resolvedDownload) | Out-Null
$stagingRoot = Join-Path $resolvedDownload (
    '.sharpproof-native-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
try {
    # Each invocation gets an isolated archive and extraction tree. This keeps
    # a cancelled/partial download or concurrent extraction from becoming a
    # trusted input to another build.
    $archivePath = Join-Path $stagingRoot $archiveName
    $extractRoot = Join-Path $stagingRoot 'extract'
    $downloaded = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest `
                -Uri ([string]$z3.archiveUrl) `
                -OutFile $archivePath `
                -TimeoutSec 300
            $downloaded = $true
            break
        }
        catch {
            if (Test-Path -LiteralPath $archivePath) {
                Remove-Item -LiteralPath $archivePath -Force
            }
            if ($attempt -eq 3) {
                throw
            }
            Start-Sleep -Seconds ([Math]::Min($attempt, 5))
        }
    }
    if (-not $downloaded) {
        throw 'The native payload archive could not be downloaded.'
    }
    Assert-Equal (Get-LowerSha256 $archivePath) ([string]$z3.archiveSha256) 'Z3 archive SHA-256'
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
    Assert-Equal (Get-LowerSha256 $sourceLibrary) ([string]$z3.librarySha256) 'libz3.so SHA-256'
    Assert-Equal ([int64]$managed.Length) ([int64]$z3.managedAssemblyBytes) 'Microsoft.Z3.dll byte length'
    Assert-Equal (Get-LowerSha256 $sourceManaged) ([string]$z3.managedAssemblySha256) 'Microsoft.Z3.dll SHA-256'

    $payloadDirectory = Join-Path $resolvedDestination "z3/$($z3.version)/linux-x64"
    [System.IO.Directory]::CreateDirectory($payloadDirectory) | Out-Null
    Copy-Item -LiteralPath $sourceLibrary -Destination (Join-Path $payloadDirectory 'libz3.so') -Force
    Copy-Item -LiteralPath $sourceManaged -Destination (Join-Path $payloadDirectory 'Microsoft.Z3.dll') -Force

    $manifest = [ordered]@{
        schemaVersion = 1
        platform = [string]$catalog.platform
        version = [string]$z3.version
        sourceUrl = [string]$z3.archiveUrl
        archiveSha256 = [string]$z3.archiveSha256
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
    Write-Output $payloadDirectory
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
