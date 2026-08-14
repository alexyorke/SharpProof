[CmdletBinding()]
param(
    [string]$DockerfilePath,
    [string]$ComposePath,
    [switch]$AuthorityOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($DockerfilePath)) {
    $DockerfilePath = Join-Path $repositoryRoot 'eng/container/Dockerfile'
}
if ([string]::IsNullOrWhiteSpace($ComposePath)) {
    $ComposePath = Join-Path $repositoryRoot 'compose.yaml'
}
$catalog = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/container/toolchain.json') -Raw |
    ConvertFrom-Json
$acceptance = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/acceptance/contract.json') -Raw |
    ConvertFrom-Json
$globalJson = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'global.json') -Raw |
    ConvertFrom-Json
$dockerfile = Get-Content -LiteralPath $DockerfilePath -Raw
$compose = Get-Content -LiteralPath $ComposePath -Raw
$devContainer = Get-Content -LiteralPath (
    Join-Path $repositoryRoot '.devcontainer/devcontainer.json') -Raw |
    ConvertFrom-Json
$devInitializer = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/container/dev-init.sh') -Raw
$directoryBuildTargets = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'Directory.Build.targets') -Raw
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

function Assert-SingleMatchingLine {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Name)

    $matches = @($Lines | Where-Object { $_ -cmatch $Pattern })
    if ($matches.Count -cne 1 -or $matches[0] -cne $Expected) {
        throw "$Name must occur exactly once as '$Expected'."
    }
}

function Get-DockerfileStageLines {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$FromLine)

    $start = [array]::IndexOf($Lines, $FromLine)
    if ($start -lt 0) {
        throw "Dockerfile stage '$FromLine' was not found."
    }
    $end = $Lines.Count
    for ($index = $start + 1; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -cmatch '^FROM\s+') {
            $end = $index
            break
        }
    }
    return @($Lines[($start + 1)..($end - 1)])
}

function Assert-DockerfileAuthority {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)]$ToolchainCatalog)

    $lines = @($Content -split "`r?`n")
    if (@($lines | Where-Object { $_ -cmatch '^\s*#\s*syntax\s*=' }).Count -ne 0) {
        throw (
            'The Dockerfile must use the bundled Dockerfile grammar and must not ' +
            'introduce an unpinned external frontend.')
    }

    $authorities = @(
        [pscustomobject]@{
            Argument = 'POWERSHELL_IMAGE'
            Image = "$($ToolchainCatalog.powershell.image)@$($ToolchainCatalog.powershell.imageDigest)"
            Stage = 'powershell'
        },
        [pscustomobject]@{
            Argument = 'DOTNET_TEST_RUNTIME_IMAGE'
            Image = "$($ToolchainCatalog.dotnet.testRuntimeImage)@$($ToolchainCatalog.dotnet.testRuntimeImageDigest)"
            Stage = 'test-runtime'
        },
        [pscustomobject]@{
            Argument = 'DOTNET_MINIMUM_SDK_IMAGE'
            Image = "$($ToolchainCatalog.dotnet.minimumSdkImage)@$($ToolchainCatalog.dotnet.minimumSdkImageDigest)"
            Stage = 'minimum-sdk'
        },
        [pscustomobject]@{
            Argument = 'DOTNET_MINIMUM_FRAMEWORK_IMAGE'
            Image = "$($ToolchainCatalog.dotnet.minimumSdkFrameworkImage)@$($ToolchainCatalog.dotnet.minimumSdkFrameworkImageDigest)"
            Stage = 'minimum-framework'
        },
        [pscustomobject]@{
            Argument = 'DOTNET_SDK_IMAGE'
            Image = "$($ToolchainCatalog.dotnet.baseImage)@$($ToolchainCatalog.dotnet.baseImageDigest)"
            Stage = 'toolchain'
        })

    $firstFrom = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -cmatch '^FROM\s+') {
            $firstFrom = $index
            break
        }
    }
    if ($firstFrom -lt 0) {
        throw 'The Dockerfile must contain the canonical build stages.'
    }

    foreach ($authority in $authorities) {
        $argumentPattern = '^ARG\s+' + [regex]::Escape($authority.Argument) + '(?:=|$)'
        $declarations = @()
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -cmatch $argumentPattern) {
                $declarations += [pscustomobject]@{
                    Index = $index
                    Text = $lines[$index]
                }
            }
        }
        $expected = "ARG $($authority.Argument)=$($authority.Image)"
        if ($declarations.Count -cne 1 -or
            $declarations[0].Index -ge $firstFrom -or
            $declarations[0].Text -cne $expected) {
            throw (
                "Dockerfile authority $($authority.Argument) must be declared " +
                "exactly once globally as '$expected'.")
        }
    }

    $actualStages = @($lines | Where-Object { $_ -cmatch '^FROM\s+' })
    $expectedStages = @(
        'FROM ${POWERSHELL_IMAGE} AS powershell',
        'FROM ${DOTNET_TEST_RUNTIME_IMAGE} AS test-runtime',
        'FROM ${DOTNET_MINIMUM_SDK_IMAGE} AS minimum-sdk',
        'FROM ${DOTNET_MINIMUM_FRAMEWORK_IMAGE} AS minimum-framework',
        'FROM ${DOTNET_SDK_IMAGE} AS toolchain',
        'FROM toolchain AS dev',
        'FROM toolchain AS build',
        'FROM build AS test',
        'FROM build AS package')
    if ($actualStages.Count -cne $expectedStages.Count) {
        throw 'The Dockerfile must contain exactly the canonical build stages.'
    }
    for ($index = 0; $index -lt $expectedStages.Count; $index++) {
        if ($actualStages[$index] -cne $expectedStages[$index]) {
            throw (
                "Dockerfile stage $index must be '$($expectedStages[$index])'; " +
                "found '$($actualStages[$index])'.")
        }
    }


    $toolchainLines = Get-DockerfileStageLines `
        -Lines $lines `
        -FromLine 'FROM ${DOTNET_SDK_IMAGE} AS toolchain'
    $toolchainText = $toolchainLines -join "`n"
    foreach ($required in @(
            'ARG USER_UID=1000',
            'ARG USER_GID=1000',
            'useradd --uid "${USER_UID}"',
            '/home/sharpproof/.local/share/NuGet',
            '/home/sharpproof/.nuget/packages',
            '/home/sharpproof/.dotnet')) {
        if (-not $toolchainText.Contains(
                $required,
                [StringComparison]::Ordinal)) {
            throw "Dockerfile toolchain stage is missing '$required'."
        }
    }

    $stageContracts = @(
        [pscustomobject]@{
            From = 'FROM toolchain AS dev'
            Root = '/workspace/SharpProof'
            Command = 'dev'
        },
        [pscustomobject]@{
            From = 'FROM toolchain AS build'
            Root = '/src'
            Command = 'build'
        },
        [pscustomobject]@{
            From = 'FROM build AS test'
            Root = '/src'
            Command = 'portable-tests'
        },
        [pscustomobject]@{
            From = 'FROM build AS package'
            Root = '/src'
            Command = 'pack'
        })
    foreach ($stage in $stageContracts) {
        $stageLines = Get-DockerfileStageLines `
            -Lines $lines `
            -FromLine $stage.From
        Assert-SingleMatchingLine `
            $stageLines `
            '^ENV SHARPPROOF_REPO_ROOT=' `
            "ENV SHARPPROOF_REPO_ROOT=$($stage.Root)" `
            "$($stage.Command) repository root"
        Assert-SingleMatchingLine `
            $stageLines `
            '^WORKDIR ' `
            "WORKDIR $($stage.Root)" `
            "$($stage.Command) working directory"
        Assert-SingleMatchingLine `
            $stageLines `
            '^USER ' `
            'USER sharpproof' `
            "$($stage.Command) user"
        Assert-SingleMatchingLine `
            $stageLines `
            '^ENTRYPOINT ' `
            'ENTRYPOINT ["/usr/local/bin/sharpproof-container"]' `
            "$($stage.Command) entrypoint"
        Assert-SingleMatchingLine `
            $stageLines `
            '^CMD ' `
            "CMD [`"$($stage.Command)`"]" `
            "$($stage.Command) default command"
    }
    $buildLines = Get-DockerfileStageLines `
        -Lines $lines `
        -FromLine 'FROM toolchain AS build'
    Assert-SingleMatchingLine `
        $buildLines `
        '^COPY .+ \. \.$' `
        'COPY --chown=sharpproof:sharpproof . .' `
        'Build source ownership'
}

function Assert-ComposeAuthority {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Platform)

    $lines = @($Content -split "`r?`n")
    if ($Content.Contains("`t", [System.StringComparison]::Ordinal)) {
        throw 'Compose authority must use spaces, not tabs.'
    }
    Assert-SingleMatchingLine `
        -Lines $lines `
        -Pattern '^x-sharpproof-common:' `
        -Expected 'x-sharpproof-common: &sharpproof-common' `
        -Name 'Compose common authority'
    Assert-SingleMatchingLine `
        -Lines $lines `
        -Pattern '^services:' `
        -Expected 'services:' `
        -Name 'Compose services mapping'

    $commonStart = [array]::IndexOf(
        $lines,
        'x-sharpproof-common: &sharpproof-common')
    $servicesStart = [array]::IndexOf($lines, 'services:')
    if ($commonStart -ge $servicesStart) {
        throw 'Compose common authority must precede the services mapping.'
    }
    $commonLines = @($lines[($commonStart + 1)..($servicesStart - 1)])
    $expectedImage = '  image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}'
    Assert-SingleMatchingLine $commonLines '^  image:' $expectedImage 'Compose tooling image'
    Assert-SingleMatchingLine $commonLines '^  platform:' "  platform: $Platform" 'Compose platform'
    Assert-SingleMatchingLine `
        $commonLines `
        '^    SHARPPROOF_REPO_ROOT:' `
        '    SHARPPROOF_REPO_ROOT: /workspace/SharpProof' `
        'Compose repository root'
    Assert-SingleMatchingLine $commonLines '^  build:' '  build:' 'Compose build mapping'

    $buildStart = [array]::IndexOf($commonLines, '  build:')
    $buildEnd = $commonLines.Count
    for ($index = $buildStart + 1; $index -lt $commonLines.Count; $index++) {
        if ($commonLines[$index] -cmatch '^  \S') {
            $buildEnd = $index
            break
        }
    }
    $buildLines = @($commonLines[($buildStart + 1)..($buildEnd - 1)])
    Assert-SingleMatchingLine $buildLines '^    context:' '    context: .' 'Compose build context'
    Assert-SingleMatchingLine `
        $buildLines `
        '^    dockerfile:' `
        '    dockerfile: eng/container/Dockerfile' `
        'Compose Dockerfile'
    Assert-SingleMatchingLine $buildLines '^    target:' '    target: dev' 'Compose build target'

    $serviceNames = @()
    for ($index = $servicesStart + 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -cmatch '^\S') {
            break
        }
        if ($lines[$index] -cmatch '^  ([a-z0-9-]+):\s*$') {
            $serviceNames += $Matches[1]
        }
    }
    if ($serviceNames -cnotcontains 'tooling') {
        throw 'Compose must define the canonical tooling service.'
    }
    foreach ($serviceName in $serviceNames) {
        $header = "  ${serviceName}:"
        $serviceStart = -1
        for ($index = $servicesStart + 1; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -ceq $header) {
                $serviceStart = $index
                break
            }
        }
        if ($serviceStart -lt 0) {
            throw "Compose service '$serviceName' could not be resolved."
        }
        $serviceEnd = $lines.Count
        for ($index = $serviceStart + 1; $index -lt $lines.Count; $index++) {
            if ($lines[$index] -cmatch '^\S' -or
                $lines[$index] -cmatch '^  [a-z0-9-]+:\s*$') {
                $serviceEnd = $index
                break
            }
        }
        $serviceLines = @($lines[($serviceStart + 1)..($serviceEnd - 1)])
        Assert-SingleMatchingLine `
            $serviceLines `
            '^    <<:' `
            '    <<: *sharpproof-common' `
            "Compose service '$serviceName' authority"
        if (@($serviceLines | Where-Object {
                    $_ -cmatch '^    (?:image|build|platform):'
                }).Count -ne 0) {
            throw "Compose service '$serviceName' overrides canonical image authority."
        }
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

Assert-DockerfileAuthority -Content $dockerfile -ToolchainCatalog $catalog
Assert-ComposeAuthority -Content $compose -Platform ([string]$catalog.platform)
if ($AuthorityOnly) {
    Write-Host 'SharpProof container authority validation passed.'
    return
}
if ($compose -cmatch '/workspace/seed|SHARPPROOF_SEED_ROOT') {
    throw 'The Dev Container must not depend on a host seed checkout.'
}
if ($devContainer.PSObject.Properties.Name -contains 'initializeCommand') {
    throw 'Dev Container initialization must not invoke host tooling.'
}
Assert-Exact `
    $devContainer.postCreateCommand `
    'sharpproof-dev-init' `
    'Dev Container initializer'
Assert-Exact `
    $devContainer.postStartCommand `
    'sp contract' `
    'Dev Container startup validation'
if ($devInitializer -cnotmatch 'SHARPPROOF_ORIGIN_URL' -or
    $devInitializer -cnotmatch 'SHARPPROOF_DEV_REF' -or
    $devInitializer -cnotmatch 'git "\$\{clone_arguments\[@\]\}"') {
    throw 'The Dev Container must clone its checkout entirely in-container.'
}
if ($devInitializer -cmatch 'git bundle|repository\.bundle|SHARPPROOF_SEED_ROOT') {
    throw 'The Dev Container initializer retains a host Git bootstrap.'
}
if ($directoryBuildTargets -cnotmatch '_RequireSharpProofCanonicalContainer' -or
    $directoryBuildTargets -cnotmatch 'SHARPPROOF_CONTAINER' -or
    $directoryBuildTargets -cnotmatch '/etc/sharpproof/container-contract\.json') {
    throw 'Repository MSBuild entry points must reject host execution.'
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
        $marker.dotnetMinimumSdkVersion `
        $catalog.dotnet.minimumSdkVersion `
        'Installed minimum .NET SDK version'
    Assert-Exact `
        $marker.dotnetMinimumSdkFrameworkVersion `
        $catalog.dotnet.minimumSdkFrameworkVersion `
        'Installed minimum-SDK framework version'
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
    $installedSdks = & dotnet --list-sdks
    if ($installedSdks -notcontains
        "$($catalog.dotnet.minimumSdkVersion) [/usr/share/dotnet/sdk]") {
        throw 'The pinned minimum .NET SDK is not installed in the container.'
    }
    foreach ($pack in @(
            'Microsoft.NETCore.App.Ref',
            'Microsoft.AspNetCore.App.Ref',
            'Microsoft.NETCore.App.Host.linux-x64')) {
        $packPath = Join-Path `
            "/usr/share/dotnet/packs/$pack" `
            ([string]$catalog.dotnet.minimumSdkFrameworkVersion)
        if (-not (Test-Path -LiteralPath $packPath -PathType Container)) {
            throw "The minimum-SDK framework pack is missing: $packPath"
        }
    }
}

Write-Host 'SharpProof container contract validation passed.'
