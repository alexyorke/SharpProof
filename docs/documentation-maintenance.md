# Maintaining documentation

The [source declaration reference](generated/source-reference.md) is generated from
public package MSBuild property declarations and analyzer-visible properties,
public attribute enums (including effect and capability bits), release properties,
canonical tooling commands and parameters, corpus CLI usage, fuzz CLI options and
validation, analyzer diagnostic descriptors, and shipped/unshipped attribute API
signatures. New declarations
in these source locations appear automatically. Expressions and conditions remain
literal: this is not an evaluated MSBuild configuration or a semantic API catalog.

## Updating the reference

Run the canonical Linux amd64 container:

```sh
docker compose run --rm tooling docs-update
cp artifacts/docs/source-reference.md docs/generated/source-reference.md
docker compose run --rm tooling docs
```

Task commands use a private source workspace. The generated file is exported to
`artifacts/docs/source-reference.md`; copying it back is an explicit source edit.
The generator writes only this generated reference. It adds no timestamps, and
repeated generation from the same declarations produces identical bytes.

`tooling pr` invokes `pr-gates`, which invokes `docs` before building. A stale or
missing reference fails the existing canonical PR gate. `docs` also checks local
inline Markdown links (including images), reference link definitions, and heading
anchors across every tracked Markdown file. Ignored artifacts and worktrees are
excluded. HTTP links are excluded; no network is needed for the
documentation checks. Regression fixtures prove rejection of new undocumented
property, enum and command declarations, successful regeneration, idempotence,
preserved authored prose, missing files and broken heading anchors.
Isolated local Git remotes and a mock GitHub CLI also exercise updater no-op,
creation, refresh, duplicate suppression, closed/merged PR reuse, ownership
refusal, file boundaries and a default branch race. These fixtures never contact
GitHub or alter the repository's branches.

## Link audit after merges

[Repository link check](../.github/workflows/links.yml) is required by the main CI
on pull requests and every push to `master` (including merges), and by release
publication. It also runs weekly and supports manual dispatch. It audits the whole
checkout, including hidden source directories, using [lychee](https://lychee.cli.rs/).
[lychee.toml](../lychee.toml) lists the Markdown, HTML, CSS, reStructuredText, and
plain-text formats scanned. Git metadata and ignored build outputs are excluded.
Program source and structured data are not parsed as documents: their strings can
contain URI identifiers, incomplete URL prefixes, and synthetic test inputs.

The audit checks Markdown links and images, reference links, HTML hyperlinks,
bare web URLs, relative and repository-root file paths, and heading/ID fragments.
URLs inside code examples are included. It checks external HTTP(S) targets live,
following redirects and retrying failures; HTTP errors and timeouts fail the job.
It does not execute JavaScript or validate application-generated navigation, and
email delivery and unsupported URL schemes are outside its scope.

Failures are grouped by source file in the Actions job summary and in the
`link-report-<commit>-<attempt>` artifact, retained for 14 days. This job only
reports: it never fixes links, commits changes, or opens pull requests. Existing
broken links, illustrative URLs, or sites blocking automated requests can cause
failures and require human review. No domains or error status codes are silently
allowlisted.

See [repository quality gates](../eng/lint/README.md) for the required Markdown
style, spelling, workflow, script, and package compatibility checks.

## Automatic update pull requests

[Documentation maintenance](../.github/workflows/docs-maintenance.yml) generates
and tests the reference on PRs, every default branch push, Mondays at 09:23 UTC,
and manual dispatch. GitHub schedules can be delayed. The updater runs only on
the default branch and opens or updates one PR from `codex/automation-docs-sync`.
It changes only the generated reference, refuses to replace another author's PR
or an unowned existing branch, and does nothing when output is unchanged. A
leftover branch from a closed or merged bot PR is reused only if its head still
matches that PR's recorded head. It
checks that the default branch has not advanced before publishing. Updates need
the usual review and required checks; merging is never automatic.

The repository Actions setting **Allow GitHub Actions to create and approve pull
requests** must be enabled for PR creation. Keep default workflow permissions at
read-only. Only the updater job requests contents, pull requests and Actions write
access. It uses the existing `GITHUB_TOKEN`; no additional credential is needed.
The updater does not approve reviews.

GitHub suppresses ordinary push and PR workflow events caused by `GITHUB_TOKEN`.
The updater explicitly dispatches `ci.yml`, `coverage.yml` (with the generating
default branch commit as `comparison_ref`) and `package-consumers.yml` against
the update branch. Their normal checks run against its current commit. The docs
updater is not among the dispatched workflows, so this cannot recurse. Repository
or organization policies may still require workflow approval or prohibit bot PRs;
such restrictions surface as workflow failures and require maintainer action.

## Authored guides still need review

Generation cannot infer feature semantics, compatibility promises, supported
language forms, examples, operational advice or whether external links work.
Review the authored guides whenever behavior changes, and link new reference
areas from the appropriate guides. Existing `DocumentationSnippetTests` compile
maintained `csharp` fences during analyzer tests. Generated declaration excerpts
use `text` fences because they are literal source fragments, not standalone
programs. Compilation alone does not verify their analysis behavior; use the
existing sample and tutorial checks for that purpose. The generated scope deliberately
does not hash every source file or rewrite prose on unrelated implementation edits.
