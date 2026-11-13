# Repository security controls

## GitHub settings

The settings below were checked in GitHub Settings on 2026-11-13. They are
repository administration state, not controls installed by cloning this source.

The public repository is `alexyorke/SharpProof`; its default branch is `master`.
Private vulnerability reporting, secret scanning, secret push protection, Dependabot
security updates, and extended CodeQL analysis for C#, Python and GitHub Actions are enabled.
CodeQL runs from [codeql.yml](../../.github/workflows/codeql.yml) (advanced setup), whose
config excludes the vendored `eng/common` tree. Keep GitHub's default setup disabled:
uploads from the workflow are rejected while it is enabled.

The default-branch ruleset requires PRs, resolved review threads, up-to-date required
checks, and CodeQL results. CodeQL errors and medium/high/critical security findings block
merges. Force pushes and branch deletion are blocked; there are no bypass actors.
The current solo-maintainer policy requires zero independent approvals because the
only collaborator and code owner is `alexyorke`. Add a second trusted maintainer
before requiring one approval, code-owner review, and approval of the last push.

All `v*` release tags and `evidence/v*` tags are immutable. A separate creation rule
allows only `alexyorke` to create release tags. The creation exception does not bypass
the separate no-update/no-deletion ruleset.
Release immutability also protects assets once a release is published. Upload
all assets to a draft before publishing it.

Actions default to read-only tokens, require full action commit SHAs, and permit
only the used checkout, cache/restore, cache/save, upload-artifact, download-artifact,
setup-dotnet, dependency-review-action and attest-build-provenance Actions (including
its exact SHA-pinned `actions/attest` dependency), plus
`github/codeql-action/*` and `NuGet/login`. Actions cannot create or approve PRs.
All external contributors require workflow approval. Keep checkout credentials
disabled in build/test jobs. Dependabot does not automatically dismiss development
dependency alerts. Copilot automated tasks are restricted to updating their triggering
resource where GitHub supports that restriction; workflow approvals remain required.

The `nuget.org` environment retains its explicit release-tag allowlist and requires
`alexyorke` to approve publication. Admin bypass is disabled. Self-approval remains
possible for solo-maintainer releases: this is a deliberate publication decision,
not an independent review. Do not enable prevent-self-review without adding a
second reviewer. Public releases use NuGet OIDC.

## Release

[release.yml](../../.github/workflows/release.yml) runs on `v*` tag pushes and manual
dry runs. Publication requires canonical `master` ancestry and successful latest
`push` runs of both `ci.yml` and `codeql.yml` for the exact release SHA. The gate
checks workflow IDs, paths and repository identities, and runs again after approval.
The package job has only contents-read permission. It builds and tests packages,
stages an OCI image archive, and embeds that archive's manifest digest in templates.
It generates Syft/SPDX SBOMs and scans the exact image archive and package feed.
Both native smoke jobs must pass before the `nuget.org` approval gate releases
registry-write, attestation and NuGet OIDC permissions. Publication copies the
archive with `--preserve-digests`, attests the image, packages, symbols and SBOM
files, then publishes NuGet packages and an immutable GitHub release.
Manual dispatch, including dispatch against a tag, never publishes.

Tooling cache tags select candidates only. Before pulling/executing one, `ci.sh`
requires GitHub provenance from the canonical `ci.yml` workflow on `refs/heads/master`
and a hosted runner. It uses the verified OCI digest for the pull. GitHub CLI 2.102+
is required for correct identity matching; missing/old CLI, missing attestations,
or verification failures cause a source build. The master image job builds fresh
and attests its published digest before dependent CI jobs use it.

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
fail the job; the JSON report is retained for 14 days with the lane's evidence. Findings
without a published fix are retained in release scan reports for triage; the legacy
toolchain scan uses Grype's `--only-fixed` filter. Release SBOM/scanner/OCI-copy tools
are digest-pinned in [release-tools.json](release-tools.json). Review those tool versions during image servicing; add reviewed,
expiring exceptions only with a documented rationale and remediation owner.

The serviced minimum-host fixture uses SDK 9.0.321 in the same 9.0.3xx/Roslyn 4.14 feature band; consumer version eligibility remains 9.0.300. .NET 8 fixtures use the final 8.0.34 servicing packs/runtime. The explicitly pinned PowerShell 7.5 archive is installed on the Ubuntu SDK base, and the unused SDK-bundled PowerShell tool store is removed. The only scanner exception is GO-2026-6604 for the exact Linux shfmt binary (the upstream advisory affects Windows junction handling only). Review it when servicing shfmt.

MCR's PowerShell 7.5 aliases were verified to contain 7.5.0 despite newer upstream releases. The PowerShell stage therefore replaces that directory with the official 7.5.12 archive, pinned by SHA-256 in both the Dockerfile and catalog. Build-time and runtime contract checks require the exact serviced version. Update the archive URL/hash/version together during servicing.
