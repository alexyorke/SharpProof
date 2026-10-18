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
updater uses explicit write permissions and authenticates Git with its job token
before pushing its bot branch and creating a PR. It explicitly dispatches the
image-security workflow alongside the existing canonical workflows. GitHub may
require a maintainer to approve bot-created default CodeQL runs; approve the run
rather than bypassing the required analysis. The repository's shared create/approve-PR
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

The package job generates `packages.tsv` and a release-only `packages.lock.json` identity inventory from each packed nuspec and its SHA-512. The inventory describes the release artifacts, not a consumer restore. The SBOM scans unpacked package contents together with that inventory and must contain every qualified package ID/version.
All consumers, provenance and publication use the immutable artifact ID emitted by the package job; run/commit/attempt artifact names are never overwritten. After qualification, a separate least-privilege job creates an SPDX package-graph
SBOM and signed provenance/SBOM attestations for the exact downloaded artifacts.
The publish job verifies provenance against this repository, workflow, tag and SHA
before requesting NuGet credentials. `release-evidence-<sha>-<attempt>` retains the
SBOM and SHA-256 checksums; attestations remain available through GitHub's API.

Public publication checks any existing package bytes before pushing, rejects any
mismatch, and downloads every published `.nupkg` afterward to verify its SHA-512.
A duplicate response is accepted only if the feed returns the qualified bytes.
The post-publication check retries feed propagation for up to eleven ten-second
waits per package. Authentication, transport and non-404 HTTP failures fail closed.
NuGet's public download API does not expose `.snupkg` archives for this comparison;
symbol packages are covered by local checksums and provenance. The private preview
feed has no assumed public download API, does not use the public byte checker, and rejects duplicate pushes rather than silently skipping them.

Verify a downloaded package with:

```sh
gh attestation verify SharpProof.Verifier.<version>.nupkg \
  --repo alexyorke/SharpProof \
  --signer-workflow alexyorke/SharpProof/.github/workflows/package-consumers.yml \
  --source-ref refs/tags/v<version> --source-digest <commit-sha>
```

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
