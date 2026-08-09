[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$catalog = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/container/toolchain.json') -Raw |
    ConvertFrom-Json
$acceptance = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/acceptance/contract.json') -Raw |
    ConvertFrom-Json
$globalJson = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'global.json') -Raw |
    ConvertFrom-Json
$dockerfile = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/container/Dockerfile') -Raw
$compose = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'compose.yaml') -Raw
$packages = [xml](Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'Directory.Packages.props') -Raw)
$packageProjects = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'scripts/package-projects.json') -Raw |
    ConvertFrom-Json

function Assert-Exact {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Name)

    if ([string]$Actual -cne [string]$Expected) {
        throw "$Name must be '$Expected'; found '$Actual'."
    }
}

Assert-Exact $catalog.schemaVersion 1 'Container toolchain schema'
Assert-Exact $catalog.platform 'linux/amd64' 'Container platform'
Assert-Exact $globalJson.sdk.version $catalog.dotnet.sdkVersion '.NET SDK version'
Assert-Exact $globalJson.sdk.rollForward 'disable' '.NET SDK roll-forward policy'
Assert-Exact `
    $acceptance.container.contractVersion `
    $catalog.containerContractVersion `
    'Container contract version'
Assert-Exact `
    $acceptance.container.platform `
    $catalog.platform `
    'Acceptance container platform'

$dotnetImage = "$($catalog.dotnet.baseImage)@$($catalog.dotnet.baseImageDigest)"
$dotnetTestRuntimeImage =
    "$($catalog.dotnet.testRuntimeImage)@$($catalog.dotnet.testRuntimeImageDigest)"
$powershellImage = "$($catalog.powershell.image)@$($catalog.powershell.imageDigest)"
if ($dockerfile -cnotmatch [regex]::Escape(
        "ARG DOTNET_SDK_IMAGE=$dotnetImage")) {
    throw 'The Dockerfile .NET SDK base does not match the toolchain catalog.'
}
if ($dockerfile -cnotmatch [regex]::Escape(
        "ARG DOTNET_TEST_RUNTIME_IMAGE=$dotnetTestRuntimeImage")) {
    throw 'The Dockerfile .NET 8 test runtime does not match the toolchain catalog.'
}
if ($dockerfile -cnotmatch [regex]::Escape(
        "ARG POWERSHELL_IMAGE=$powershellImage")) {
    throw 'The Dockerfile PowerShell base does not match the toolchain catalog.'
}
if ($compose -cnotmatch '(?m)^\s*platform:\s*linux/amd64\s*$') {
    throw 'Compose must pin linux/amd64.'
}
if ($compose -cnotmatch [regex]::Escape(
        "cpus: `${SHARPPROOF_CONTAINER_CPU_LIMIT:-$($acceptance.container.defaultCpuCount)}")) {
    throw 'Compose CPU defaults do not match the acceptance contract.'
}
$memoryGiB = [int]$acceptance.container.defaultMemoryMiB / 1024
if ($compose -cnotmatch [regex]::Escape(
        "mem_limit: `${SHARPPROOF_CONTAINER_MEMORY_LIMIT:-$($memoryGiB)g}")) {
    throw 'Compose memory defaults do not match the acceptance contract.'
}

$z3Package = $packages.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -ceq 'Microsoft.Z3' }
if ($null -eq $z3Package) {
    throw 'Directory.Packages.props must pin Microsoft.Z3.'
}
Assert-Exact $z3Package.Version $catalog.z3.version 'Microsoft.Z3 version'
Assert-Exact `
    $packageProjects.projects[-1] `
    'SharpProof.Verifier/SharpProof.Verifier.csproj' `
    'Verifier package project'
Assert-Exact `
    $catalog.support.verifierPackageId `
    'SharpProof.Verifier' `
    'Verifier package ID'

if ($IsLinux -and $env:SHARPPROOF_CONTAINER -ceq '1') {
    $markerPath = if ([string]::IsNullOrWhiteSpace(
            $env:SHARPPROOF_CONTAINER_CONTRACT)) {
        '/etc/sharpproof/container-contract.json'
    } else {
        $env:SHARPPROOF_CONTAINER_CONTRACT
    }
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    Assert-Exact `
        $marker.contractVersion `
        $catalog.containerContractVersion `
        'Installed container contract version'
    Assert-Exact $marker.platform $catalog.platform 'Installed container platform'
    Assert-Exact `
        $marker.dotnetTestRuntimeVersion `
        $catalog.dotnet.testRuntimeVersion `
        'Installed .NET test runtime version'
    Assert-Exact `
        $marker.z3LibrarySha256 `
        $catalog.z3.librarySha256 `
        'Installed Z3 hash declaration'

    $native = Join-Path `
        ($env:SHARPPROOF_NATIVE_ROOT ?? '/opt/sharpproof/native') `
        "z3/$($catalog.z3.version)/linux-x64/libz3.so"
    $information = Get-Item -LiteralPath $native
    Assert-Exact $information.Length $catalog.z3.libraryBytes 'Installed Z3 size'
    Assert-Exact `
        (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash.ToLowerInvariant() `
        $catalog.z3.librarySha256 `
        'Installed Z3 hash'
    $installedRuntimes = & dotnet --list-runtimes
    if ($installedRuntimes -notcontains
        "Microsoft.NETCore.App $($catalog.dotnet.testRuntimeVersion) [/usr/share/dotnet/shared/Microsoft.NETCore.App]") {
        throw 'The pinned .NET test runtime is not installed in the container.'
    }
}

Write-Host 'SharpProof container contract validation passed.'
