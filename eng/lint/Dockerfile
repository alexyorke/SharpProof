FROM node:22-bookworm-slim@sha256:c3de60bf2f9dd0ac6370e6117950ff62d6e339527e7472301c9c78a017978392 AS node
FROM rhysd/actionlint:1.7.12@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667 AS actionlint
FROM koalaman/shellcheck:v0.11.0@sha256:61862eba1fcf09a484ebcc6feea46f1782532571a34ed51fedf90dd25f925a8d AS shellcheck
FROM ghcr.io/zizmorcore/zizmor:1.31.1@sha256:42d409dbb851d4c5d35a058cf042b828bea1e3f2884378c526eb0974780e18d9 AS zizmor
FROM lycheeverse/lychee:0.24.2@sha256:e2d19e57cf6ab037026f20b8e449a1f30d9d7f81eef4194763aab2eab20bd28d AS lychee
FROM canonical_tooling AS lint
USER root
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/npm
COPY --from=actionlint /usr/local/bin/actionlint /usr/local/bin/actionlint
COPY --from=shellcheck /bin/shellcheck /usr/local/bin/shellcheck
COPY --from=zizmor /usr/bin/zizmor /usr/local/bin/zizmor
COPY --from=lychee /usr/local/bin/lychee /usr/local/bin/lychee
COPY eng/lint/package*.json /opt/sharpproof/lint/
# Every download is pinned: npm by its lockfile, ApiCompat by this checksum.
# PSScriptAnalyzer comes from the tooling image, which checks its catalog hash.
ADD --checksum=sha256:9f1ff5a1a05d8086f78af35ecaa6346c15dd8619026bf61303350a11f7a6978e https://api.nuget.org/v3-flatcontainer/microsoft.dotnet.apicompat.tool/10.0.403/microsoft.dotnet.apicompat.tool.10.0.403.nupkg /tmp/lint-tools/
RUN export HOME=/tmp/lint-home DOTNET_CLI_HOME=/tmp/lint-dotnet NUGET_PACKAGES=/tmp/lint-nuget \
    && node /usr/local/lib/node_modules/npm/bin/npm-cli.js ci --prefix /opt/sharpproof/lint --ignore-scripts --no-audit --no-fund \
    && printf '<configuration><packageSources><clear /><add key="pinned" value="/tmp/lint-tools" /></packageSources></configuration>' > /tmp/lint-tools/nuget.config \
    && dotnet tool install Microsoft.DotNet.ApiCompat.Tool --version 10.0.403 --tool-path /opt/sharpproof/tools --configfile /tmp/lint-tools/nuget.config \
    && rm -rf /tmp/lint-tools
ENV PATH="/opt/sharpproof/lint/node_modules/.bin:/opt/sharpproof/tools:${PATH}"
USER sharpproof
