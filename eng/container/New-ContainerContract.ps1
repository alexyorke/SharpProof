[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CatalogPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
$contract = [ordered]@{
    schemaVersion = 1
    contractVersion = [int]$catalog.containerContractVersion
    platform = [string]$catalog.platform
    dotnetSdkVersion = [string]$catalog.dotnet.sdkVersion
    dotnetTestRuntimeVersion = [string]$catalog.dotnet.testRuntimeVersion
    dotnetBaseImage = [string]$catalog.dotnet.baseImage
    dotnetBaseImageDigest = [string]$catalog.dotnet.baseImageDigest
    powershellVersionLine = [string]$catalog.powershell.versionLine
    powershellImageDigest = [string]$catalog.powershell.imageDigest
    z3Version = [string]$catalog.z3.version
    z3LibraryBytes = [int64]$catalog.z3.libraryBytes
    z3LibrarySha256 = [string]$catalog.z3.librarySha256
    verifierPackageId = [string]$catalog.support.verifierPackageId
}

$parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
[System.IO.Directory]::CreateDirectory($parent) | Out-Null
$contract | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
