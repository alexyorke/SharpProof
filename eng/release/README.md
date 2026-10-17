# Release process

[package-consumers.yml](../../.github/workflows/package-consumers.yml) owns packing, consumer qualification, and tag publication.

1. The package job runs `tooling pack -Configuration Release` in the canonical Linux amd64 container and uploads the packed three-package graph.
2. Linux container and native macOS ARM64/x64 verifier consumers download those artifacts and run real proof and payload-integrity checks.
3. Branches, pull requests, and tags run portable consumers on Linux, Windows, macOS ARM64, and macOS Intel against the exact packed artifacts. Tags additionally run exact-SHA security.
4. A `v*` tag must exactly match `SharpProofPackageVersion` in [SharpProof.Release.props](../../SharpProof.Release.props).
5. Publication depends on all required tag jobs and runs only in the canonical repository. It downloads the package artifacts and pushes with duplicate skipping.

`v1.0.0-preview.1` selects the private-preview environment/feed. Other matching release tags select nuget.org and NuGet OIDC login. Feed credentials stay in the configured secret/environment mechanism.

Branch package checks do not run tag-only security or publication dependencies and do not publish. Do not report branch success as release qualification or public feed availability.

Package artifacts are named `nuget-packages-<run-id>-<sha>`. Both container and portable consumers download that artifact into `nupkgs`; publication downloads the same artifact. Consumer jobs do not repack product source. Preserve the artifact's run and SHA when reviewing qualification. Duplicate skipping during push does not verify the bytes of a preexisting remote package.

[third-party-components.json](third-party-components.json) records package components summarized in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt). [Native SMT packaging](../../docs/native-smt-packaging.md) describes layout and pinned payload checks.
