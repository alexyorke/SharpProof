# Private preview release receipt template

Status: **Pending - no publication or qualification is asserted by this template.**
Copy this template into the private release evidence set and replace every Pending
entry with actual evidence. Keep official binaries, symbols, detailed evidence, and
the completed receipt in the private repository/package/image destinations. Public
source and public CI validation are separate from this official private release.

## Source, workflow, destination, and access

| Field | Planned value or required record | Actual evidence/status |
| --- | --- | --- |
| Public source repository | `alexyorke/SharpProof` | Pending |
| Source commit | Approved exact master commit; full 40-character SHA | Pending |
| Package repository metadata | Private mirrored repository association at the unchanged public source commit; public source origin recorded separately | Pending metadata/source identity verification |
| Source Link | Inspect qualified PDB URLs and verify the approved source commit; do not infer origin from package metadata | Pending URL/commit inspection |
| Private workflow repository | `alexyorke/SharpProof-preview` | Pending |
| Candidate workflow commit | Full 40-character SHA, equal to qualification's actual `github.workflow_sha` | Pending |
| Qualification workflow | Manual main-bound `.github/workflows/private-preview.yml`; no signing or publication | Pending run URL, ID, attempt, and input record |
| Promotion workflow commit | Full 40-character SHA, equal to promotion's actual `github.workflow_sha` | Pending |
| Promotion workflow | Manual main-bound `.github/workflows/private-preview-promote.yml`; `stage-registry` then `finalize` | Pending phase run URLs, IDs, attempts, and input records |
| Receipt intake workflow | Owner manual main-bound `.github/workflows/private-preview-receipts.yml`; `receipt_directory=private-preview-receipts/<candidateArtifactId>` | Pending run/artifact ID, API digest, and promotion-SHA match |
| Official package version/tag | `1.0.0-preview.1` / `v1.0.0-preview.1` | Pending |
| Public CI validation version | `0.0.0-ci.<run>.<attempt>`; never the official preview identity | Pending |
| Private NuGet destination | `https://nuget.pkg.github.com/alexyorke/index.json` | Pending upload and read-back evidence |
| Private image destination | `ghcr.io/alexyorke/sharpproof-preview@sha256:<digest>` | Pending manifest digest and read-back evidence |
| Initial access policy | Owner only for repository, packages, images, symbols, and evidence | Pending independent access/visibility check |
| Protected environment | `private-preview-release` | Pending required-reviewer/protection and run evidence |

The source SHA identifies compiled public source. The candidate workflow SHA identifies
qualification; the promotion workflow SHA identifies the separate signing/publication
orchestration. Preserve all three identities. Record the actual source/tag relationship
and reject a moving ref, workflow mismatch, unapproved source, or an official preview
identity produced by public CI.

Record actual account, environment, key-configuration, and access observations only
in private evidence. Environment creation alone does not establish the required
protection policy, a signature, or publication approval.

## Exact artifacts and signatures

| Subject | Required evidence | Actual result |
| --- | --- | --- |
| Frozen candidate artifact | `private-preview-candidate-<run>-<attempt>`; immutable numeric ID, API SHA-256 digest, successful manual-main qualification run | Pending |
| Unsigned candidate manifest | Exact `release-manifest.json` hash, source/workflow bindings, and `SHA256SUMS`; not an offline release signature | Pending |
| Core packages | Exact `SharpProof`, `SharpProof.Attributes`, and `SharpProof.Verifier` archives/version/source metadata | Pending |
| Optional packages | Exact `SharpProof.Tool`, `SharpProof.CodeFixes`, and `SharpProof.Templates` archives | Pending |
| Symbol packages | Attributes/Verifier `.snupkg` files and exact PE/PDB/source identity checks | Pending |
| Tooling symbols | Embedded PDB/source checks and any extracted matching PDB inventory; retain Tool `.snupkg` if produced | Pending |
| Candidate image | `consumer-image.tar`, `consumer-image.json`, `image-receipt.json`, manifest digest, platform, and package version/source identity | Pending |
| Editor acceptance artifact | `private-preview-acceptance-<run>-<attempt>` immutable ID/API digest; `editor-acceptance.json` plus hashed `evidence/`, bound to candidate manifest/artifact/run and six package hashes | Pending |
| Owner-only access evidence | `access-acceptance.json` plus hashed `evidence/` in the combined acceptance artifact; all 10 subjects and all seven granular checks per subject | Pending |
| Receipt owner review | Actual `reviewedBy` owner and `reviewedAtUtc`; schema version 1 and exact source/workflow/artifact/hash bindings | Pending |
| Inventories | Package manifests, archive checksums, SBOM scope, third-party notices, dependency/reproducibility results | Pending |
| Signed release manifest | Same frozen candidate manifest; binds source/candidate-workflow SHAs, package/symbol/SBOM/checksum subjects and exact image digest | Pending |
| Detached manifest signature | RSA 3072-bit / SHA-256; pinned public key and SPKI fingerprint from `eng/release/private-preview/` | Pending verifier output and fingerprint |
| NuGet archive signing status | Record actual archive signature state separately from the detached release-manifest signature | Pending |
| Retained private assets | Original archives, symbols, manifests, SBOM, checksums, signature, receipt, and qualification reports | Pending private asset IDs and hashes |

Keep signing material in the configured `PRIVATE_PREVIEW_SIGNING_KEY` secret of the
protected environment. The receipt contains the public verification identity and
verification result, never the private key or credentials. This release uses offline
manifest signatures without a public transparency-log submission. A signed manifest
is not a NuGet author signature, an Authenticode signature, or an assembly strong name.
Qualification retains unsigned evidence only. Promotion must consume the exact
candidate bytes and never rebuild or repack them. The promotion run records its own
workflow SHA separately from the frozen manifest's candidate-workflow SHA.

Retain matching `.snupkg` files privately even when the package feed does not provide
a usable symbol-server path. Record how a consumer obtains the matching symbols and
verify that procedure; do not infer symbol availability from package upload success.
Keep SBOM scope explicit: a core payload inventory does not establish the full optional
tool/editor/template/image dependency closure.

## Qualification and publication observations

| Required observation | Actual evidence/status |
| --- | --- |
| Exact source/candidate-workflow/promotion-workflow commits, pinned toolchain, and clean source provenance | Pending |
| Latest allowed exact-master-SHA CI, coverage, toolchain-security, package-consumers, docs-maintenance, and nightly gates | Pending |
| Complete authenticated coverage gate and unchanged acceptance floors | Pending |
| Two independent exact-source builds and required archive/payload comparisons | Pending |
| Generated workspace: pinned tool, optional editor package, private feed, portable/strict configuration | Pending |
| Init preview/apply, all output formats, baseline report/enforce negative controls | Pending |
| Relative/workspace regression export, relocation, fail-before/pass-after, and fresh strict proof | Pending |
| Starter and workspace Compose receipts: exact image, restore/tool restore/build/doctor/cleanup, private volume isolation, and credential handling | Pending |
| Visual Studio stable, .NET 10-capable, Windows x64 interactive acceptance | Pending |
| VS Code stable with C# extension, Windows x64 interactive acceptance | Pending |
| Rider stable, Windows x64 interactive acceptance | Pending |
| All 26 required scenarios per editor, including UI package installation and undo/source restoration | Pending |
| Missing-baseline, wrong SHA, wrong registry/feed, altered subject, signature failure, and duplicate-conflict controls | Pending |
| Private repository/package/image visibility and initial owner-only access | Pending |
| `stage-registry`: editor receipt and protection checks, frozen manifest signature, exact private feed/image upload, no release tag | Pending |
| `finalize`: same candidate, editor/access evidence, fresh private-feed restore and pull by exact published digest | Pending |
| Release tag/assets created only after finalization checks; original package/symbol bytes retained | Pending |
| Private release assets and receipt retention, with immutable run/artifact identities | Pending |

Use the [editor acceptance runbook](editor-acceptance.md) for all three real editors.
The [promotion receipt contract](../eng/scripts/SharpProof.PrivatePreviewPromotionReceipts.psm1)
defines the actual JSON fields and scenario/access inventories. Finalization's
`editor_acceptance_artifact_id` identifies one intake artifact containing both editor
and access receipts; there is no separate access-artifact input. Access evidence covers
visibility, direct users, teams, inheritance, Actions repository grants, actual anonymous
denial, and actual authenticated non-owner denial for every subject. Private metadata
alone does not establish granular access or a denial test.
Fixture controls, workflow definitions, local dry runs, and a private visibility setting
are separate evidence. None establishes a successful official build, valid signature,
publication, or consumer restore by itself.
An interrupted UI attempt does not establish interactive acceptance; keep its actual
observations private and leave incomplete qualification Pending.

Record partial publication explicitly: which exact subjects exist, their hashes/digests,
which checks failed, and the reviewed recovery action. Do not overwrite conflicting
versions or replace the signed subjects silently. Final outcome, reviewer, decision
time, and links to the completed evidence are all **Pending**.
