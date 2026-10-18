#!/usr/bin/env bash
set -euo pipefail

# Run only in the tag workflow, before granting publication credentials.
: "${GITHUB_REPOSITORY:?}"
: "${GITHUB_SHA:?}"
: "${DEFAULT_BRANCH:?}"
[[ "$GITHUB_REPOSITORY" == alexyorke/SharpProof ]]
commit=$(git rev-parse "${GITHUB_SHA}^{commit}")
git fetch --no-tags origin "refs/heads/${DEFAULT_BRANCH}:refs/remotes/origin/${DEFAULT_BRANCH}"
git merge-base --is-ancestor "$commit" "refs/remotes/origin/${DEFAULT_BRANCH}"

# Check workflow identity, event, branch and exact SHA, rather than accepting
# a similarly named check from a PR, another workflow, or an earlier commit.
for workflow in ci.yml coverage.yml toolchain-security.yml; do
  result=$(gh api --method GET \
    "repos/${GITHUB_REPOSITORY}/actions/workflows/${workflow}/runs" \
    -f branch="$DEFAULT_BRANCH" -f event=push -f head_sha="$commit" \
    -f per_page=100 \
    --jq '.workflow_runs | sort_by(.run_number) | last | .conclusion // "missing-or-pending"')
  if [[ "$result" != success ]]; then
    echo "Release rejected: latest ${workflow} push run for ${commit} is ${result}." >&2
    echo 'Complete the master test and coverage gates before tagging.' >&2
    exit 1
  fi
done
echo "Release commit ${commit} passed the default-branch test and coverage workflows."
