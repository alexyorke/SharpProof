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
4. Tag a `master` commit whose latest `ci.yml` and `codeql.yml` push runs succeeded.
   The release preflight enforces workflow identity, exact SHA and master ancestry.
5. The package job builds the consumer image (`./ci.sh consumer-image`): the
   Dockerfile's `consumer` stage on top of the exact tooling image the job packs
   with. It exports an OCI archive and pins its manifest digest without pushing
   anything. The existing `ghcr.io/alexyorke/sharpproof-preview` package must be public
   for public consumers. The job then runs `tooling pack` and
   `tooling pack-tool` in the canonical Linux amd64 container (`SharpProof.Templates`
   pins the consumer image by that digest) and runs the package tests against that
   exact feed. The uploaded artifact includes `consumer-image/reference.txt`.
6. Native smoke jobs on `windows-latest` and `macos-latest` download those archives
   and require one Proven postcondition and its Refuted broken counterpart. These
   jobs use the runner's architecture and Core MSBuild; they do not qualify every
   packaged architecture or full-framework MSBuild.exe.
7. The package job generates Syft and SPDX SBOMs for the staged image and all
   packed packages, then scans both. Fixed high/critical vulnerabilities block
   publication; complete reports also retain unfixed findings for triage.
8. The publish job (canonical repository only, `nuget.org` environment) requires
   preflight, packaging/scanning and both native smoke jobs. After approval it
   rechecks release eligibility, copies the OCI archive without changing its digest,
   and attests the image, `.nupkg`, `.snupkg`, SBOM and vulnerability-report files.
   It pushes every
   `.nupkg` with its `.snupkg` through NuGet OIDC and creates the GitHub release with
   the same archives and evidence, uploading assets to a draft before publishing
   the immutable release. NuGet and GitHub publication are separate operations: a GitHub
   release failure after the NuGet push does not roll back published packages.
   After a failure, use "Re-run failed jobs": it reuses the packages the first
   attempt built. A full re-run builds new packages and a new consumer image,
   which no longer match anything already pushed.
9. Once the packages are on nuget.org, remove the two notes that say they are
   not: the **Status** line at the top of the root `README.md`, and the "Until
   `1.0.0-preview.2` is on nuget.org" clause in step 1 of
   `docs/getting-started.md`. They are the only places that say so.

Manual dispatch on a branch exercises packaging and native smoke tests without
publishing. Its image is retained as a seven-day workflow artifact and the template
digest refers to that unpublished archive. `./ci.sh release-packages` runs the same
dry run locally. Dispatch against a `v*` tag is also a dry run. A passing workflow definition or source version is not
evidence that the corresponding packages have reached NuGet.org.

For a published package, verify the original `.nupkg` against its GitHub build
provenance and inspect the attested source and workflow identity:

```sh
gh attestation verify <package.nupkg> --repo alexyorke/SharpProof \
  --signer-workflow alexyorke/SharpProof/.github/workflows/release.yml \
  --source-ref refs/tags/v<VERSION> --source-digest <COMMIT-SHA> --deny-self-hosted-runners
gh attestation verify oci://ghcr.io/alexyorke/sharpproof-preview@sha256:<DIGEST> \
  --repo alexyorke/SharpProof \
  --signer-workflow alexyorke/SharpProof/.github/workflows/release.yml \
  --source-ref refs/tags/v<VERSION> --source-digest <COMMIT-SHA> --deny-self-hosted-runners
```

Use GitHub CLI 2.102 or newer; older versions have workflow/ref identity matching
bugs. Verify downloaded symbol packages and SBOM files with the same file command.
These are build-provenance attestations over the SBOM bytes, not SPDX predicate
attestations. NuGet repository signatures cover a different verification boundary; a public
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
