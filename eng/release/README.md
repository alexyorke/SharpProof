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

## Debugging packages and packing

All first-party assemblies use deterministic builds and portable PDBs with
commit-specific Source Link. The three `.snupkg` files contain matching PDBs
for every shipped SharpProof assembly. Every PDB also embeds its source files,
including linked and generated source, so source inspection works offline once
the symbols are available. Package qualification rejects missing embedded source.
Release optimization remains enabled; optimized locals and stepping can differ
from a Debug build.

Publish the `.snupkg` files alongside the `.nupkg` files. In Visual Studio, enable
Source Link support and the NuGet.org symbol server, and disable Just My Code to
step into package code. For private feeds without a symbol server, extract the
matching `.snupkg` and configure that directory as a local symbol source.

`tooling pack` writes `build.binlog` and `pack.binlog` under
`artifacts/pack-diagnostics`, separate from the package feed. These logs remain
local. Binary logs exclude project import contents but can still contain paths
and evaluated properties; review them before sharing.

See the official [Source Link guidance](https://github.com/dotnet/sourcelink),
[NuGet debugging guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget#symbol-packages),
and [symbol package publishing instructions](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg).
