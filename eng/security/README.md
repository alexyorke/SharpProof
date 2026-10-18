# Repository security controls

## GitHub settings

The public repository is `alexyorke/SharpProof`; its default branch is `master`.
Private vulnerability reporting, secret scanning, secret push protection, Dependabot
security updates, and extended CodeQL analysis for C# and GitHub Actions are enabled.

The default-branch ruleset requires PRs, resolved review threads, up-to-date required
checks, and CodeQL results. CodeQL errors and high/critical security findings block
merges. Force pushes and branch deletion are blocked; there are no bypass actors.
The current solo-maintainer policy requires zero independent approvals because the
only collaborator and code owner is `alexyorke`. Add a second trusted maintainer
before requiring one approval, code-owner review, and approval of the last push.

All `v*` release tags and `evidence/v*` tags are immutable. A separate creation rule
allows only `alexyorke` to create release tags. The creation exception does not bypass
the separate no-update/no-deletion ruleset.

Actions default to read-only tokens, require full action commit SHAs, and permit
only `actions/*`, `github/codeql-action`, `NuGet/login`, `lycheeverse/lychee-action`,
`anchore/scan-action`, and `anchore/sbom-action`. All external contributors require
workflow approval. Keep checkout credentials disabled in build/test jobs. The docs
updater retains checkout credentials and explicit write permissions because it
pushes its bot branch and creates a PR. It explicitly dispatches the image-security workflow alongside the existing canonical workflows. GitHub may require a maintainer to approve bot-created default CodeQL runs; approve the run rather than bypassing the required analysis. The repository's shared create/approve-PR
Actions switch remains enabled for this updater; it must not approve or auto-merge PRs.

The `nuget.org` environment retains its explicit release-tag allowlist and requires
`alexyorke` to approve publication. Admin bypass is disabled. Self-approval remains
possible for solo-maintainer releases: this is a deliberate publication decision,
not an independent review. Do not enable prevent-self-review without adding a
second reviewer. Public releases use NuGet OIDC. Private preview credentials remain
scoped to `nuget.private-preview` and its explicit preview tag.

## Release qualification and provenance

Tag only after the exact commit is on `master` and its latest push runs for `ci.yml`,
`coverage.yml`, and `toolchain-security.yml` have succeeded. The tag workflow checks
all three workflow identities, branch, event, SHA and conclusion before attesting
or publishing. Missing, pending, cancelled and failed runs reject the release.
Tag-only security and every native/portable exact-artifact consumer must also pass.

The package job generates a five-column `packages.tsv`: package ID, version,
archive filename, original archive SHA-512, and NuGet's signing-independent content
hash. Its release-only `packages.lock.json` is an identity inventory for these
archives, not a consumer restore graph. The optional `SharpProof.Tool` has a
separate qualified feed, manifest and immutable artifact identity.

Consumers, provenance and publication download the immutable artifact IDs emitted
by the producing jobs. Run/commit/attempt artifact names are never overwritten;
partial retries retain the exact bound IDs rather than selecting newer artifacts
by name. The tool's qualification is separate from the core package graph.

After consumer and security qualification, a separate least-privilege provenance
job generates the reviewed SPDX 2.3 shipped-payload SBOM, release manifest and
checksums from the exact original archives. This inventory describes reviewed
redistributed components and payload hashes; it is not a scanner-generated
complete dependency graph or vulnerability scan. The workflow also retains a supplemental scanner-generated package-graph SBOM; it does not replace the reviewed shipped-payload inventory or extend the core SBOM to tool dependencies.
The tool has separate workflow
provenance and checksums; its dependency closure is not included in the core SBOM.
The job signs core workflow/SBOM and tool provenance subjects and retains the
bundles and evidence as `signed-release-evidence-<sha>-<attempt>`, downloaded through the provenance job's immutable evidence artifact ID. It needs `attestations: write`
and `id-token: write`, without package-publishing credentials.

The publisher downloads those exact package and evidence artifacts, verifies
checksums and signed bundles against the canonical repository, workflow, tag,
source SHA and hosted-runner policy, then requests NuGet credentials. It needs
`attestations: read` rather than permission to create replacement attestations.
Original archive SHA-256/SHA-512 hashes and attestations authenticate the original
release assets; they do not assert that NuGet's repository-signature envelope
leaves the ZIP bytes unchanged.

Public publication checks existing versions before pushing and downloads every
published `.nupkg` afterward. It validates the NuGet repository signature and
compares signing-independent content with the qualified manifest. A different
payload or an invalid repository signature fails publication; a duplicate push
response alone is insufficient. NuGet's public download API does not expose
`.snupkg` archives for this comparison; symbol packages retain their original
checksums and provenance. The private preview feed has no assumed public download
API, skips the public-feed checker, and rejects duplicate pushes instead of
silently skipping them. Neither a package comparison nor a fixture run establishes
that a public release or account configuration has succeeded.

Verify the original GitHub release asset against its downloaded bundle:

```sh
gh attestation verify SharpProof.<version>.nupkg \
  --bundle provenance.sigstore.json --repo alexyorke/SharpProof \
  --signer-workflow alexyorke/SharpProof/.github/workflows/package-consumers.yml \
  --source-ref refs/tags/v<version> --source-digest <commit-sha> \
  --deny-self-hosted-runners
```

For a package downloaded from NuGet.org, validate its repository signature through
NuGet's supported signature tooling and compare signing-independent content using
the qualified release manifest. Do not strip signatures or rewrite ZIPs to force
a match with the original release asset's whole-file hash. See the
[release process](../release/README.md) for bundle subjects and account setup.

This release workflow requires unsigned original package archives; manifest generation rejects any `.signature.p7s` entry. Public-feed verification accepts only NuGet.org repository-primary signatures, uses SDK 10 signature verification with explicit SHA-256 certificate fingerprints obtained from the official repository, checks the repository service index, and compares NuGet content hashes. Wrong certificate pins, changed content, author-signed archives and repository-countersigned archives fail closed. A `trustedSigners` configuration alone does not enforce this policy; the explicit certificate pins do. Attributes assembly strong-name signing is a separate identity mechanism.

Exercise the release rejection paths in the canonical container:

```sh
docker compose run --rm tooling dev -lc \
  'pwsh -NoLogo -NoProfile -File scripts/Test-SharpProofReleaseSecurity.ps1'
```

## Toolchain servicing

Dependabot proposes weekly Docker image servicing updates alongside existing NuGet
and action updates. Keep SDK/runtime/PowerShell minor-version compatibility changes
intentional. An image digest change alone is insufficient: synchronize the matching
image reference/digest in `eng/container/toolchain.json`, SDK version in `global.json`,
and any concrete SDK/runtime/reference-pack COPY paths in the Dockerfile. Preserve
the minimum-host fixtures unless an intentional compatibility migration changes them.

For each refresh, build the canonical image and run its contract checks, release
security regressions, repository security profile, and ordinary PR/package consumer
gates. Inspect the image vulnerability artifact before merging. Never disable the
contract checks or vulnerability gate to accept a refresh.

`toolchain-security.yml` scans the actual assembled Linux amd64 tooling image on PRs,
master pushes, weekly schedules and manual dispatch. Fixed high/critical findings
fail the job; the JSON report is retained for 30 days. Findings without a published
fix remain in the report for triage. Scanner/SBOM action SHAs and Grype/Syft versions
are pinned. Review those tool versions during image servicing; add reviewed,
expiring exceptions only with a documented rationale and remediation owner.

The serviced minimum-host fixture uses SDK 9.0.319 in the same 9.0.3xx/Roslyn 4.14 feature band; consumer version eligibility remains 9.0.300. .NET 8 fixtures use 8.0.32 servicing packs/runtime. The explicitly pinned PowerShell 7.5 Ubuntu image replaces the stale Debian copy, and the unused SDK-bundled PowerShell tool store is removed. The sole scanner exception is GO-2026-6604 for the exact Linux shfmt binary: the upstream advisory affects Windows junction handling only. Review it when servicing shfmt.

MCR's PowerShell 7.5 aliases were verified to contain 7.5.0 despite newer upstream releases. The PowerShell stage therefore replaces that directory with the official 7.5.12 archive, pinned by SHA-256 in both the Dockerfile and catalog. Build-time and runtime contract checks require the exact serviced version. Update the archive URL/hash/version together during servicing.
