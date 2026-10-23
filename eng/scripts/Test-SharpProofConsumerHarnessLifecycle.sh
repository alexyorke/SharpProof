#!/usr/bin/env bash
# Exercise the real snapshot/export entrypoint without running product builds.
set -euo pipefail
repository=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
entrypoint="$repository/eng/container/entrypoint.sh"
if [[ "${SHARPPROOF_CONTAINER:-}" != 1 || "$(uname -s)" != Linux ||
"$(uname -m)" != x86_64 || "$(id -u)" == 0 ]]; then
  echo 'Run consumer harness lifecycle regressions through the canonical tooling dev container user.' >&2
  exit 1
fi
fixture_root=$(mktemp -d /tmp/sharpproof-consumer-harness-lifecycle.XXXXXXXX)
cleanup() {
  if [[ ! "$fixture_root" =~ ^/tmp/sharpproof-consumer-harness-lifecycle\.[[:alnum:]]{8}$ ]]; then
    echo 'Unexpected consumer lifecycle fixture path; refusing cleanup.' >&2
    return 1
  fi
  if [[ -f "$fixture_root/task-roots" ]]; then
    while IFS= read -r task_directory; do
      if [[ ! "$task_directory" =~ ^/tmp/sharpproof-task\.[[:alnum:]]{8}$ ||
        "$(realpath -e "$task_directory")" != "$task_directory" ]]; then
        echo 'Unexpected consumer lifecycle task path; refusing cleanup.' >&2
        return 1
      fi
      chmod -R u+w -- "$task_directory"
      rm -rf -- "$task_directory"
    done < "$fixture_root/task-roots"
  fi
  chmod -R u+w -- "$fixture_root"
  rm -rf -- "$fixture_root"
}
trap cleanup EXIT
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL="$fixture_root/gitconfig" GIT_TERMINAL_PROMPT=0
mkdir -p "$fixture_root/bin"
cat > "$fixture_root/bin/pwsh" << 'SHIM'
#!/usr/bin/env bash
set -euo pipefail
[[ "$PWD" == "$SHARPPROOF_REPO_ROOT" && "$PWD" != "$FIXTURE_SOURCE" ]]
printf '%s\n' "$PWD" >> "$FIXTURE_ROOT/task-roots"
printf 'called\n' > "$FIXTURE_STATE/called"
command_name=""
harness=""
previous=""
for argument in "$@"; do
  case "${previous,,}" in
    -command) command_name="$argument" ;;
    -consumerharness) harness="$argument" ;;
  esac
  previous="$argument"
done
[[ "$command_name" == package-consumers ]]
if [[ "$FIXTURE_SCENARIO" == legacy ]]; then
  [[ -z "$harness" && ! -e artifacts/consumer-harness ]]
  mkdir -p artifacts
  printf 'legacy result\n' > artifacts/legacy-evidence.txt
else
  [[ "$harness" == artifacts/consumer-harness ]]
  cmp "$FIXTURE_SOURCE/artifacts/consumer-harness/receipt.json" "$harness/receipt.json"
  cmp "$FIXTURE_SOURCE/artifacts/consumer-harness/payload/Harness.dll" "$harness/payload/Harness.dll"
  # The downloaded input belongs to the host. Deliberate changes to this
  # disposable copy must never be exported back, even when permissions allow it.
  chmod -R u+w -- "$harness"
  printf 'private receipt mutation\n' > "$harness/receipt.json"
  printf 'private payload mutation\n' > "$harness/payload/Harness.dll"
  touch -m -t 200101010101 "$harness" "$harness/payload"
  mkdir -p artifacts/package-consumers/harness-results artifacts/unrelated-evidence
  printf '{"scenario":"%s"}\n' "$FIXTURE_SCENARIO" > artifacts/package-consumers/harness-results/qualification.json
  printf 'private unrelated output\n' > artifacts/unrelated-evidence/private.txt
fi
exit "$FIXTURE_PWSH_STATUS"
SHIM
chmod +x "$fixture_root/bin/pwsh"
export PATH="$fixture_root/bin:$PATH" FIXTURE_ROOT="$fixture_root"

fail() {
  echo "Consumer harness lifecycle regression failed ($scenario): $*" >&2
  exit 1
}
snapshot_input() {
  (
    cd "$case_root/source/artifacts/consumer-harness"
    find . -type f -print0 | sort -z | xargs -0 sha256sum
    find . -print0 | sort -z | xargs -0 stat --format='%n|%a|%s|%y|%u|%g'
  ) > "$1"
}
new_case() {
  scenario=$1
  case_root="$fixture_root/$scenario"
  mkdir -p "$case_root/source/eng/scripts" "$case_root/state"
  printf '/artifacts/\n' > "$case_root/source/.gitignore"
  printf '<Solution />\n' > "$case_root/source/SharpProof.slnx"
  printf '<Project />\n' > "$case_root/source/eng/Versions.props"
  printf '# Intercepted by the fixture pwsh shim.\n' > "$case_root/source/eng/scripts/Invoke-SharpProofContainer.ps1"
  git -C "$case_root/source" init -q --initial-branch=master
  git -C "$case_root/source" config user.name 'Consumer lifecycle fixture'
  git -C "$case_root/source" config user.email 'fixture@example.invalid'
  git -C "$case_root/source" add -- .
  git -C "$case_root/source" commit -qm 'fixture source'
  mkdir -p "$case_root/source/artifacts/consumer-harness/payload"
  printf '{"fixture":"immutable"}\n' > "$case_root/source/artifacts/consumer-harness/receipt.json"
  printf 'immutable runtime dependency\n' > "$case_root/source/artifacts/consumer-harness/payload/Harness.dll"
  find "$case_root/source/artifacts/consumer-harness" -type f -exec chmod 444 {} +
  find "$case_root/source/artifacts/consumer-harness" -type d -exec chmod 555 {} +
  snapshot_input "$case_root/before"
  export FIXTURE_SOURCE="$case_root/source" FIXTURE_STATE="$case_root/state" FIXTURE_SCENARIO="$scenario"
}
run_case() {
  local expected=$1
  shift
  local actual=0
  SHARPPROOF_REPO_ROOT="$FIXTURE_SOURCE" bash "$entrypoint" package-consumers "$@" > "$case_root/entrypoint.log" 2>&1 || actual=$?
  [[ "$actual" == "$expected" ]] || fail "expected exit $expected, got $actual; see $case_root/entrypoint.log"
  snapshot_input "$case_root/after"
  cmp -s "$case_root/before" "$case_root/after" || fail 'downloaded input bytes, permissions, ownership or timestamps changed'
}

for expected_status in 0 23; do
  new_case "harness-$expected_status"
  export FIXTURE_PWSH_STATUS="$expected_status"
  run_case "$expected_status" -PackageSource nupkgs -ConsumerHarness artifacts/consumer-harness
  [[ -f "$case_root/state/called" ]] || fail 'consumer was not invoked'
  result="$case_root/source/artifacts/package-consumers/harness-results/qualification.json"
  [[ -f "$result" ]] || fail 'new qualification evidence was not exported'
  [[ "$(cat "$result")" == "{\"scenario\":\"$scenario\"}" ]] || fail 'exported evidence does not match this invocation'
  [[ ! -e "$case_root/source/artifacts/unrelated-evidence" ]] || fail 'unrelated private output was exported'
done

new_case legacy
export FIXTURE_PWSH_STATUS=0
run_case 0 -PackageSource nupkgs
[[ "$(cat "$case_root/source/artifacts/legacy-evidence.txt")" == 'legacy result' ]] || fail 'legacy export behavior changed'

new_case unsupported-path
export FIXTURE_PWSH_STATUS=0
run_case 2 -PackageSource nupkgs -ConsumerHarness ../consumer-harness
[[ ! -e "$case_root/state/called" ]] || fail 'unsupported harness path reached the consumer'
grep -q 'must use the explicit artifacts/consumer-harness input' "$case_root/entrypoint.log" || fail 'unsupported path failed for an unrelated reason'
echo 'Consumer harness lifecycle regressions passed: immutable imported inputs, success/failure evidence export, legacy fallback and rejected paths.'
