# Maintaining documentation

The [source declaration reference](generated/source-reference.md) is generated from
public package MSBuild property declarations and analyzer-visible properties,
public attribute enums (including effect and capability bits), release properties,
canonical tooling commands, and the corpus CLI usage declaration. New declarations
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
inline Markdown links, reference link definitions, and heading anchors in the
README and `docs/**/*.md`. HTTP links are excluded; no network is needed for the
documentation checks. Regression fixtures prove rejection of new undocumented
property, enum and command declarations, successful regeneration, idempotence,
preserved authored prose, missing files and broken heading anchors.

## Automatic update pull requests

[Documentation maintenance](../.github/workflows/docs-maintenance.yml) generates
and tests the reference on PRs, every default branch push, Mondays at 09:23 UTC,
and manual dispatch. GitHub schedules can be delayed. The updater runs only on
the default branch and opens or updates one PR from `codex/automation-docs-sync`.
It changes only the generated reference, refuses to replace another author's PR
or an unowned existing branch, and does nothing when output is unchanged. It
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
