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
workflow approval. Keep checkout credentials disabled in build/test jobs.

The `nuget.org` environment retains its explicit release-tag allowlist and requires
`alexyorke` to approve publication. Admin bypass is disabled. Self-approval remains
possible for solo-maintainer releases: this is a deliberate publication decision,
not an independent review. Do not enable prevent-self-review without adding a
second reviewer. Public releases use NuGet OIDC.

## Release

[release.yml](../../.github/workflows/release.yml) runs on `v*` tags. It checks the tag
against the checked-in package version, packs the product and optional developer
packages in the canonical container, runs the package tests against that feed, then
pushes to NuGet.org through NuGet OIDC from the `nuget.org` environment and creates
the GitHub release with the same archives. Tag only commits whose `ci.yml` run on
`master` succeeded.

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

The `repository` job in `ci.yml` scans the actual assembled Linux amd64 tooling image on PRs,
master pushes, weekly schedules and manual dispatch. Fixed high/critical findings
fail the job; the JSON report is retained for 30 days. Findings without a published
fix remain in the report for triage. Scanner/SBOM action SHAs and Grype/Syft versions
are pinned. Review those tool versions during image servicing; add reviewed,
expiring exceptions only with a documented rationale and remediation owner.

The serviced minimum-host fixture uses SDK 9.0.319 in the same 9.0.3xx/Roslyn 4.14 feature band; consumer version eligibility remains 9.0.300. .NET 8 fixtures use 8.0.32 servicing packs/runtime. The explicitly pinned PowerShell 7.5 Ubuntu image replaces the stale Debian copy, and the unused SDK-bundled PowerShell tool store is removed. The sole scanner exception is GO-2026-6604 for the exact Linux shfmt binary: the upstream advisory affects Windows junction handling only. Review it when servicing shfmt.

MCR's PowerShell 7.5 aliases were verified to contain 7.5.0 despite newer upstream releases. The PowerShell stage therefore replaces that directory with the official 7.5.12 archive, pinned by SHA-256 in both the Dockerfile and catalog. Build-time and runtime contract checks require the exact serviced version. Update the archive URL/hash/version together during servicing.
