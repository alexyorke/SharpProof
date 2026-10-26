# Release process

[release.yml](../../.github/workflows/release.yml) publishes a release from a `v*` tag.

1. Update `SharpProofPackageVersion` in [eng/Versions.props](../../eng/Versions.props).
   The workflow rejects a tag that does not match it.
2. Review [the changelog](../../CHANGELOG.md), [upgrade guidance](../../docs/upgrading.md),
   and the [support matrix](../../docs/preview-support.md). Describe changes to Unknown
   reasons, selection, assumptions, proof outcomes, and evidence formats even if
   application APIs are unchanged.
3. Tag a `master` commit whose `ci.yml` run succeeded.
4. The package job runs `tooling pack` and `tooling pack-tool` in the canonical Linux
   amd64 container, then runs the package tests against that exact feed.
5. The publish job (canonical repository only, `nuget.org` environment) pushes every
   `.nupkg` with its `.snupkg` through NuGet OIDC and creates the GitHub release with
   the same archives. NuGet and GitHub publication are separate operations: a GitHub
   release failure after the NuGet push does not roll back published packages.

Public CI uses distinct `0.0.0-ci.<run>.<attempt>` versions through
`SHARPPROOF_CI_PACKAGE_VERSION`; those binaries are never release artifacts.

## Packages

The three-package graph is `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`.
The standard consumer entrypoint is one `SharpProof` reference without activation
properties or `PrivateAssets="all"`. The optional `SharpProof.Tool`,
`SharpProof.CodeFixes`, and `SharpProof.Templates` packages are packed separately by
`tooling pack-tool`; the main package graph does not depend on them.

[third-party-components.json](third-party-components.json) records package components
summarized in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt).
[Native SMT packaging](../../docs/native-smt-packaging.md) describes layout and pinned
payload checks.

## Debugging packages and packing

All first-party assemblies use deterministic builds and portable PDBs with
commit-specific Source Link. The two core `.snupkg` files contain matching PDBs
for core-package assemblies; optional Tool/editor payloads embed their PDBs.
Every PDB also embeds its source files, including linked and generated source.
Release optimization remains enabled; optimized locals and stepping can differ
from a Debug build.

In Visual Studio, enable Source Link support and the NuGet.org symbol server, and
disable Just My Code to step into package code.

`tooling pack` writes `build.binlog` and `pack.binlog` under
`artifacts/pack-diagnostics`, separate from the package feed. Binary logs exclude
project import contents but can still contain paths and evaluated properties;
review them before sharing.
