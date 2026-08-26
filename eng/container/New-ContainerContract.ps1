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

function Invoke-NativeText {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Arguments)

    $lines = @(& $Name @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "The required tool '$Name' failed with exit code $LASTEXITCODE."
    }
    return @($lines | ForEach-Object { [string]$_ })
}

function Assert-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Actual,
        [Parameter(Mandatory = $true)][string]$Expected)

    if ($Actual -cne $Expected) {
        throw "$Name is '$Actual', but the catalog requires '$Expected'."
    }
}

$expectedDotnet = $catalog.dotnet
$expectedPowershell = $catalog.powershell
$dotnetSdkVersionOutput = Invoke-NativeText `
    -Name 'dotnet' `
    -Arguments @('--version')
$dotnetSdkVersion = ($dotnetSdkVersionOutput -join '').Trim()
Assert-Exact 'Installed .NET SDK version' $dotnetSdkVersion `
    ([string]$expectedDotnet.sdkVersion)

$sdkLines = Invoke-NativeText dotnet @('--list-sdks')
$minimumSdkLine = "{0} [/usr/share/dotnet/sdk]" -f `
    ([string]$expectedDotnet.minimumSdkVersion)
if ($sdkLines -notcontains $minimumSdkLine) {
    throw "The catalog minimum .NET SDK is not installed: $minimumSdkLine"
}

$runtimeLines = Invoke-NativeText dotnet @('--list-runtimes')
$testRuntimeLine = "Microsoft.NETCore.App {0} [/usr/share/dotnet/shared/Microsoft.NETCore.App]" -f `
    ([string]$expectedDotnet.testRuntimeVersion)
if ($runtimeLines -notcontains $testRuntimeLine) {
    throw "The catalog .NET test runtime is not installed: $testRuntimeLine"
}

$frameworkVersion = [string]$expectedDotnet.minimumSdkFrameworkVersion
foreach ($pack in @(
        'Microsoft.NETCore.App.Ref',
        'Microsoft.AspNetCore.App.Ref',
        'Microsoft.NETCore.App.Host.linux-x64')) {
    $packPath = "/usr/share/dotnet/packs/$pack/$frameworkVersion"
    if (-not [System.IO.Directory]::Exists($packPath)) {
        throw "The catalog framework pack is not installed: $packPath"
    }
}

$powershellVersion = $PSVersionTable.PSVersion
$powershellVersionLine = "{0}.{1}" -f `
    $powershellVersion.Major, $powershellVersion.Minor
Assert-Exact 'Installed PowerShell version line' $powershellVersionLine `
    ([string]$expectedPowershell.versionLine)

$contract = [ordered]@{
    schemaVersion = 1
    contractVersion = [int]$catalog.containerContractVersion
    platform = [string]$catalog.platform
    dotnetSdkVersion = $dotnetSdkVersion
    dotnetMinimumSdkVersion = [string]$catalog.dotnet.minimumSdkVersion
    dotnetMinimumSdkFrameworkVersion =
        [string]$catalog.dotnet.minimumSdkFrameworkVersion
    dotnetTestRuntimeVersion = [string]$expectedDotnet.testRuntimeVersion
    dotnetBaseImage = [string]$catalog.dotnet.baseImage
    dotnetBaseImageDigest = [string]$catalog.dotnet.baseImageDigest
    powershellVersionLine = $powershellVersionLine
    powershellImageDigest = [string]$catalog.powershell.imageDigest
    z3Version = [string]$catalog.z3.version
    z3LibraryBytes = [int64]$catalog.z3.libraryBytes
    z3LibrarySha256 = [string]$catalog.z3.librarySha256
    verifierPackageId = [string]$catalog.support.verifierPackageId
}

$parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
[System.IO.Directory]::CreateDirectory($parent) | Out-Null
$contract | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
