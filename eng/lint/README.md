# Repository quality gates

The main CI workflow requires repository quality and link checks on pull requests
and pushes to master. A failed, skipped, or canceled required job fails the existing
build-and-test gate. Release publication also requires both workflows. Each has a
weekly schedule and manual dispatch. Findings are reported; CI never applies fixes.

## Run locally

Build the canonical toolchain, then its lint layer:

```sh
docker compose build tooling
docker compose build lint
docker compose run --rm lint
```

For host Git worktrees, the wrapper mounts the resolved Git metadata read-only:

```powershell
pwsh -File scripts/Invoke-SharpProof.ps1 lint
```

The lint service inherits the canonical Linux amd64 toolchain, container contract,
resource limits, and private task workspace. The ordinary toolchain does not install
the extra tools. Image digests, npm versions and lockfile, module version, and
ApiCompat version are pinned in this directory.

## Checks and reports

| Check | Scope and failure policy |
| --- | --- |
| actionlint | All GitHub Actions workflows; all reported errors fail. |
| zizmor | Offline audits of workflows, composite actions, and Dependabot; findings fail. |
| PSScriptAnalyzer | All tracked and unignored PowerShell scripts/modules; every diagnostic from the selected correctness/security rules fails. |
| ShellCheck | All tracked and unignored shell scripts; all reported findings fail. |
| markdownlint-cli2 | Authored Markdown; enabled rule violations fail. |
| CSpell | Authored Markdown prose; unknown words fail. |
| ApiCompat | Public Attributes package against the pinned baseline; incompatible APIs fail. |
| Gate regressions | Deliberately invalid fixtures must be rejected by each checker. |

Logs, structured results, and a summary are written to `artifacts/lint` and uploaded
even after a check fails. The runner evaluates every check before returning failure.
Tool startup failures and missing inputs also fail. No findings baseline is used.

PowerShell rules focus on correctness and security; naming and cosmetic rules are
not enabled. Markdown allows long lines, inline HTML, bare URLs, repeated headings
in different sections, and documents without a leading title. Generated references,
build outputs, and imported third-party documentation are excluded from Markdown
style and spelling checks. CSpell also excludes fenced/inline code and URLs; its
dictionary contains reviewed project and compiler terminology.

One zizmor rule is disabled with an inline explanation in `zizmor.yml`:
actionlint 1.7.12 rejects the newer self-repository syntax suggested by zizmor.
Conventional local references remain in use until both tools support that syntax.
Other offline audits remain enabled, including credential persistence and injection.

## API baseline

There is no public NuGet release to use as a baseline yet. `api-baseline.txt` pins
an immutable Git commit. The compatibility script builds its Attributes package in
a separate temporary clone, builds the current package with SDK package validation,
then compares the two with ApiCompat, including parameter names. Missing baseline
objects, restore/build errors, missing packages, and API removals all fail.

Attributes is the public `lib/` assembly. Analyzer and verifier payloads use custom
package layouts; their existing package layout and consumer checks remain required.
This gate does not claim to verify behavioral compatibility. Advance the baseline
only when intentionally accepting a compatibility change, with normal review.

## Links

The separate link workflow checks external URLs, local paths, and fragments using
`lychee.toml`. It fails on broken links and empty input, keeps a report even on
failure, and exercises missing-file and missing-anchor fixtures on successful runs.
See [documentation maintenance](../../docs/documentation-maintenance.md) for scope.
