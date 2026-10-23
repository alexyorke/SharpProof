[CmdletBinding()]
param(
    [string]$DockerfilePath,
    [string]$ComposePath,
    [switch]$AuthorityOnly)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Read-SharpProofSdkPolicy.ps1')

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ([string]::IsNullOrWhiteSpace($DockerfilePath)) {
    $DockerfilePath = Join-Path $repositoryRoot 'eng/container/Dockerfile'
}
if ([string]::IsNullOrWhiteSpace($ComposePath)) {
    $ComposePath = Join-Path $repositoryRoot 'compose.yaml'
}
$catalog = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/container/toolchain.json') -Raw |
    ConvertFrom-Json
$apiInventoryManifest = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/api-coverage/net10.0.manifest.json') -Raw |
    ConvertFrom-Json
$acceptance = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'eng/acceptance/contract.json') -Raw |
    ConvertFrom-Json
$globalJson = Read-SharpProofSdkPolicy -Path (
    Join-Path $repositoryRoot 'global.json')
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
    Join-Path $repositoryRoot 'eng/scripts/package-projects.json') -Raw |
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
        [Collections.IEnumerable]$Lines,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Name)

    $Lines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($Lines))
    $matchingLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($Lines | Where-Object { $_ -cmatch $Pattern }))
    if ($matchingLines.Length -cne 1 -or $matchingLines[0] -cne $Expected) {
        throw "$Name must occur exactly once as '$Expected'."
    }
}

function Get-DockerfileStageLines {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [Collections.IEnumerable]$Lines,
        [Parameter(Mandatory = $true)][string]$FromLine)

    $Lines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($Lines))
    $start = $Lines.IndexOf($FromLine)
    if ($start -lt 0) {
        throw "Dockerfile stage '$FromLine' was not found."
    }
    $end = $Lines.Length
    for ($index = $start + 1; $index -lt $Lines.Length; $index++) {
        if ($Lines[$index] -cmatch '^FROM\s+') {
            $end = $index
            break
        }
    }
    return , [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($Lines[($start + 1)..($end - 1)]))
}

function Assert-DockerfileAuthority {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)]$ToolchainCatalog)

    $lines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($Content -split "`r?`n"))
    if (@($lines | Where-Object { $_ -cmatch '^\s*#\s*syntax\s*=' }).Count -ne 0) {
        throw (
            'The Dockerfile must use the bundled Dockerfile grammar and must not ' +
            'introduce an unpinned external frontend.')
    }

    $authorities = [Collections.Immutable.ImmutableArray]::CreateRange[object]([object[]]@(
            ([ordered]@{
                Argument = 'DOTNET_MINIMUM_SDK_IMAGE'
                Image    = "$($ToolchainCatalog.dotnet.minimumSdkImage)@$($ToolchainCatalog.dotnet.minimumSdkImageDigest)"
            }).AsReadOnly(),
            ([ordered]@{
                Argument = 'DOTNET_MINIMUM_FRAMEWORK_IMAGE'
                Image    = "$($ToolchainCatalog.dotnet.minimumSdkFrameworkImage)@$($ToolchainCatalog.dotnet.minimumSdkFrameworkImageDigest)"
            }).AsReadOnly(),
            ([ordered]@{
                Argument = 'DOTNET_SDK_IMAGE'
                Image    = "$($ToolchainCatalog.dotnet.baseImage)@$($ToolchainCatalog.dotnet.baseImageDigest)"
            }).AsReadOnly()))

    $firstFrom = -1
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -cmatch '^FROM\s+') {
            $firstFrom = $index
            break
        }
    }
    if ($firstFrom -lt 0) {
        throw 'The Dockerfile must contain the canonical build stages.'
    }

    $authorityArguments = (($authorities | ForEach-Object {
                [regex]::Escape($_.Argument)
            }) -join '|')
    $argumentPattern = '^ARG\s+(?<argument>' + $authorityArguments + ')(?:=|$)'
    $declarationBuilder = [Collections.Immutable.ImmutableDictionary]::CreateBuilder[string, object]([StringComparer]::OrdinalIgnoreCase)
    foreach ($authority in $authorities) {
        $declarationBuilder[$authority.Argument] = ([ordered]@{
                DeclarationCount = 0
                First            = $null
            }).AsReadOnly()
    }
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -cmatch $argumentPattern) {
            $declarations = $declarationBuilder[$Matches.argument]
            $first = if ($declarations.DeclarationCount -eq 0) {
                ([ordered]@{ Index = $index; Text = $lines[$index] }).AsReadOnly()
            }
            else { $declarations.First }
            $declarationBuilder[$Matches.argument] = ([ordered]@{
                    DeclarationCount = $declarations.DeclarationCount + 1
                    First            = $first
                }).AsReadOnly()
        }
    }

    $declarationsByArgument = $declarationBuilder.ToImmutable()
    foreach ($authority in $authorities) {
        $declarations = $declarationsByArgument[$authority.Argument]
        $expected = "ARG $($authority.Argument)=$($authority.Image)"
        if ($declarations.DeclarationCount -cne 1 -or
            $declarations.First.Index -ge $firstFrom -or
            $declarations.First.Text -cne $expected) {
            throw (
                "Dockerfile authority $($authority.Argument) must be declared " +
                "exactly once globally as '$expected'.")
        }
    }

    $actualStages = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($lines | Where-Object { $_ -cmatch '^FROM\s+' }))
    $expectedStages = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(
            'FROM ${DOTNET_SDK_IMAGE} AS powershell',
            'FROM ${DOTNET_MINIMUM_SDK_IMAGE} AS minimum-sdk',
            'FROM ${DOTNET_MINIMUM_FRAMEWORK_IMAGE} AS minimum-framework',
            'FROM powershell AS api-inventory',
            'FROM ${DOTNET_SDK_IMAGE} AS windows-native',
            'FROM ${DOTNET_SDK_IMAGE} AS toolchain',
            'FROM toolchain AS consumer'))
    if ($actualStages.Length -cne $expectedStages.Length) {
        throw 'The Dockerfile must contain exactly the canonical build stages.'
    }
    for ($index = 0; $index -lt $expectedStages.Length; $index++) {
        if ($actualStages[$index] -cne $expectedStages[$index]) {
            throw (
                "Dockerfile stage $index must be '$($expectedStages[$index])'; " +
                "found '$($actualStages[$index])'.")
        }
    }


    $windowsNativeLines = Get-DockerfileStageLines `
        -Lines $lines `
        -FromLine 'FROM ${DOTNET_SDK_IMAGE} AS windows-native'
    $windowsNativeText = $windowsNativeLines -join "`n"
    foreach ($required in @(
            'COPY eng/container/windows-native/ /opt/sharpproof-windows-cross/',
            'COPY eng/container/toolchain.json /opt/sharpproof-windows-cross/toolchain.json',
            '/bin/bash /opt/sharpproof-windows-cross/build.sh',
            'python3 /opt/sharpproof-windows-cross/verify-output.py',
            '/opt/sharpproof-windows-cross/toolchain.json /output /payload')) {
        if (-not $windowsNativeText.Contains($required, [StringComparison]::Ordinal)) {
            throw "Dockerfile windows-native stage is missing '$required'."
        }
    }
    foreach ($url in @(
            [string]$ToolchainCatalog.z3.windowsCrossBuild.sourceUrl,
            [string]$ToolchainCatalog.z3.windowsCrossBuild.compilerUrl)) {
        if (-not $windowsNativeText.Contains($url, [StringComparison]::Ordinal)) {
            throw "Dockerfile windows-native stage does not use the catalog input '$url'."
        }
    }

    $powershellLines = Get-DockerfileStageLines `
        -Lines $lines `
        -FromLine 'FROM ${DOTNET_SDK_IMAGE} AS powershell'
    Assert-SingleMatchingLine `
        $powershellLines `
        '^ADD --checksum=' `
        "ADD --checksum=sha256:$($ToolchainCatalog.powershell.archiveSha256) $($ToolchainCatalog.powershell.archiveUrl) /tmp/powershell.tar.gz" `
        'serviced PowerShell archive'

    $apiInventoryPackPath = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/$($ToolchainCatalog.dotnet.apiInventoryPackVersion)"
    $apiInventoryLines = Get-DockerfileStageLines `
        -Lines $lines `
        -FromLine 'FROM powershell AS api-inventory'
    Assert-SingleMatchingLine `
        $apiInventoryLines `
        '^ADD --checksum=' `
        "ADD --checksum=sha256:$($ToolchainCatalog.dotnet.apiInventoryPackSha256) $($ToolchainCatalog.dotnet.apiInventoryPackUrl) /tmp/reference-pack.nupkg" `
        'reviewed API reference pack archive'
    if (-not ($apiInventoryLines -join "`n").Contains("`$destination = `"$apiInventoryPackPath`"", [StringComparison]::Ordinal)) {
        throw "Dockerfile api-inventory stage must install the pack at '$apiInventoryPackPath'."
    }

    $toolchainLines = Get-DockerfileStageLines `
        -Lines $lines `
        -FromLine 'FROM ${DOTNET_SDK_IMAGE} AS toolchain'
    $toolchainText = $toolchainLines -join "`n"
    Assert-SingleMatchingLine `
        $toolchainLines `
        '^COPY --from=api-inventory ' `
        "COPY --from=api-inventory $apiInventoryPackPath $apiInventoryPackPath" `
        'exact reviewed API reference pack copy'
    $testRuntimePath = "/usr/share/dotnet/shared/Microsoft.NETCore.App/$($ToolchainCatalog.dotnet.testRuntimeVersion)"
    $testRuntimeCopyPattern = '^COPY --from=\S+ ' + [regex]::Escape($testRuntimePath) + ' '
    Assert-SingleMatchingLine `
        $toolchainLines `
        $testRuntimeCopyPattern `
        "COPY --from=minimum-framework $testRuntimePath $testRuntimePath" `
        'pinned test runtime copy'
    Assert-SingleMatchingLine `
        $toolchainLines `
        '^COPY --from=windows-native ' `
        'COPY --from=windows-native /payload/ /opt/sharpproof/windows-native/' `
        'verified Windows native payload copy'
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

    Assert-SingleMatchingLine `
        $toolchainLines `
        '^ENV SHARPPROOF_REPO_ROOT=' `
        'ENV SHARPPROOF_REPO_ROOT=/workspace/SharpProof' `
        'dev repository root'
    Assert-SingleMatchingLine `
        $toolchainLines `
        '^WORKDIR ' `
        'WORKDIR /workspace/SharpProof' `
        'dev working directory'
    Assert-SingleMatchingLine `
        $toolchainLines `
        '^USER ' `
        'USER sharpproof' `
        'dev user'
    Assert-SingleMatchingLine `
        $toolchainLines `
        '^ENTRYPOINT ' `
        'ENTRYPOINT ["/usr/local/bin/sharpproof-container"]' `
        'dev entrypoint'
    Assert-SingleMatchingLine `
        $toolchainLines `
        '^CMD ' `
        'CMD ["dev"]' `
        'dev default command'
}

function Assert-ConsumerAuthority {
    param([Parameter(Mandatory)][string]$Content)

    $lines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($Content -split "`r?`n"))
    $consumer = Get-DockerfileStageLines -Lines $lines -FromLine 'FROM toolchain AS consumer'
    Assert-SingleMatchingLine $consumer '^ARG ' 'ARG SHARPPROOF_PACKAGE_VERSION' 'consumer version argument'
    Assert-SingleMatchingLine $consumer '^LABEL ' `
        'LABEL org.opencontainers.image.source="https://github.com/alexyorke/SharpProof" \' 'consumer source label'
    Assert-SingleMatchingLine $consumer '^\s+io\.sharpproof\.consumer\.version=' `
        '      io.sharpproof.consumer.version="${SHARPPROOF_PACKAGE_VERSION}" \' 'consumer version label'
    Assert-SingleMatchingLine $consumer '^\s+io\.sharpproof\.platform=' `
        '      io.sharpproof.platform="linux/amd64"' 'consumer platform label'
    Assert-SingleMatchingLine $consumer '^USER ' 'USER root' 'consumer startup user'
    Assert-SingleMatchingLine $consumer '^COPY ' `
        'COPY --chmod=0755 eng/container/consumer-entrypoint.sh /usr/local/bin/sharpproof-consumer' 'consumer entrypoint source'
    Assert-SingleMatchingLine $consumer '^RUN ' `
        'RUN test -n "${SHARPPROOF_PACKAGE_VERSION}" \' 'consumer required version'
    Assert-SingleMatchingLine $consumer '^\s+&& install ' `
        '    && install -d -o sharpproof -g sharpproof /workspace/consumer' 'consumer workspace ownership'
    Assert-SingleMatchingLine $consumer '^WORKDIR ' 'WORKDIR /workspace/consumer' 'consumer working directory'
    Assert-SingleMatchingLine $consumer '^ENTRYPOINT ' `
        'ENTRYPOINT ["/usr/local/bin/sharpproof-consumer"]' 'consumer entrypoint'
    Assert-SingleMatchingLine $consumer '^CMD ' 'CMD ["--info"]' 'consumer default command'
}

function Assert-ComposeAuthority {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Platform)

    $lines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($Content -split "`r?`n"))
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

    $commonStart = $lines.IndexOf(
        'x-sharpproof-common: &sharpproof-common')
    $servicesStart = $lines.IndexOf('services:')
    if ($commonStart -ge $servicesStart) {
        throw 'Compose common authority must precede the services mapping.'
    }
    $commonLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($lines[($commonStart + 1)..($servicesStart - 1)]))
    $expectedImage = '  image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}'
    Assert-SingleMatchingLine $commonLines '^  image:' $expectedImage 'Compose tooling image'
    Assert-SingleMatchingLine $commonLines '^  platform:' "  platform: $Platform" 'Compose platform'
    Assert-SingleMatchingLine `
        $commonLines `
        '^    SHARPPROOF_REPO_ROOT:' `
        '    SHARPPROOF_REPO_ROOT: /workspace/SharpProof' `
        'Compose repository root'
    Assert-SingleMatchingLine $commonLines '^  build:' '  build:' 'Compose build mapping'

    $buildStart = $commonLines.IndexOf('  build:')
    $buildEnd = $commonLines.Length
    for ($index = $buildStart + 1; $index -lt $commonLines.Length; $index++) {
        if ($commonLines[$index] -cmatch '^  \S') {
            $buildEnd = $index
            break
        }
    }
    $buildLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($commonLines[($buildStart + 1)..($buildEnd - 1)]))
    Assert-SingleMatchingLine $buildLines '^    context:' '    context: .' 'Compose build context'
    Assert-SingleMatchingLine `
        $buildLines `
        '^    dockerfile:' `
        '    dockerfile: eng/container/Dockerfile' `
        'Compose Dockerfile'
    Assert-SingleMatchingLine $buildLines '^    target:' '    target: toolchain' 'Compose build target'

    $assertService = {
        param(
            [Parameter(Mandatory = $true)][string]$ServiceName,
            [Parameter(Mandatory = $true)][int]$ServiceStart,
            [Parameter(Mandatory = $true)][int]$ServiceEnd)

        $serviceLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@($lines[($ServiceStart + 1)..($ServiceEnd - 1)]))
        Assert-SingleMatchingLine `
            $serviceLines `
            '^    <<:' `
            '    <<: *sharpproof-common' `
            "Compose service '$ServiceName' authority"
        if ($ServiceName -ceq 'lint') {
            # The lint service may layer tools over the canonical image, but
            # cannot select an unrelated base or override the platform.
            Assert-SingleMatchingLine $serviceLines '^    image:' `
                '    image: ${COMPOSE_PROJECT_NAME}-lint:local' 'Lint image'
            Assert-SingleMatchingLine $serviceLines '^    build:' '    build:' 'Lint build'
            Assert-SingleMatchingLine $serviceLines '^      context:' '      context: .' 'Lint context'
            Assert-SingleMatchingLine $serviceLines '^      dockerfile:' `
                '      dockerfile: eng/lint/Dockerfile' 'Lint Dockerfile'
            Assert-SingleMatchingLine $serviceLines '^      additional_contexts:' `
                '      additional_contexts:' 'Lint build contexts'
            Assert-SingleMatchingLine $serviceLines '^        canonical_tooling:' `
                '        canonical_tooling: service:tooling' 'Lint base'
            if (@($serviceLines | Where-Object { $_ -cmatch '^    platform:' }).Count -ne 0) {
                throw 'Lint must inherit the canonical platform.'
            }
            $lintLines = [Collections.Immutable.ImmutableArray]::CreateRange[string]([string[]]@(Get-Content (Join-Path $repositoryRoot 'eng/lint/Dockerfile')))
            Assert-SingleMatchingLine $lintLines '^FROM .* AS lint$' `
                'FROM canonical_tooling AS lint' 'Lint final stage'
            if (@($lintLines | Where-Object { $_ -cmatch '^FROM ' })[-1] -cne 'FROM canonical_tooling AS lint') {
                throw 'Lint must end in a stage derived from the canonical image.'
            }
            return
        }
        if (@($serviceLines | Where-Object {
                    $_ -cmatch '^    (?:image|build|platform):'
                }).Count -ne 0) {
            throw "Compose service '$ServiceName' overrides canonical image authority."
        }
    }

    $serviceNameBuilder = [Collections.Immutable.ImmutableArray]::CreateBuilder[string]()
    $currentServiceName = $null
    $currentServiceStart = -1
    for ($index = $servicesStart + 1; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -cmatch '^\S') {
            if ($currentServiceStart -ge 0) {
                & $assertService $currentServiceName $currentServiceStart $index
            }
            $currentServiceStart = -1
            break
        }
        if ($lines[$index] -cmatch '^  ([a-z0-9-]+):\s*$') {
            if ($currentServiceStart -ge 0) {
                & $assertService $currentServiceName $currentServiceStart $index
            }
            $currentServiceName = $Matches[1]
            [void]$serviceNameBuilder.Add($currentServiceName)
            $currentServiceStart = $index
        }
    }
    if ($currentServiceStart -ge 0) {
        & $assertService $currentServiceName $currentServiceStart $lines.Length
    }
    $serviceNames = $serviceNameBuilder.ToImmutable()
    if ($serviceNames -cnotcontains 'tooling') {
        throw 'Compose must define the canonical tooling service.'
    }
}

Assert-Exact $catalog.schemaVersion 2 'Container toolchain schema'
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

Assert-Exact $catalog.dotnet.sdkVersion $apiInventoryManifest.sdkVersion 'API inventory generation SDK'
Assert-Exact $catalog.dotnet.apiInventoryPackUrl ('https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.ref/{0}/microsoft.netcore.app.ref.{0}.nupkg' -f $catalog.dotnet.apiInventoryPackVersion) 'API inventory reference pack source'
Assert-Exact $catalog.dotnet.testRuntimeVersion $catalog.dotnet.minimumSdkFrameworkVersion 'Test runtime from the minimum-framework SDK'
Assert-DockerfileAuthority -Content $dockerfile -ToolchainCatalog $catalog
Assert-ConsumerAuthority -Content $dockerfile
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
    $directoryBuildTargets -cnotmatch 'SHARPPROOF_CONTAINER_CONTRACT') {
    throw 'Repository MSBuild entry points must reject host execution.'
}
if ($compose -cnotmatch [regex]::Escape(
        "cpus: `${SHARPPROOF_CONTAINER_CPU_LIMIT:-$($acceptance.container.defaultCpuLimit)}")) {
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
$versions = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/Versions.props') -Raw)
Assert-Exact $versions.Project.PropertyGroup.MicrosoftZ3Version $catalog.z3.version 'Microsoft.Z3 version'
Assert-Exact `
($packageProjects.projects -join '|') `
    'src/SharpProof.Attributes/SharpProof.Attributes.csproj|src/SharpProof.Verifier/SharpProof.Verifier.csproj|src/SharpProof.Package/SharpProof.Package.csproj' `
    'Package projects in dependency order'
Assert-Exact `
    $catalog.support.verifierPackageId `
    'SharpProof.Verifier' `
    'Verifier package ID'

if ($IsLinux -and $env:SHARPPROOF_CONTAINER -ceq '1') {
    $apiInventoryPack = "/usr/share/dotnet/packs/$($apiInventoryManifest.pack)/$($apiInventoryManifest.version)/ref/$($apiInventoryManifest.framework)"
    if (-not (Test-Path -LiteralPath $apiInventoryPack -PathType Container)) {
        throw "The reviewed active API inventory reference pack is missing: $apiInventoryPack"
    }
    $compatibilityFramework = 'net{0}.0' -f ([version]$catalog.dotnet.apiInventoryPackVersion).Major
    $compatibilityPack = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/$($catalog.dotnet.apiInventoryPackVersion)/ref/$compatibilityFramework"
    if (-not (Test-Path -LiteralPath $compatibilityPack -PathType Container)) {
        throw "The retained compatibility reference pack is missing: $compatibilityPack"
    }
    $markerPath = $env:SHARPPROOF_CONTAINER_CONTRACT
    if ([string]::IsNullOrWhiteSpace($markerPath)) {
        throw 'SHARPPROOF_CONTAINER_CONTRACT must identify the installed marker.'
    }
    $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    Assert-Exact $marker.schemaVersion 3 'Installed container contract schema version'
    Assert-Exact `
        $marker.powershellArchiveSha256 `
        $catalog.powershell.archiveSha256 `
        'Installed PowerShell archive SHA-256 digest'
    Assert-Exact `
        $marker.contractVersion `
        $catalog.containerContractVersion `
        'Installed container contract version'
    Assert-Exact `
        $marker.z3LibrarySha256 `
        $catalog.z3.librarySha256 `
        'Installed Z3 SHA-256 digest'
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
    $native = Join-Path `
    ($env:SHARPPROOF_NATIVE_ROOT ?? '/opt/sharpproof/native') `
        "z3/$($catalog.z3.version)/linux-x64/libz3.so"
    $information = Get-Item -LiteralPath $native
    Assert-Exact $information.Length $catalog.z3.libraryBytes 'Installed Z3 size'
    $nativeSha256 = (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Exact $nativeSha256 $catalog.z3.librarySha256 'Installed Z3 SHA-256 digest'
    $managed = Join-Path (Split-Path -Parent $native) 'Microsoft.Z3.dll'
    $managedInformation = Get-Item -LiteralPath $managed
    Assert-Exact `
        $managedInformation.Length `
        $catalog.z3.managedAssemblyBytes `
        'Installed managed Z3 size'
    $managedSha256 = (Get-FileHash -LiteralPath $managed -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Exact `
        $managedSha256 `
        $catalog.z3.managedAssemblySha256 `
        'Installed managed Z3 SHA-256 digest'
    foreach ($architecture in @('x64', 'arm64')) {
        $windowsCatalog = if ($architecture -ceq 'x64') { $catalog.z3.windows } else { $catalog.z3.windowsArm64 }
        $windowsNative = Join-Path `
        ($env:SHARPPROOF_NATIVE_ROOT ?? '/opt/sharpproof/native') `
            "z3/$($catalog.z3.version)/win-$architecture/libz3.dll"
        $windowsInformation = Get-Item -LiteralPath $windowsNative
        Assert-Exact $windowsInformation.Length $windowsCatalog.libraryBytes "Installed Windows $architecture Z3 size"
        $windowsSha256 = (Get-FileHash -LiteralPath $windowsNative -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Exact $windowsSha256 $windowsCatalog.librarySha256 "Installed Windows $architecture Z3 SHA-256 digest"
        $windowsManaged = Join-Path (Split-Path -Parent $windowsNative) 'Microsoft.Z3.dll'
        $windowsManagedInformation = Get-Item -LiteralPath $windowsManaged
        Assert-Exact `
            $windowsManagedInformation.Length `
            $catalog.z3.managedAssemblyBytes `
            "Installed Windows $architecture managed Z3 size"
        $windowsManagedSha256 = (Get-FileHash -LiteralPath $windowsManaged -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Exact `
            $windowsManagedSha256 `
            $catalog.z3.managedAssemblySha256 `
            "Installed Windows $architecture managed Z3 SHA-256 digest"
    }
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

if ($env:SHARPPROOF_CONTAINER -ceq '1') {
    Assert-Exact $PSVersionTable.PSVersion.ToString() $catalog.powershell.version 'Serviced PowerShell version'
    Import-Module PSScriptAnalyzer -RequiredVersion $catalog.scriptTools.psscriptAnalyzer.version -Force
    Assert-Exact (Get-Module PSScriptAnalyzer).Version.ToString() $catalog.scriptTools.psscriptAnalyzer.version 'PSScriptAnalyzer version'
    $shellcheckVersion = & shellcheck --version
    if ($LASTEXITCODE -ne 0 -or $shellcheckVersion -notcontains "version: $($catalog.scriptTools.shellcheck.version)") {
        throw 'The pinned ShellCheck is not installed.'
    }
    Assert-Exact (& shfmt --version) "v$($catalog.scriptTools.shfmt.version)" 'shfmt version'
    if ($LASTEXITCODE -ne 0) { throw 'The pinned shfmt is not installed.' }
    & bash (Join-Path $PSScriptRoot 'Test-SharpProofContainerEntrypoint.sh')
    if ($LASTEXITCODE -ne 0) { throw 'Container entrypoint input and clean-source regressions failed.' }
}

Write-Host 'SharpProof container contract validation passed.'
