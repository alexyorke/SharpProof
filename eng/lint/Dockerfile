FROM node:25-bookworm-slim@sha256:81db02c4b671288a03915da9534dbd54f96d0e7c24d80ccc54f5b36b2e684370 AS node
FROM rhysd/actionlint:1.7.12@sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667 AS actionlint
FROM ghcr.io/zizmorcore/zizmor:1.32.0@sha256:3014cc8f68f663935f69b3c3e0e41f5ce102b268d97db13db0f62208747f860d AS zizmor
FROM lycheeverse/lychee:0.24.2@sha256:e2d19e57cf6ab037026f20b8e449a1f30d9d7f81eef4194763aab2eab20bd28d AS lychee
FROM canonical_tooling AS lint
USER root
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules/npm /usr/local/lib/node_modules/npm
COPY --from=actionlint /usr/local/bin/actionlint /usr/local/bin/actionlint
COPY --from=zizmor /usr/bin/zizmor /usr/local/bin/zizmor
COPY --from=lychee /usr/local/bin/lychee /usr/local/bin/lychee
COPY eng/lint/package*.json /opt/sharpproof/lint/
# Every download is pinned: npm by its lockfile, ApiCompat by this checksum.
ADD --checksum=sha256:41cac2f03e95d1423a40332df037fdccfeb22710cb362744210a359f3e064724 https://api.nuget.org/v3-flatcontainer/microsoft.dotnet.apicompat.tool/10.0.404/microsoft.dotnet.apicompat.tool.10.0.404.nupkg /tmp/lint-tools/
RUN export HOME=/tmp/lint-home DOTNET_CLI_HOME=/tmp/lint-dotnet NUGET_PACKAGES=/tmp/lint-nuget \
    && node /usr/local/lib/node_modules/npm/bin/npm-cli.js ci --prefix /opt/sharpproof/lint --ignore-scripts --no-audit --no-fund \
    && printf '<configuration><packageSources><clear /><add key="pinned" value="/tmp/lint-tools" /></packageSources></configuration>' > /tmp/lint-tools/nuget.config \
    && dotnet tool install Microsoft.DotNet.ApiCompat.Tool --version 10.0.404 --tool-path /opt/sharpproof/tools --configfile /tmp/lint-tools/nuget.config \
    && rm -rf /tmp/lint-tools
ENV PATH="/opt/sharpproof/lint/node_modules/.bin:/opt/sharpproof/tools:${PATH}"
USER sharpproof
