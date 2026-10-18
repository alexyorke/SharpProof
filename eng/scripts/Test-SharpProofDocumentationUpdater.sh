#!/usr/bin/env bash
# Runs only in the canonical Linux container; all remotes and gh responses are local.
set -euo pipefail
repository=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
updater="$repository/eng/scripts/Update-SharpProofDocumentation.sh"
branch=codex/automation-docs-sync
mkdir -p "$repository/artifacts"
fixture_root=$(mktemp -d "$repository/artifacts/docs-updater-tests.XXXXXX")
cleanup() {
  case "$fixture_root" in
    "$repository"/artifacts/docs-updater-tests.*) rm -rf -- "$fixture_root" ;;
    *) echo 'Unexpected updater fixture path; refusing cleanup.' >&2; return 1 ;;
  esac
}
trap cleanup EXIT
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0
unset GH_TOKEN GITHUB_TOKEN
fail() { echo "Updater lifecycle regression failed ($scenario): $*" >&2; exit 1; }
equal() { [[ "$1" == "$2" ]] || fail "expected [$2], got [$1]"; }
remote_head() { git --git-dir="$case_root/remote.git" rev-parse "refs/heads/$1"; }

# Deliberately supports only the publication API contract. Unexpected commands fail.
mkdir -p "$fixture_root/bin"
cat > "$fixture_root/bin/gh" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
arg_value() {
  local key=$1; shift
  while (($#)); do
    if [[ "$1" == "$key" ]]; then printf '%s' "$2"; return; fi
    shift
  done
  return 1
}
case "$1 $2" in
  'pr list')
    [[ "$(arg_value --head "$@")" == codex/automation-docs-sync ]]
    [[ "$(arg_value --base "$@")" == master ]]
    state=$(arg_value --state "$@")
    status=$(cat "$MOCK_STATE/status")
    if [[ "$status" == "$state" || ( "$state" == all && ( "$status" == closed || "$status" == merged ) ) ]]; then printf '17\n'; fi
    ;;
  'pr view')
    [[ "$3" == 17 ]]
    case "$(arg_value --json "$@")" in
      author) cat "$MOCK_STATE/author" ;;
      headRefOid) cat "$MOCK_STATE/head" ;;
      files) cat "$MOCK_STATE/files" ;;
      *) exit 91 ;;
    esac
    ;;
  'pr create')
    [[ "$(arg_value --base "$@")" == master ]]
    [[ "$(arg_value --head "$@")" == codex/automation-docs-sync ]]
    [[ -s "$(arg_value --body-file "$@")" ]]
    printf 'create\n' >> "$MOCK_STATE/writes"
    printf 'open\n' > "$MOCK_STATE/status"
    git rev-parse HEAD > "$MOCK_STATE/head"
    ;;
  'pr edit')
    [[ "$3" == 17 && -s "$(arg_value --body-file "$@")" ]]
    printf 'edit\n' >> "$MOCK_STATE/writes"
    git rev-parse HEAD > "$MOCK_STATE/head"
    ;;
  'workflow run')
    [[ "$(arg_value --ref "$@")" == codex/automation-docs-sync ]]
    case "$3" in
      ci.yml|package-consumers.yml) [[ $# == 5 ]] ;;
      coverage.yml) [[ $# == 7 && "$(arg_value -f "$@")" == "comparison_ref=$GITHUB_SHA" ]] ;;
      *) exit 92 ;;
    esac
    printf 'dispatch:%s\n' "$3" >> "$MOCK_STATE/writes"
    ;;
  *) echo 'Unexpected gh command in updater fixture.' >&2; exit 93 ;;
esac
MOCK
chmod +x "$fixture_root/bin/gh"

new_case() {
  scenario=$1
  case_root="$fixture_root/$scenario"
  mkdir -p "$case_root/state" "$case_root/work"
  git init -q --bare --initial-branch=master "$case_root/remote.git"
  git -C "$case_root/work" init -q --initial-branch=master
  git -C "$case_root/work" config user.name 'Documentation fixture'
  git -C "$case_root/work" config user.email 'fixture@example.invalid'
  mkdir -p "$case_root/work/docs/generated" "$case_root/work/artifacts/docs"
  printf 'authored guide\n' > "$case_root/work/guide.md"
  printf 'old reference\n' > "$case_root/work/docs/generated/source-reference.md"
  git -C "$case_root/work" add guide.md docs/generated/source-reference.md
  git -C "$case_root/work" commit -qm 'fixture base'
  git -C "$case_root/work" remote add origin "$case_root/remote.git"
  git -C "$case_root/work" push -q origin master
  base=$(remote_head master)
  printf 'new reference\n' > "$case_root/work/artifacts/docs/source-reference.md"
  printf 'none\n' > "$case_root/state/status"
  printf 'app/github-actions\n' > "$case_root/state/author"
  printf 'true\n' > "$case_root/state/files"
  printf '%s\n' "$base" > "$case_root/state/head"
  : > "$case_root/state/writes"
}
seed_branch() {
  local reference=$1
  git -C "$case_root/work" checkout -qb "$branch"
  printf '%s\n' "$reference" > "$case_root/work/docs/generated/source-reference.md"
  git -C "$case_root/work" add docs/generated/source-reference.md
  git -C "$case_root/work" commit -qm 'previous reference'
  git -C "$case_root/work" push -q origin "$branch"
  known_head=$(remote_head "$branch")
  printf '%s\n' "$known_head" > "$case_root/state/head"
  git -C "$case_root/work" checkout -q master
}
run_updater() {
  local expected=$1 result=0
  # Every run models a fresh checkout of the generation commit, retaining local mock history.
  git -C "$case_root/work" checkout -q master
  git -C "$case_root/work" reset -q --hard "$base"
  (
    cd "$case_root/work"
    export PATH="$fixture_root/bin:$PATH" DEFAULT_BRANCH=master GITHUB_SHA="$base"
    export MOCK_STATE="$case_root/state"
    bash "$updater"
  ) > "$case_root/output" 2>&1 || result=$?
  if [[ "$expected" == success ]]; then
    if ((result != 0)); then tail -n 20 "$case_root/output" >&2; fail "updater exited $result"; fi
  else
    ((result != 0)) || fail 'unowned branch was accepted'
  fi
}
assert_no_writes() { [[ ! -s "$case_root/state/writes" ]] || fail 'unexpected PR mutation or workflow dispatch'; }
assert_publication() {
  local action=$1 expected
  expected=$(printf '%s\ndispatch:ci.yml\ndispatch:coverage.yml\ndispatch:package-consumers.yml' "$action")
  equal "$(cat "$case_root/state/writes")" "$expected"
  equal "$(git --git-dir="$case_root/remote.git" show "$branch:docs/generated/source-reference.md")" 'new reference'
  equal "$(git --git-dir="$case_root/remote.git" show "$branch:guide.md")" 'authored guide'
  equal "$(remote_head master)" "$base"
  equal "$(git --git-dir="$case_root/remote.git" rev-list --count "master..$branch")" 1
  equal "$(git --git-dir="$case_root/remote.git" diff --name-only "master...$branch")" 'docs/generated/source-reference.md'
  equal "$(git --git-dir="$case_root/remote.git" for-each-ref --format='%(refname)' refs/heads | wc -l | tr -d ' ')" 2
}
assert_preserved() {
  equal "$(remote_head "$branch")" "$known_head"
  equal "$(remote_head master)" "$base"
  assert_no_writes
}

new_case unchanged
printf 'old reference\n' > "$case_root/work/artifacts/docs/source-reference.md"
run_updater success
assert_no_writes
equal "$(git --git-dir="$case_root/remote.git" for-each-ref --format='%(refname)' refs/heads)" refs/heads/master

new_case create
run_updater success
assert_publication create
published_head=$(remote_head "$branch")
run_updater success
assert_publication create
equal "$(remote_head "$branch")" "$published_head"

new_case existing_open
seed_branch 'previous reference'
printf 'open\n' > "$case_root/state/status"
run_updater success
assert_publication edit
[[ "$(remote_head "$branch")" != "$known_head" ]] || fail 'changed reference was not pushed'
published_head=$(remote_head "$branch")
run_updater success
assert_publication edit
equal "$(remote_head "$branch")" "$published_head"

new_case existing_open_current
seed_branch 'new reference'
printf 'open\n' > "$case_root/state/status"
run_updater success
assert_preserved

# The all-state PR lookup must recognize both closed and merged histories.
for lifecycle in closed merged; do
  new_case "$lifecycle"
  seed_branch 'previous reference'
  printf '%s\n' "$lifecycle" > "$case_root/state/status"
  if [[ "$lifecycle" == merged ]]; then
    # Advance master with the former PR's result, then generate a newer reference.
    git -C "$case_root/work" merge -q --ff-only "$branch"
    git -C "$case_root/work" push -q origin master
    base=$(remote_head master)
    printf 'github-actions[bot]\n' > "$case_root/state/author"
  fi
  run_updater success
  assert_publication create
done

new_case unknown_branch
seed_branch 'unowned reference'
run_updater failure
assert_preserved

new_case closed_changed_head
seed_branch 'previous reference'
printf 'closed\n' > "$case_root/state/status"
printf '%s\n' "$base" > "$case_root/state/head"
run_updater failure
assert_preserved

for lifecycle in open closed; do
  new_case "${lifecycle}_human"
  seed_branch 'human reference'
  printf '%s\n' "$lifecycle" > "$case_root/state/status"
  printf 'human-author\n' > "$case_root/state/author"
  run_updater failure
  assert_preserved

  new_case "${lifecycle}_extra_files"
  seed_branch 'previous reference'
  printf '%s\n' "$lifecycle" > "$case_root/state/status"
  git -C "$case_root/work" checkout -q "$branch"
  printf 'unrelated work\n' > "$case_root/work/unrelated.txt"
  git -C "$case_root/work" add unrelated.txt
  git -C "$case_root/work" commit -qm 'unrelated branch work'
  git -C "$case_root/work" push -q origin "$branch"
  known_head=$(remote_head "$branch")
  printf '%s\n' "$known_head" > "$case_root/state/head"
  printf 'false\n' > "$case_root/state/files"
  run_updater failure
  assert_preserved
done

new_case master_advanced
seed_branch 'previous reference'
printf 'open\n' > "$case_root/state/status"
printf 'later authored guide\n' > "$case_root/work/guide.md"
git -C "$case_root/work" add guide.md
git -C "$case_root/work" commit -qm 'advance default branch'
git -C "$case_root/work" push -q origin master
advanced=$(remote_head master)
run_updater success
equal "$(remote_head master)" "$advanced"
equal "$(remote_head "$branch")" "$known_head"
equal "$(git -C "$case_root/work" show HEAD:docs/generated/source-reference.md)" 'old reference'
equal "$(cat "$case_root/work/docs/generated/source-reference.md")" 'old reference'
assert_no_writes

printf 'Documentation updater lifecycle regressions passed: no-op, creation, refresh, duplicate suppression, ownership, closed/merged reuse and default-branch race.\n'
