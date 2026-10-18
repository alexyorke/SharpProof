# Release process

[package-consumers.yml](../../.github/workflows/package-consumers.yml) owns packing, consumer qualification, and tag publication.

1. The package job runs `tooling pack -Configuration Release` in the canonical Linux amd64 container and uploads the packed three-package graph.
2. Linux container and native Windows, macOS, and glibc Linux x64/ARM64 verifier consumers download those artifacts and run real proof and payload-integrity checks.
3. Branches, pull requests, and tags run portable consumers on Linux, Windows, macOS ARM64, and macOS Intel against the exact packed artifacts. Tags additionally run exact-SHA security.
4. A `v*` tag must exactly match `SharpProofPackageVersion` in [eng/Versions.props](../../eng/Versions.props).
5. Publication depends on all required tag jobs and runs only in the canonical repository. It downloads the qualified package artifacts and signed evidence, verifies them before requesting feed credentials, and applies the destination feed's duplicate policy.

`v1.0.0-preview.1` selects the private-preview environment/feed. Other matching release tags select nuget.org and NuGet OIDC login. Feed credentials stay in the configured secret/environment mechanism.

Branch package checks do not run tag-only security or publication dependencies and do not publish. Do not report branch success as release qualification or public feed availability.

Package artifacts are named `nuget-packages-<run-id>-<sha>-<attempt>`. All consumers, provenance, and publication download the immutable artifact ID emitted by the package job into `nupkgs`. Artifacts are never overwritten; partial retries retain the package job's bound artifact identity. Consumer jobs do not repack product source. Preserve the artifact's run and SHA when reviewing qualification. Public-feed publication checks existing and newly published packages against qualified content while validating any NuGet repository-signature envelope separately from pre-upload archive hashes. Mismatched duplicate versions fail publication.

The package job records packed nuspec IDs/versions and pre-upload SHA-512 hashes in `packages.tsv` and a release-only `packages.lock.json`. This lock-shaped identity inventory describes qualified release archives, not a consumer restore graph or the reviewed third-party component inventory. Preserve both the immutable artifact identity and its content evidence; artifact names or successful workflow definitions alone do not establish a qualified release.

[third-party-components.json](third-party-components.json) records package components summarized in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt). [Native SMT packaging](../../docs/native-smt-packaging.md) describes layout and pinned payload checks.

The three-package graph is internal to restore and publication: `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`. The standard consumer entrypoint is one `SharpProof` reference without activation properties or `PrivateAssets="all"`; strict full verification and exact-version API/analyzer dependencies are automatic. Verifier remains a compatibility entrypoint. Release qualification must exercise the minimal main-package consumer and a packed library's downstream consumer so runtime contract dependencies remain intact. Retain explicit portable-only advisory configurations and minimum compiler-host checks separately.

Release qualification includes all six native OS/architecture combinations: Windows, macOS, and glibc Linux on x64/ARM64. Windows payloads link their C++ runtime statically and require no separate Visual C++ redistributable. Require the exact-artifact native ABI matrix to pass; compilation, import audits, and payload presence alone are insufficient. Native consumer builds require SDK 10.0.100+ and the .NET 10 runtime, with the host/filesystem and native runtime prerequisites in the [support matrix](../../docs/preview-support.md). Repository build/test/pack remains canonical-container-only. musl and other OS/CPU combinations are unsupported.

## Security qualification

See the [repository security runbook](../security/README.md) for exact-master test/coverage/image gates, immutable release tags, explicit publication approval, package SBOM/provenance verification, and image servicing. Tag only after all three exact-commit master push workflows have succeeded.

## Prepare release notes and upgrades

Before tagging, review [the changelog](../../CHANGELOG.md), [upgrade guidance](../../docs/upgrading.md), and [support matrix](../../docs/preview-support.md). Record the source SHA, exact package version, consumer qualification run, and significant diagnostic/configuration/semantic changes. Describe changes to Unknown reasons, selection, assumptions, proof outcomes, and evidence formats even if application APIs are unchanged. Do not claim publication until the destination feed confirms it.

Preview the rendered package README and check its relative links on the destination feed. Validate that each package's identity, dependency graph, XML documentation, symbols/source information, and platform payloads match the release artifacts. Keep release-only security and publication dependencies intact.

Run the [project template smoke check](../../templates/README.md#maintainer-validation) against the exact packed feed before publication. Templates obtain the version from release props rather than maintaining their own release number.

## Release SBOM and signed evidence

Public tag publication authenticates the existing three main packages and two
symbol packages, then generates `sbom.spdx.json`, `release-manifest.json`, and
`SHA256SUMS` with [New-SharpProofReleaseEvidence.ps1](../../eng/scripts/New-SharpProofReleaseEvidence.ps1).
The SPDX 2.3 document describes the shipped payload: all reviewed inventory
components (currently 18), every distinct third-party binary and its archive
entry hash, static runtime relationships, and the five complete archive hashes.
The manifest binds those bytes to the source commit and inventory digest. This
scope excludes SDKs, build tools and dependencies not redistributed in the
packages; it is not a complete source/build dependency SBOM or vulnerability scan.
Custom license references include the distributed notice bundle rather than an
invented SPDX license classification.

[Test-SharpProofReleaseEvidence.ps1](../../eng/scripts/Test-SharpProofReleaseEvidence.ps1)
regenerates the evidence from the actual archives and reviewed inventory and
rejects any changed bytes or missing component. Synthetic regression fixtures
exercise altered archives, omitted SBOM components, provenance mismatch and
missing symbol packages. These checks run in the canonical Linux amd64 container.

After consumer and security qualification, the separate least-privilege provenance job uses the SHA-pinned, official
[actions/attest](https://github.com/actions/attest) action twice for the core subjects: release workflow
provenance for the packages/SBOM/manifest, and an SPDX SBOM attestation for the
same five packages. The optional tool receives its own workflow provenance. The job retains the signed bundles and evidence as an immutable artifact; the publisher downloads that exact artifact without recreating attestations. Before requesting NuGet credentials,
[Test-SharpProofReleaseAttestations.sh](../../eng/scripts/Test-SharpProofReleaseAttestations.sh)
uses `gh attestation verify` to check each subject against its signed bundle,
the canonical repository, exact signer workflow, source commit, predicate type
and hosted-runner policy. Its fixture tests policy propagation and rejection of
CLI verification failures; it does not fabricate a successful signature check.
The real keyless signature/service exchange is exercised only by an authorized
public tag run. See [GitHub's verification reference](https://cli.github.com/manual/gh_attestation_verify).

After successful NuGet push, the job creates a draft GitHub release, uploads the
same unmodified `.nupkg`/`.snupkg` files plus SBOM, manifest, checksums and both
Sigstore bundles, then publishes the release. Existing releases/assets are not
overwritten on reruns; inspect and reconcile a partial prior run explicitly.
NuGet and GitHub publication are separate operations: a GitHub release failure
after NuGet upload does not roll back already published packages.

The optional `SharpProof.Tool` has its own pack/manifest-restore qualification
job, exact-run/source artifact and separate NuGet push. Its single archive is
kept outside the main three-package/two-symbol feed. Public releases include
its independent `TOOL-SHA256SUMS` and workflow provenance bundle; the core SBOM
does not claim to describe the tool's dependency closure. A separate reviewed
tool dependency inventory/SBOM remains future work. Its original archive is
verified against the same signer/source policy before public publication.

The private-preview tag `v1.0.0-preview.1` still uses its private feed credentials.
It generates/checks local evidence but skips public attestations and GitHub
release assets. No private-preview package is newly exposed by these steps.
The qualified optional tool is also pushed only to that private feed for this
tag, using the existing private credentials.

Consumers can download the public GitHub release assets into one directory,
run `sha256sum -c SHA256SUMS`, and verify a package's workflow provenance:

```sh
gh attestation verify SharpProof.<version>.nupkg \
  --bundle provenance.sigstore.json --repo alexyorke/SharpProof \
  --signer-workflow alexyorke/SharpProof/.github/workflows/package-consumers.yml \
  --source-digest <release-commit> --deny-self-hosted-runners
```

Use `sbom.sigstore.json` and `--predicate-type https://spdx.dev/Document/v2.3` to
verify the SBOM attestation. These attestations identify this release workflow's
claims about qualified artifact bytes. They do not establish an isolated trusted
builder, a SLSA level, or a NuGet author signature. NuGet OIDC authenticates the
publisher; it does not author-sign packages.

NuGet.org [automatically repository-signs uploaded packages](https://learn.microsoft.com/en-us/nuget/reference/signed-packages-reference).
Adding that signature can change the archive bytes, so a NuGet.org download may
have a different whole-file digest than the original GitHub release asset and
its attestation. Verify the downloaded NuGet package using `dotnet nuget verify
--all`, and use the original release assets for these pre-upload digest checks.
Do not remove `.signature.p7s`, rewrite ZIPs, or treat a stripped archive hash as
an equivalent signed-package verification. Duplicate-skipping upload still does
not prove an existing feed package has the intended bytes.

This release workflow requires unsigned original package archives; manifest generation rejects any `.signature.p7s` entry. Public-feed verification accepts only NuGet.org repository-primary signatures, uses SDK 10 signature verification with explicit SHA-256 certificate fingerprints obtained from the official repository, checks the repository service index, and compares NuGet content hashes. Wrong certificate pins, changed content, author-signed archives and repository-countersigned archives fail closed. A `trustedSigners` configuration alone does not enforce this policy; the explicit certificate pins do. Attributes assembly strong-name signing is a separate identity mechanism.

## Required account setup

The provenance job needs `attestations: write` and `id-token: write`; the publisher needs `attestations: read`, `contents: write` and `id-token: write`. Account and environment policy must allow the
pinned official action. Registry storage records are disabled, so
`artifact-metadata: write` is not required. GitHub attestations are available for
public repositories on current plans; private/internal repositories require
Enterprise Cloud and GitHub Enterprise Server is unsupported by this action.
See the [official action requirements](https://github.com/actions/attest#usage).

Maintainers must configure the NuGet trusted publisher for this repository,
workflow and `nuget.org` environment, including permission/ownership for the new
`SharpProof.Tool` package ID, set `NUGET_USER`, and maintain the private
preview environment's existing feed URL/key. Tag protection, environment
reviewers, organization action allowlists and release immutability are account
settings; changing this workflow does not configure or verify those settings.
No publication, trusted-publisher registration or signing credential setup is
performed during ordinary branch/PR validation.
