#!/usr/bin/env bash
set -euo pipefail
branch=codex/automation-docs-sync
git fetch origin "$DEFAULT_BRANCH"
# Generation and publication must describe the same default-branch commit.
if [[ "$(git rev-parse FETCH_HEAD)" != "$GITHUB_SHA" ]]; then
  echo 'Default branch advanced; the next push run will regenerate its reference.'
  exit 0
fi
mkdir -p docs/generated
cp artifacts/docs/source-reference.md docs/generated/source-reference.md
if git diff --quiet -- docs/generated/source-reference.md && git ls-files --error-unmatch docs/generated/source-reference.md >/dev/null 2>&1; then
  echo 'Reference is current; no branch or PR changes.'
  exit 0
fi
existing=$(gh pr list --head "$branch" --base "$DEFAULT_BRANCH" --state open --json number --jq '.[0].number // empty')
if [[ -n "$existing" ]]; then
  author=$(gh pr view "$existing" --json author --jq '.author.login')
  if [[ "$author" != 'app/github-actions' && "$author" != 'github-actions[bot]' ]]; then
    echo 'Updater branch belongs to another author; refusing to replace it.' >&2
    exit 1
  fi
  if [[ "$(gh pr view "$existing" --json files --jq '[.files[].path] == ["docs/generated/source-reference.md"]')" != 'true' ]]; then
    echo 'Updater PR includes other files; refusing to replace it.' >&2
    exit 1
  fi
fi
if git ls-remote --exit-code --heads origin "$branch" >/dev/null; then
  git fetch origin "$branch:refs/remotes/origin/$branch"
  if [[ -z "$existing" ]]; then
    prior=$(gh pr list --head "$branch" --base "$DEFAULT_BRANCH" --state all --limit 1 --json number --jq '.[0].number // empty')
    if [[ -z "$prior" ]]; then
      echo 'Updater branch has no bot PR history; refusing to replace unowned work.' >&2
      exit 1
    fi
    author=$(gh pr view "$prior" --json author --jq '.author.login')
    prior_head=$(gh pr view "$prior" --json headRefOid --jq '.headRefOid')
    if [[ ( "$author" != 'app/github-actions' && "$author" != 'github-actions[bot]' ) || "$prior_head" != "$(git rev-parse "origin/$branch")" ]]; then
      echo 'Closed updater branch changed ownership or content; refusing to replace it.' >&2
      exit 1
    fi
    if [[ "$(gh pr view "$prior" --json files --jq '[.files[].path] == ["docs/generated/source-reference.md"]')" != 'true' ]]; then
      echo 'Closed updater PR includes other files; refusing to replace it.' >&2
      exit 1
    fi
  fi
  if [[ -n "$existing" ]] && git show "origin/$branch:docs/generated/source-reference.md" | cmp -s - artifacts/docs/source-reference.md; then
    echo 'Open update PR already contains this reference; no changes.'
    exit 0
  fi
fi
git checkout -B "$branch"
git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add -- docs/generated/source-reference.md
git commit -m 'docs: refresh source declaration reference'
git push --force-with-lease origin "$branch"
cat > artifacts/docs/pr-body.md <<'BODY'
Refreshes the deterministic reference from the current package property declarations, public attribute enums, release settings and tooling command declarations.

Generated facts need review alongside any affected authored guides. Narrative behavior and external links are not automatically validated by generation. Existing analyzer tests compile maintained C# examples; generated literal source excerpts are text. The canonical documentation gate checks source drift and local Markdown links and anchors, and runs positive and negative regression fixtures.

GitHub suppresses push and pull_request workflows created with GITHUB_TOKEN. This updater explicitly dispatches the canonical workflows against this branch. Required checks and normal review still apply; no automatic merge is enabled.
BODY
if [[ -z "$existing" ]]; then
  gh pr create --base "$DEFAULT_BRANCH" --head "$branch" --title 'docs: refresh source declaration reference' --body-file artifacts/docs/pr-body.md
else
  gh pr edit "$existing" --body-file artifacts/docs/pr-body.md
fi
# workflow_dispatch is an explicit GITHUB_TOKEN event exception.
gh workflow run ci.yml --ref "$branch"
gh workflow run coverage.yml --ref "$branch" -f comparison_ref="$GITHUB_SHA"
gh workflow run package-consumers.yml --ref "$branch"
