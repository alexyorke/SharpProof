# Release process

[release.yml](../../.github/workflows/release.yml) publishes a release from a `v*` tag.

1. Update `PreReleaseVersionIteration` (or `PreReleaseVersionLabel`) in [eng/Versions.props](../../eng/Versions.props).
   The workflow rejects a tag that does not match it.
2. Review [the changelog](../../CHANGELOG.md), [upgrade guidance](../../docs/upgrading.md),
   and the [support matrix](../../docs/preview-support.md). Describe changes to Unknown
   reasons, selection, assumptions, proof outcomes, and evidence formats even if
   application APIs are unchanged. Then rename the changelog's `## Unreleased`
   heading to `## <version>` (for example `## 1.0.0-preview.2`) and start a new
   empty `## Unreleased` above it: the workflow publishes that section as the
   GitHub release notes and rejects a tag without it. Move the entries in each
   `PublicAPI.Unshipped.txt` and `AnalyzerReleases.Unshipped.md` to their
   `Shipped` files.
3. Before the first release, set the `NUGET_USER` repository variable and the
   nuget.org trusted-publishing policy for this repository, `release.yml` and the
   `nuget.org` environment. The package job fails at once when the variable is
   missing. Run the workflow by manual dispatch on `master` and fix what it
   finds: release tags are immutable, so a tag that fails cannot be moved.
4. Tag a `master` commit whose `ci.yml` run succeeded.
5. The package job builds the consumer image (`./ci.sh consumer-image`): the
   Dockerfile's `consumer` stage on top of the exact tooling image the job packs
   with. A tagged canonical run pushes it to `ghcr.io/alexyorke/sharpproof-preview`
   and pins its registry digest. GHCR creates a new package as private, and the
   templates on nuget.org pin that image, so make it public before approving the
   `nuget.org` deployment. The job then runs `tooling pack` and
   `tooling pack-tool` in the canonical Linux amd64 container (`SharpProof.Templates`
   pins the consumer image by that digest) and runs the package tests against that
   exact feed. The uploaded artifact includes `consumer-image/reference.txt`.
6. Native smoke jobs on `windows-latest` and `macos-latest` download those archives
   and require one Proven postcondition and its Refuted broken counterpart. These
   jobs use the runner's architecture and Core MSBuild; they do not qualify every
   packaged architecture or full-framework MSBuild.exe.
7. Tagged canonical-repository runs attest the `.nupkg` files with GitHub build
   provenance. The workflow does not currently generate an SPDX SBOM, a signed
   offline manifest, or an attestation for the `.snupkg` files.
8. The publish job (canonical repository only, `nuget.org` environment) requires
   packaging, both native smoke jobs and attestation. It pushes every
   `.nupkg` with its `.snupkg` through NuGet OIDC and creates the GitHub release with
   the same archives. NuGet and GitHub publication are separate operations: a GitHub
   release failure after the NuGet push does not roll back published packages.
   After a failure, use "Re-run failed jobs": it reuses the packages the first
   attempt built. A full re-run builds new packages and a new consumer image,
   which no longer match anything already pushed.

Manual dispatch on a branch exercises packaging and native smoke tests without
publishing. Its consumer image stays on the runner, and the templates pin the
local image ID instead of a registry digest, so a dispatch's packages are never
release artifacts. `./ci.sh release-packages` runs the same dry run locally. Dispatch against a `v*` tag still satisfies the workflow's tag-gated
publication conditions. A passing workflow definition or source version is not
evidence that the corresponding packages have reached NuGet.org.

For a published package, verify the original `.nupkg` against its GitHub build
provenance and inspect the attested source and workflow identity:

```sh
gh attestation verify <package.nupkg> --repo alexyorke/SharpProof
```

NuGet repository signatures cover a different verification boundary; a public
assembly strong name is not publisher authentication.

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
