# syntax=docker/dockerfile:1.7

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0.316-bookworm-slim@sha256:10e4355ee23eea62fddba62d35284d36e5fc682762561773ed76c8dae5fa8c9a
ARG DOTNET_TEST_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:8.0-bookworm-slim@sha256:794b603ad8cf882df3a37dcb124eccdcd688c367864cd66e93e921cda9caba76
ARG POWERSHELL_IMAGE=mcr.microsoft.com/powershell:7.5-debian-12@sha256:7ab5bd5ca6f95a3351fa0c6a1205237d57048c94542355aab55519a0861a9b25

FROM ${POWERSHELL_IMAGE} AS powershell
FROM ${DOTNET_TEST_RUNTIME_IMAGE} AS test-runtime

FROM ${DOTNET_SDK_IMAGE} AS toolchain
ARG TARGETOS
ARG TARGETARCH
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
RUN ln -s /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh \
    && dotnet --version \
    && dotnet --list-runtimes | grep -F 'Microsoft.NETCore.App 8.0.29' \
    && pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'

COPY eng/container/toolchain.json /opt/sharpproof/toolchain.json
COPY eng/container/Prepare-NativePayload.ps1 /opt/sharpproof/Prepare-NativePayload.ps1
COPY eng/container/New-ContainerContract.ps1 /opt/sharpproof/New-ContainerContract.ps1
RUN pwsh -NoLogo -NoProfile -File /opt/sharpproof/Prepare-NativePayload.ps1 \
        -CatalogPath /opt/sharpproof/toolchain.json \
        -DestinationRoot /opt/sharpproof/native \
    && pwsh -NoLogo -NoProfile -File /opt/sharpproof/New-ContainerContract.ps1 \
        -CatalogPath /opt/sharpproof/toolchain.json \
        -OutputPath /etc/sharpproof/container-contract.json

COPY eng/container/entrypoint.sh /usr/local/bin/sharpproof-container
RUN chmod 0755 /usr/local/bin/sharpproof-container

FROM toolchain AS dev
ARG USER_UID=1000
ARG USER_GID=1000
ENV HOME=/home/sharpproof
RUN groupadd --gid "${USER_GID}" sharpproof \
    && useradd --uid "${USER_UID}" --gid "${USER_GID}" --create-home --shell /bin/bash sharpproof \
    && install -d -o sharpproof -g sharpproof /workspace/SharpProof /workspace/artifacts
WORKDIR /workspace/SharpProof
ENTRYPOINT ["/usr/local/bin/sharpproof-container"]
CMD ["dev"]

FROM toolchain AS build
WORKDIR /src
COPY . .
RUN dotnet restore SharpProof.sln --locked-mode \
    && dotnet build SharpProof.sln --configuration Debug --no-restore
ENTRYPOINT ["/usr/local/bin/sharpproof-container"]
CMD ["build"]

FROM build AS test
ENV SHARPPROOF_REPO_ROOT=/src
ENTRYPOINT ["/usr/local/bin/sharpproof-container"]
CMD ["portable-tests"]

FROM build AS package
ENV SHARPPROOF_REPO_ROOT=/src
ENTRYPOINT ["/usr/local/bin/sharpproof-container"]
CMD ["pack"]
