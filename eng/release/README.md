# Release process

[package-consumers.yml](../../.github/workflows/package-consumers.yml) owns packing, consumer qualification, and tag publication.

1. The package job runs `tooling pack -Configuration Release` in the canonical Linux amd64 container and uploads the packed three-package graph.
2. Linux container, native Windows x64, and native macOS ARM64/x64 verifier consumers download those artifacts and run real proof and payload-integrity checks.
3. Branches, pull requests, and tags run portable consumers on Linux, Windows, macOS ARM64, and macOS Intel against the exact packed artifacts. Tags additionally run exact-SHA security.
4. A `v*` tag must exactly match `SharpProofPackageVersion` in [SharpProof.Release.props](../../SharpProof.Release.props).
5. Publication depends on all required tag jobs and runs only in the canonical repository. It downloads the package artifacts and pushes with duplicate skipping.

`v1.0.0-preview.1` selects the private-preview environment/feed. Other matching release tags select nuget.org and NuGet OIDC login. Feed credentials stay in the configured secret/environment mechanism.

Branch package checks do not run tag-only security or publication dependencies and do not publish. Do not report branch success as release qualification or public feed availability.

Package artifacts are named `nuget-packages-<run-id>-<sha>`. Both container and portable consumers download that artifact into `nupkgs`; publication downloads the same artifact. Consumer jobs do not repack product source. Preserve the artifact's run and SHA when reviewing qualification. Public-feed publication verifies qualified package SHA-512 hashes before pushing and after downloading published packages. Mismatched duplicate versions fail publication.

[third-party-components.json](third-party-components.json) records package components summarized in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt). [Native SMT packaging](../../docs/native-smt-packaging.md) describes layout and pinned payload checks.

The three-package graph is internal to restore and publication. The standard consumer entrypoint is one `SharpProof.Verifier` reference without activation properties or `PrivateAssets="all"`; strict full verification and exact-version API/analyzer dependencies are automatic. Release qualification must exercise that minimal consumer and a packed library's downstream consumer so runtime contract dependencies remain intact. Retain the deliberate portable-only entrypoint and minimum compiler-host checks separately.

Release qualification includes the native Linux x64/ARM64 consumer matrix. Require the exact-artifact native matrix to pass; payload presence alone is insufficient. Native consumer builds require SDK 10.0.100+ and the .NET 10 runtime, with the host/filesystem and native runtime prerequisites in the [support matrix](../../docs/preview-support.md). Repository build/test/pack remains canonical-container-only. Windows ARM64 and musl Linux are unsupported.

## Security qualification

See the [repository security runbook](../security/README.md) for exact-master test/coverage/image gates, immutable release tags, explicit publication approval, package SBOM/provenance verification, and image servicing. Tag only after all three exact-commit master push workflows have succeeded.

## Prepare release notes and upgrades

Before tagging, review [the changelog](../../CHANGELOG.md), [upgrade guidance](../../docs/upgrading.md), and [support matrix](../../docs/preview-support.md). Record the source SHA, exact package version, consumer qualification run, and significant diagnostic/configuration/semantic changes. Describe changes to Unknown reasons, selection, assumptions, proof outcomes, and evidence formats even if application APIs are unchanged. Do not claim publication until the destination feed confirms it.

Preview the rendered package README and check its relative links on the destination feed. Validate that each package's identity, dependency graph, XML documentation, symbols/source information, and platform payloads match the release artifacts. Keep release-only security and publication dependencies intact.

Run the [project template smoke check](../../templates/README.md#maintainer-validation) against the exact packed feed before publication. Templates obtain the version from release props rather than maintaining their own release number.
