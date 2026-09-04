ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0.316-bookworm-slim@sha256:10e4355ee23eea62fddba62d35284d36e5fc682762561773ed76c8dae5fa8c9a
ARG DOTNET_MINIMUM_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0.300-bookworm-slim@sha256:c5b188baf837b0180a14f988815b9cc7a55b836dd6cbe1b6e6523cf3098faaa8
ARG DOTNET_MINIMUM_FRAMEWORK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0.410-bookworm-slim@sha256:b56053d0a8f4627047740941396e76cd9e7a9421c83b1d81b68f10e5019862d7
ARG DOTNET_TEST_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim@sha256:794b603ad8cf882df3a37dcb124eccdcd688c367864cd66e93e921cda9caba76
ARG POWERSHELL_IMAGE=mcr.microsoft.com/powershell:7.5-debian-12@sha256:7ab5bd5ca6f95a3351fa0c6a1205237d57048c94542355aab55519a0861a9b25

FROM ${POWERSHELL_IMAGE} AS powershell
FROM ${DOTNET_TEST_RUNTIME_IMAGE} AS test-runtime
FROM ${DOTNET_MINIMUM_SDK_IMAGE} AS minimum-sdk
FROM ${DOTNET_MINIMUM_FRAMEWORK_IMAGE} AS minimum-framework

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
COPY --from=test-runtime /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.29 /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.29
COPY --from=minimum-sdk /usr/share/dotnet/sdk/9.0.300 /usr/share/dotnet/sdk/9.0.300
COPY --from=minimum-framework /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.16 /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.16
COPY --from=minimum-framework /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/8.0.16 /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/8.0.16
COPY --from=minimum-framework /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/8.0.16 /usr/share/dotnet/packs/Microsoft.NETCore.App.Host.linux-x64/8.0.16
RUN ln -s /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh \
    && dotnet --version \
    && dotnet --list-sdks | grep -F '9.0.300 [/usr/share/dotnet/sdk]' \
    && test -d /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.16 \
    && test -d /usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/8.0.16 \
    && dotnet --list-runtimes | grep -F 'Microsoft.NETCore.App 8.0.29' \
    && pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'

COPY eng/container/toolchain.json /opt/sharpproof/toolchain.json
COPY eng/container/Prepare-NativePayload.ps1 /opt/sharpproof/Prepare-NativePayload.ps1
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
RUN groupadd --gid "${USER_GID}" sharpproof \
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
