ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.404-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb
ARG DOTNET_MINIMUM_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0.321-bookworm-slim@sha256:1dfdb27e9c2c345cb598925ccf6b13719ecfebea8467bd4745c23a3c108cdf84
ARG DOTNET_MINIMUM_FRAMEWORK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0.428-bookworm-slim@sha256:1b955f179b02df4f0eb5fadedaa898cbe44d620a64aab52eec72c8e91d1355b5
# MCR's 7.5 aliases still contain 7.5.0, so install the verified serviced
# release on the SDK base this build already pulls instead of pulling a
# PowerShell image only to replace its payload.
FROM ${DOTNET_SDK_IMAGE} AS powershell
ADD --checksum=sha256:db85ae1f45005ffd20e456890585167257151fa99406ccd7ab344f7cf3a766fd https://github.com/PowerShell/PowerShell/releases/download/v7.5.12/powershell-7.5.12-linux-x64.tar.gz /tmp/powershell.tar.gz
RUN mkdir -p /opt/microsoft/powershell/7 \
    && tar -xzf /tmp/powershell.tar.gz -C /opt/microsoft/powershell/7 \
    && chmod +x /opt/microsoft/powershell/7/pwsh \
    && rm /tmp/powershell.tar.gz
FROM ${DOTNET_MINIMUM_SDK_IMAGE} AS minimum-sdk
FROM ${DOTNET_MINIMUM_FRAMEWORK_IMAGE} AS minimum-framework
# The API inventory needs one reviewed reference pack, not a whole SDK image.
# Its NuGet package holds the SDK-installed pack files byte for byte; only the
# analyzers, data, and ref folders are installed, omitting packaging metadata.
FROM powershell AS api-inventory
ADD --checksum=sha256:645538957cff063262a94e2cca4644611e63e5eef8e4a31fe193e8d061e789c5 https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.ref/9.0.18/microsoft.netcore.app.ref.9.0.18.nupkg /tmp/reference-pack.nupkg
RUN /opt/microsoft/powershell/7/pwsh -NoLogo -NoProfile -Command '$ErrorActionPreference = "Stop"; $destination = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.18"; $archive = [IO.Compression.ZipFile]::OpenRead("/tmp/reference-pack.nupkg"); try { foreach ($entry in $archive.Entries) { if ($entry.FullName -cnotmatch "^(analyzers|data|ref)/" -or $entry.FullName.EndsWith("/")) { continue }; if ($entry.FullName -cmatch "(^|/)\.\.(/|$)") { throw "Unsafe reference pack entry: $($entry.FullName)" }; $path = Join-Path $destination $entry.FullName; [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null; [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path) } } finally { $archive.Dispose() }' \
    && rm /tmp/reference-pack.nupkg

FROM ${DOTNET_SDK_IMAGE} AS windows-native
ARG TARGETOS
ARG TARGETARCH
RUN test "${TARGETOS}" = "linux" && test "${TARGETARCH}" = "amd64" \
    && apt-get update \
    && apt-get install -y --no-install-recommends cmake ninja-build python3 xz-utils curl \
    && rm -rf /var/lib/apt/lists/*
COPY eng/container/windows-native/ /opt/sharpproof-windows-cross/
COPY eng/container/toolchain.json /opt/sharpproof-windows-cross/toolchain.json
# Build the pinned source with a pinned official compiler. The recipe verifies
# both archives before extraction and both PE import closures after linking.
RUN mkdir -p /archives /output \
    && curl --fail --location --retry 3 \
        https://github.com/mstorsjo/llvm-mingw/releases/download/20261006/llvm-mingw-20261006-ucrt-ubuntu-22.04-x86_64.tar.xz \
        --output /archives/llvm-mingw-20261006-ucrt-ubuntu-22.04-x86_64.tar.xz \
    && curl --fail --location --retry 3 \
        https://github.com/Z3Prover/z3/archive/z3-4.12.2.tar.gz \
        --output /archives/z3-4.12.2.tar.gz \
    && /bin/bash /opt/sharpproof-windows-cross/build.sh \
    && python3 /opt/sharpproof-windows-cross/verify-output.py \
        /opt/sharpproof-windows-cross/toolchain.json /output /payload

FROM ${DOTNET_SDK_IMAGE} AS toolchain
ARG TARGETOS
ARG TARGETARCH
ARG USER_UID=1000
ARG USER_GID=1000
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_XMLDOC_MODE=skip \
    POWERSHELL_TELEMETRY_OPTOUT=1 \
    SHARPPROOF_CONTAINER=1 \
    SHARPPROOF_CONTAINER_CONTRACT=/etc/sharpproof/container-contract.json \
    SHARPPROOF_NATIVE_ROOT=/opt/sharpproof/native

RUN test "${TARGETOS}" = "linux" && test "${TARGETARCH}" = "amd64"
COPY --from=powershell /opt/microsoft/powershell/7 /opt/microsoft/powershell/7
# The minimum-framework SDK carries the pinned test runtime, byte for byte the
# same files as the matching runtime image.
COPY --from=minimum-framework /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.34 /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.34
COPY --from=minimum-sdk /usr/share/dotnet/sdk/9.0.321 /usr/share/dotnet/sdk/9.0.321
# Retain the runtime and targeting packs needed by the minimum portable compiler host.
COPY --from=minimum-sdk /usr/share/dotnet/shared /usr/share/dotnet/shared
COPY --from=minimum-sdk /usr/share/dotnet/packs /usr/share/dotnet/packs
COPY --from=minimum-framework /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.34 /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.34
COPY --from=minimum-framework /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/8.0.34 /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/8.0.34
# Keep the reviewed API inventory pack alongside the minimum-SDK packs.
COPY --from=api-inventory /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.18 /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.18
COPY --from=minimum-framework /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/8.0.34 /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/8.0.34
# Use only the explicitly pinned PowerShell copy, not the SDK image's unused tool store.
RUN rm -rf /usr/share/powershell
COPY eng/container/toolchain.json /opt/sharpproof/toolchain.json
RUN ln -s /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh \
    && pwsh -NoLogo -NoProfile -Command '$catalog = Get-Content -LiteralPath /opt/sharpproof/toolchain.json -Raw | ConvertFrom-Json; if ((dotnet --version) -cne [string]$catalog.dotnet.sdkVersion) { throw "The base .NET SDK does not match the toolchain catalog." }; if ((dotnet --list-sdks) -notcontains ("{0} [/usr/share/dotnet/sdk]" -f $catalog.dotnet.minimumSdkVersion)) { throw "The minimum .NET SDK does not match the toolchain catalog." }; foreach ($pack in @("Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref", "Microsoft.NETCore.App.Host.linux-x64")) { $path = Join-Path "/usr/share/dotnet/packs/$pack" ([string]$catalog.dotnet.minimumSdkFrameworkVersion); if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "The minimum-SDK framework pack is missing: $path" } }; if ((dotnet --list-runtimes) -notcontains ("Microsoft.NETCore.App {0} [/usr/share/dotnet/shared/Microsoft.NETCore.App]" -f $catalog.dotnet.testRuntimeVersion)) { throw "The test runtime does not match the toolchain catalog." }; if ($PSVersionTable.PSVersion.ToString() -cne [string]$catalog.powershell.version) { throw "PowerShell does not match the serviced archive version." }; $PSVersionTable.PSVersion.ToString()'

COPY eng/container/Install-ScriptTools.ps1 /opt/sharpproof/Install-ScriptTools.ps1
# Moving archive mirrors remove superseded packages, so the immutable Ubuntu
# snapshot comes first. The live archives serve the same reviewed bytes while
# the snapshot service is unavailable; the archive checksum is verified either way.
RUN pwsh -NoLogo -NoProfile -Command '$packages = @(@("x/xz-utils", "xz-utils_5.6.1+really5.4.5-1ubuntu0.4_amd64.deb", "18ef3c30308990626cfbda8b6336ef6ef5a412d1991bdad0cdaa226fd2041ffe"), @("b/brotli", "libbrotli1_1.1.0-2ubuntu0.1_amd64.deb", "70b188a86d81f30d7510aab30e25d638ed5037dded6691af1533952b71ac61de"), @("a/apt", "libapt-pkg6.0t64_2.8.4ubuntu0.1_amd64.deb", "a50f3e68a793f90b05f44a4b5f2f59f3ec7687e39b5d8bbd1da44c84671fa3d6"), @("a/apt", "apt_2.8.4ubuntu0.1_amd64.deb", "ad2ea7f107f250ba88b0bfdba31812a341e3247589647aa2b03b9dc4e8abf8ed"), @("p/pcre2", "libpcre2-8-0_10.42-4ubuntu2.2_amd64.deb", "f41da7cb7e979d6b037ea92650c72ec8ec532ec3c93aa8d5b997207de56695ea")); New-Item -ItemType Directory -Path /tmp/packages | Out-Null; foreach ($package in $packages) { $pool, $file, $sha256 = $package; $path = "/tmp/packages/$file"; $sources = @("https://snapshot.ubuntu.com/ubuntu/20261030T000000Z/pool/main/$pool/$file", "http://archive.ubuntu.com/ubuntu/pool/main/$pool/$file", "http://security.ubuntu.com/ubuntu/pool/main/$pool/$file"); $verified = $false; foreach ($source in $sources) { try { Invoke-WebRequest -Uri $source -OutFile $path -MaximumRetryCount 2 -RetryIntervalSec 5; if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ieq $sha256) { $verified = $true; break }; Write-Warning "Checksum mismatch from ${source}" } catch { Write-Warning "Download failed from ${source}: $_" } }; if (-not $verified) { throw "No source served the reviewed package $file." } }'     && dpkg --install /tmp/packages/*.deb && rm -rf /tmp/packages
RUN pwsh -NoLogo -NoProfile -File /opt/sharpproof/Install-ScriptTools.ps1 \
        -CatalogPath /opt/sharpproof/toolchain.json

COPY eng/container/Prepare-NativePayload.ps1 /opt/sharpproof/Prepare-NativePayload.ps1
COPY --from=windows-native /payload/ /opt/sharpproof/windows-native/
RUN pwsh -NoLogo -NoProfile -File /opt/sharpproof/Prepare-NativePayload.ps1 \
        -CatalogPath /opt/sharpproof/toolchain.json \
        -DestinationRoot /opt/sharpproof/native

COPY eng/container/New-ContainerContract.ps1 /opt/sharpproof/New-ContainerContract.ps1
RUN pwsh -NoLogo -NoProfile -File /opt/sharpproof/New-ContainerContract.ps1 \
        -CatalogPath /opt/sharpproof/toolchain.json \
        -OutputPath "${SHARPPROOF_CONTAINER_CONTRACT}"

COPY --chmod=0755 eng/container/entrypoint.sh /usr/local/bin/sharpproof-container
COPY --chmod=0755 eng/container/dev-command.sh /usr/local/bin/sp
COPY --chmod=0755 eng/container/dev-init.sh /usr/local/bin/sharpproof-dev-init
COPY --chmod=0755 eng/container/loop-command.sh /usr/local/bin/sharpproof-loop

ENV HOME=/home/sharpproof \
    DOTNET_CLI_HOME=/home/sharpproof/.dotnet \
    NUGET_PACKAGES=/home/sharpproof/.nuget/packages
# Ubuntu SDK images reserve UID/GID 1000 for a placeholder account; release it for the workspace user.
RUN if id ubuntu >/dev/null 2>&1; then userdel ubuntu; fi \
    && if getent group ubuntu >/dev/null 2>&1; then groupdel ubuntu; fi \
    && groupadd --gid "${USER_GID}" sharpproof \
    && useradd --uid "${USER_UID}" --gid "${USER_GID}" --create-home --shell /bin/bash sharpproof \
    && install -d -o sharpproof -g sharpproof \
        /home/sharpproof/.local \
        /home/sharpproof/.local/share \
        /home/sharpproof/.local/share/NuGet \
        /home/sharpproof/.nuget \
        /home/sharpproof/.nuget/NuGet \
        /home/sharpproof/.nuget/packages \
        /home/sharpproof/.dotnet \
        /workspace/SharpProof
ENV SHARPPROOF_REPO_ROOT=/workspace/SharpProof
WORKDIR /workspace/SharpProof
USER sharpproof
ENTRYPOINT ["/usr/local/bin/sharpproof-container"]
CMD ["dev"]

FROM toolchain AS consumer
ARG SHARPPROOF_PACKAGE_VERSION
LABEL org.opencontainers.image.source="https://github.com/alexyorke/SharpProof" \
      io.sharpproof.consumer.version="${SHARPPROOF_PACKAGE_VERSION}" \
      io.sharpproof.platform="linux/amd64"
USER root
COPY --chmod=0755 eng/container/consumer-entrypoint.sh /usr/local/bin/sharpproof-consumer
RUN test -n "${SHARPPROOF_PACKAGE_VERSION}" \
    && install -d -o sharpproof -g sharpproof /workspace/consumer
WORKDIR /workspace/consumer
ENTRYPOINT ["/usr/local/bin/sharpproof-consumer"]
CMD ["--info"]
