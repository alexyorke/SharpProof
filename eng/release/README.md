# Release process

[package-consumers.yml](../../.github/workflows/package-consumers.yml) owns packing, consumer qualification, and tag publication.

1. The package job runs `tooling pack -Configuration Release` in the canonical Linux amd64 container and uploads the packed three-package graph.
2. Linux container and native Windows, macOS, and glibc Linux x64/ARM64 verifier consumers download those artifacts and run real proof and payload-integrity checks.
3. Branches, pull requests, and tags run portable consumers on Linux, Windows, macOS ARM64, and macOS Intel against the exact packed artifacts. Tags additionally run exact-SHA security.
4. A `v*` tag must exactly match `SharpProofPackageVersion` in [SharpProof.Release.props](../../SharpProof.Release.props).
5. Publication depends on all required tag jobs and runs only in the canonical repository. It downloads the package artifacts and pushes with duplicate skipping.

`v1.0.0-preview.1` selects the private-preview environment/feed. Other matching release tags select nuget.org and NuGet OIDC login. Feed credentials stay in the configured secret/environment mechanism.

Branch package checks do not run tag-only security or publication dependencies and do not publish. Do not report branch success as release qualification or public feed availability.

Package artifacts are named `nuget-packages-<run-id>-<sha>`. Both container and portable consumers download that artifact into `nupkgs`; publication downloads the same artifact. Consumer jobs do not repack product source. Preserve the artifact's run and SHA when reviewing qualification. Duplicate skipping during push does not verify the bytes of a preexisting remote package.

[third-party-components.json](third-party-components.json) records package components summarized in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt). [Native SMT packaging](../../docs/native-smt-packaging.md) describes layout and pinned payload checks.

The three-package graph is internal to restore and publication: `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`. The standard consumer entrypoint is one `SharpProof` reference without activation properties or `PrivateAssets="all"`; strict full verification and exact-version API/analyzer dependencies are automatic. Verifier remains a compatibility entrypoint. Release qualification must exercise the minimal main-package consumer and a packed library's downstream consumer so runtime contract dependencies remain intact. Retain explicit portable-only advisory configurations and minimum compiler-host checks separately.

Release qualification includes all six native OS/architecture combinations: Windows, macOS, and glibc Linux on x64/ARM64. Windows payloads link their C++ runtime statically and require no separate Visual C++ redistributable. Require the exact-artifact native ABI matrix to pass; compilation, import audits, and payload presence alone are insufficient. Native consumer builds require SDK 10.0.100+ and the .NET 10 runtime, with the host/filesystem and native runtime prerequisites in the [support matrix](../../docs/preview-support.md). Repository build/test/pack remains canonical-container-only. musl and other OS/CPU combinations are unsupported.

## Prepare release notes and upgrades

Before tagging, review [the changelog](../../CHANGELOG.md), [upgrade guidance](../../docs/upgrading.md), and [support matrix](../../docs/preview-support.md). Record the source SHA, exact package version, consumer qualification run, and significant diagnostic/configuration/semantic changes. Describe changes to Unknown reasons, selection, assumptions, proof outcomes, and evidence formats even if application APIs are unchanged. Do not claim publication until the destination feed confirms it.

Preview the rendered package README and check its relative links on the destination feed. Validate that each package's identity, dependency graph, XML documentation, symbols/source information, and platform payloads match the release artifacts. Keep release-only security and publication dependencies intact.

Run the [project template smoke check](../../templates/README.md#maintainer-validation) against the exact packed feed before publication. Templates obtain the version from release props rather than maintaining their own release number.
