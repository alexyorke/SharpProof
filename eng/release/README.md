# Release process

[package-consumers.yml](../../.github/workflows/package-consumers.yml) owns public CI
package qualification and the future public release path. Official private-preview
publication uses a separate private workflow and receipt.

## Official private preview

The initial official `1.0.0-preview.1` release is planned for the private
`alexyorke/SharpProof-preview` repository, its GitHub NuGet feed at
`https://nuget.pkg.github.com/alexyorke/index.json`, and
`ghcr.io/alexyorke/sharpproof-preview`. Source stays public in `alexyorke/SharpProof`.
Official archives, symbols, image, detailed evidence, and completed release receipts
stay private, with owner-only initial access. Publication and access verification are
pending; these destinations are policy, not evidence that an upload or account setup
has succeeded.

The manual, main-bound `.github/workflows/private-preview.yml` entrypoint only
qualifies a candidate. It accepts `source_commit` and `workflow_commit`, both exact
40-character SHAs. The source must be an approved public master commit; the workflow
commit must equal the executing `github.workflow_sha` in the private repository.
The source commit identifies unchanged public code mirrored into the private
repository. Private package `RepositoryUrl` associates the package with that mirror;
inspect the qualified PDBs to record their actual Source Link URLs and approved
commit. Do not infer Source Link origin from package metadata. The manifest records
the public source origin separately from the private orchestration identity.

The pinned `owner-manual-main` policy makes the owner's manual promotion dispatch
the approval boundary. Both `GITHUB_ACTOR` and `GITHUB_TRIGGERING_ACTOR` must be
`alexyorke`, with the exact approved workflow SHA on private `main`. The private
`private-preview-release` environment and its explicit main-only deployment policy
remain required. A separate reviewer rule is optional; if configured, it must contain
exactly the owner. Without that rule, GitHub provides no separate review pause before
secret access. Real editor acceptance, exact candidate/source/workflow bindings,
owner-only access evidence, signature checks, and fresh private consumer checks still
gate the appropriate promotion phase.

1. Run qualification for the exact source and workflow/toolchain. Required automated
   gates cover the six package IDs, symbols, native/package consumers, complete
   coverage, security, reproducibility, generated workspace/CI behavior, and both
   generated Compose consumer layouts. Source approval requires the latest allowed
   exact-master-SHA runs of `ci.yml`, `coverage.yml`, `toolchain-security.yml`,
   `package-consumers.yml`, `docs-maintenance.yml`, and `nightly.yml` to succeed.
   The first five require push runs; nightly accepts schedule/manual runs. A newer
   pending or failed allowed run blocks approval even if an older run passed.
   Retain the immutable
   `private-preview-candidate-<run>-<attempt>` artifact and its numeric ID/API digest.
   It contains the original packages/symbols, SBOM, `SHA256SUMS`, unsigned
   `release-manifest.json`, image archive/metadata, and OCI receipt. Qualification
   does not sign, publish registries, or create a release tag.
2. Use those exact candidate archives as an isolated local feed for
   [all three real editor sessions](../../docs/editor-acceptance.md). Verify the
   artifact digest and unsigned manifest/package hashes first. Retain the actual
   sessions as `editor-acceptance.json` with hashed attachments under `evidence/`.
   Commit these only in the private mirror at
   `private-preview-receipts/<candidateArtifactId>` and manually dispatch
   `.github/workflows/private-preview-receipts.yml` on main with `receipt_directory`
   set to that path. Its immutable `private-preview-acceptance-<run>-<attempt>`
   artifact must originate from the current promotion workflow SHA, separately from
   the frozen candidate workflow SHA. All editor observations remain Pending until
   performed; use the [receipt contract](../../eng/scripts/SharpProof.PrivatePreviewPromotionReceipts.psm1)
   rather than synthetic fixtures to describe actual acceptance.
3. Dispatch the separate main-bound `.github/workflows/private-preview-promote.yml`
   with `phase=stage-registry`, `candidate_artifact_id`,
   `editor_acceptance_artifact_id`, `source_commit`, `candidate_workflow_commit`, and
   `workflow_commit` (the executing promotion workflow SHA). Promotion verifies the
   original successful manual-main qualification run and immutable candidate bytes;
   it never repacks or rebuilds them. The owner-manual-main approval and private
   environment/main-only publication policy must pass before signing or upload.
   Sign the frozen manifest with offline RSA 3072-bit/SHA-256 using
   `PRIVATE_PREVIEW_SIGNING_KEY` and verify against the pinned public key/SPKI
   fingerprint in `eng/release/private-preview/`. This phase publishes the exact
   packages and image to private registries, but creates no tag or GitHub release.
4. Independently inspect actual granular access for the repository, all packages,
   image, symbols, and evidence. Retain `access-acceptance.json` and its attachments
   beside `editor-acceptance.json`, then rerun the receipt intake workflow. Dispatch
   `phase=finalize` for the same candidate; `editor_acceptance_artifact_id` now identifies
   that combined editor/access artifact. There is no separate access-artifact input.
   Finalization requires fresh consumer
   restore from the private feed and image pull by the published digest before
   creating `v1.0.0-preview.1` and its private release assets. A successful candidate
   or registry-stage run does not establish those read-back or finalization outcomes.
   A finalization rerun preserves the original authenticated `fresh-consumer.json`
   release asset while rerunning the remote consumer checks; its new execution audit
   remains in that run's private Actions artifact.
5. Complete the [private release receipt](../../docs/private-preview-release-receipt.md)
   with candidate/editor/access/promotion run and immutable artifact IDs, hashes,
   signature verification, exact read-back results, and any partial-publication
   recovery decision. Keep credentials and signing material in secret references.

The candidate manifest is unsigned qualification evidence. The later detached
signature authenticates the same frozen release subjects; no public transparency-log
submission is part of this private path. Source, candidate-workflow, and
promotion-workflow SHAs are separate identities and must not be substituted for one
another.

The detached manifest signature is separate from NuGet author/repository signatures,
Authenticode, and the Attributes assembly strong name. Record actual archive signing
state rather than reporting public NuGet.org signature policy as private-feed evidence.
Keep `.snupkg` files and matching PDBs available privately; do not assume a private
symbol server or infer symbol availability from package upload success. Keep the SBOM
scope explicit for core, optional-tool/editor/template, and image subjects.

Public CI uses distinct `0.0.0-ci.<run>.<attempt>` validation versions through
`SHARPPROOF_CI_PACKAGE_VERSION`; those binaries/evidence do not become official
`1.0.0-preview.1` artifacts. Public source validation neither publishes the official
preview nor replaces the private source/workflow-bound release gates. All official
publication, signatures, editor sessions, and receipt fields remain pending until
actual evidence is recorded.

## Public CI and future public releases

1. The package job runs `tooling pack -Configuration Release` in the canonical Linux amd64 container and uploads the packed three-package graph.
2. Linux container and native Windows, macOS, and glibc Linux x64/ARM64 verifier consumers download those artifacts and run real proof and payload-integrity checks.
3. Branches, pull requests, and tags run portable consumers on Linux, Windows, macOS ARM64, and macOS Intel against the exact packed artifacts. Tags additionally run exact-SHA security.
4. A `v*` tag must exactly match `SharpProofPackageVersion` in [eng/Versions.props](../../eng/Versions.props).
5. Publication depends on all required tag jobs and runs only in the canonical repository. It downloads the qualified package artifacts and signed evidence, verifies them before requesting feed credentials, and applies the destination feed's duplicate policy.

The official `v1.0.0-preview.1` identity belongs to the private path above. Future
approved public versions use matching release tags, nuget.org, and NuGet OIDC login;
they require their own version/tag decision and public release qualification. Feed
credentials stay in the configured secret/environment mechanism.

Branch package checks do not run tag-only security or publication dependencies and do not publish. Do not report branch success as release qualification or public feed availability.

Package artifacts are named `nuget-packages-<run-id>-<sha>-<attempt>`. All consumers, provenance, and publication download the immutable artifact ID emitted by the package job into `nupkgs`. Artifacts are never overwritten; partial retries retain the package job's bound artifact identity. Consumer jobs do not repack product source. Preserve the artifact's run and SHA when reviewing qualification. Public-feed publication checks existing and newly published packages against qualified content while validating any NuGet repository-signature envelope separately from pre-upload archive hashes. Mismatched duplicate versions fail publication.

The package job records packed nuspec IDs/versions and pre-upload SHA-512 hashes in `packages.tsv` and a release-only `packages.lock.json`. This lock-shaped identity inventory describes qualified release archives, not a consumer restore graph or the reviewed third-party component inventory. Preserve both the immutable artifact identity and its content evidence; artifact names or successful workflow definitions alone do not establish a qualified release.

[third-party-components.json](third-party-components.json) records package components summarized in [THIRD-PARTY-NOTICES.txt](../../THIRD-PARTY-NOTICES.txt). [Native SMT packaging](../../docs/native-smt-packaging.md) describes layout and pinned payload checks.

The three-package graph is internal to restore and publication: `SharpProof -> SharpProof.Verifier -> SharpProof.Attributes`. The standard consumer entrypoint is one `SharpProof` reference without activation properties or `PrivateAssets="all"`; strict full verification and exact-version API/analyzer dependencies are automatic. Verifier remains a compatibility entrypoint. Release qualification must exercise the minimal main-package consumer and a packed library's downstream consumer so runtime contract dependencies remain intact. Retain explicit portable-only advisory configurations and minimum compiler-host checks separately.

Release qualification includes all six native OS/architecture combinations: Windows, macOS, and glibc Linux on x64/ARM64. Windows payloads link their C++ runtime statically and require no separate Visual C++ redistributable. Require the exact-artifact native ABI matrix to pass; compilation, import audits, and payload presence alone are insufficient. Native consumer builds require SDK 10.0.100+ and the .NET 10 runtime, with the host/filesystem and native runtime prerequisites in the [support matrix](../../docs/preview-support.md). Repository build/test/pack remains canonical-container-only. musl and other OS/CPU combinations are unsupported.

## Debugging packages and packing

All first-party assemblies use deterministic builds and portable PDBs with
commit-specific Source Link. The two core `.snupkg` files contain matching PDBs
for core-package assemblies; optional Tool/editor payloads retain their embedded
PDBs and separate identity checks. Every PDB also embeds its source files,
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

The official private-preview archives, symbols, receipt/signature, and image use the
separate private workflow above. Its offline signature policy is distinct from the
public Sigstore/NuGet.org policy in this section; public CI artifacts are validation
outputs with different versions, not an alternative official preview download.

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
`SharpProof.Tool` package ID, and set `NUGET_USER`. The private-preview promotion path
has its own protected environment, private registry credentials, and offline signing
policy; public-feed configuration does not authorize that path. Tag protection, environment
reviewers, organization action allowlists and release immutability are account
settings; changing this workflow does not configure or verify those settings.
No publication, trusted-publisher registration or signing credential setup is
performed during ordinary branch/PR validation.
