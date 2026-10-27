# Repository quality gates

The main CI workflow runs the combined repository quality and link checks on pull
requests and pushes to master. A failed, skipped, or canceled quality job fails the
existing build-and-test gate. Release publication also requires this job. Quality
checks and the standalone link audit have weekly schedules and manual dispatch.
Findings are reported; CI never applies fixes.

## Run locally

Run the same complete check set used by CI with one command:

```sh
docker compose run --rm --build lint
```

For host Git worktrees or older Compose releases, use the wrapper. It builds the
images explicitly and mounts resolved worktree Git metadata read-only:

```powershell
pwsh -File eng/scripts/Invoke-SharpProof.ps1 lint
```

Docker Compose builds the canonical toolchain and its lint layer in dependency order,
so no separate setup commands or prebuilt local images are needed. This requires
Docker Compose 2.17 or newer with the wrapper. The direct `run --build` form needs
a recent Compose release with service build context fixes (verified with 5.6.0).
CI builds explicitly before running to support older runner versions.
Repeated runs reuse Docker's build cache.

The lint service inherits the canonical Linux amd64 toolchain, container contract,
resource limits, and private task workspace. The ordinary toolchain does not install
the extra tools. Image digests, npm versions and lockfile, module version, and
ApiCompat version are pinned in this directory.

## Checks and reports

| Check | Scope and failure policy |
| --- | --- |
| actionlint | All GitHub Actions workflows; all reported errors fail. |
| zizmor | Offline audits of workflows, composite actions, and Dependabot; findings fail. |
| PSScriptAnalyzer | All tracked and unignored PowerShell eng/scripts/modules; every diagnostic from the selected correctness/security rules fails. |
| ShellCheck | All tracked and unignored shell scripts; all reported findings fail. |
| markdownlint | Authored Markdown; enabled rule violations fail. |
| CSpell | Authored Markdown prose; unknown words fail. |
| Links | External URLs, local paths, and fragments; broken or empty audits fail. |
| Tool dependencies | Live npm audit of the pinned lint tools; low or higher vulnerabilities fail. |
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

The Markdown runner uses the pinned markdownlint library and Git file inventory
directly. This avoids markdownlint-cli2's unpatched globbing dependency while
preserving the same lint rules. It checks authored Markdown and rejects empty input.
The KaTeX override pins a patched transitive version used by math parsing; regression
fixtures exercise inline and block math as well as Markdown error detection.

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

The combined command checks links using `lychee.toml` and saves its detailed report
to `artifacts/lint/links.md`. The standalone link workflow runs the same script and
keeps its own artifact. Both fail on broken links and empty input and exercise
missing-file and missing-anchor fixtures on successful runs.
One historical Debian license reference is mapped to an archive mirror of the
same message, verified by its Message-ID, because the original host times out in
CI. The exact URL mapping is recorded in `lychee.toml`; the mirror is checked.
