#!/usr/bin/env bash
set -euo pipefail
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
[[ ${SHARPPROOF_CONTAINER:-} == 1 && -f ${SHARPPROOF_CONTAINER_CONTRACT:-} ]]
[[ $(id -u) != 0 ]]
fixture=$(mktemp -d /tmp/sharpproof-entrypoint-inputs.XXXXXXXX)
cleanup() {
  local dispatch_log task_directory
  for dispatch_log in "$fixture"/*/dispatch.log; do
    [[ -f $dispatch_log ]] || continue
    while IFS= read -r task_directory; do
      if [[ $task_directory == /tmp/sharpproof-task.* && -d $task_directory/.git ]]; then
        rm -rf -- "$task_directory"
      else
        printf 'Refusing unexpected entrypoint fixture workspace: %s\n' "$task_directory" >&2
        return 1
      fi
    done < "$dispatch_log"
  done
  [[ $fixture == /tmp/sharpproof-entrypoint-inputs.* ]]
  rm -rf -- "$fixture"
}
trap cleanup EXIT
mkdir -p "$fixture/bin" "$fixture/base/eng/scripts"
export GIT_CONFIG_GLOBAL="$fixture/gitconfig" GIT_CONFIG_NOSYSTEM=1
printf '*.nupkg\n*.snupkg\nartifacts/\n' > "$fixture/base/.gitignore"
printf 'tracked source\n' > "$fixture/base/Tracked.cs"
printf '# This dispatcher is replaced only at the pwsh process boundary.\n' > "$fixture/base/eng/scripts/Invoke-SharpProofContainer.ps1"
git init --quiet "$fixture/base"
git -C "$fixture/base" add -- .gitignore Tracked.cs eng/scripts/Invoke-SharpProofContainer.ps1
git -C "$fixture/base" -c user.name=Fixture -c user.email=fixture@example.invalid commit --quiet -m Fixture
expected_commit=$(git -C "$fixture/base" rev-parse HEAD)
cat > "$fixture/bin/pwsh" << 'STUB'
#!/usr/bin/env bash
set -euo pipefail
[[ $# == 6 && $1 == -NoLogo && $2 == -NoProfile && $3 == -File && $4 == ./eng/scripts/Invoke-SharpProofContainer.ps1 && $5 == -Command && $6 == developer-packages ]]
[[ $PWD == "$SHARPPROOF_REPO_ROOT" && $PWD == /tmp/sharpproof-task.* ]]
printf '%s\n' "$PWD" >> "$FIXTURE_DISPATCH_LOG"
mkdir -p artifacts/entrypoint-fixture
git rev-parse HEAD > artifacts/entrypoint-fixture/source-commit
while IFS= read -r relative_path; do
  [[ -f $relative_path && ! -L $relative_path ]]
  sha256sum -- "$relative_path"
done < "$FIXTURE_EXPECTED_PATHS" > artifacts/entrypoint-fixture/inputs.sha256
STUB
chmod +x "$fixture/bin/pwsh"
checks=0
run_case() {
  local name=$1 mutation=$2 expected_status=$3 case_root case_repository relative_path status=0
  case_root="$fixture/$name"
  case_repository="$case_root/repository"
  mkdir -p "$case_root"
  git clone --quiet --no-hardlinks "$fixture/base" "$case_repository"
  : > "$case_root/expected-paths"
  case "$mutation" in
    tool)
      mkdir -p "$case_repository/tool-nupkgs"
      for relative_path in tool-nupkgs/SharpProof.Tool.1.2.3.nupkg tool-nupkgs/packages.tsv tool-nupkgs/packages.lock.json tool-nupkgs/Test-SharpProofPublishedPackages.sh; do
        printf 'qualified input %s\n' "$relative_path" > "$case_repository/$relative_path"
        printf '%s\n' "$relative_path" >> "$case_root/expected-paths"
      done
      ;;
    core)
      mkdir -p "$case_repository/nupkgs"
      for relative_path in nupkgs/SharpProof.1.2.3.nupkg nupkgs/packages.tsv nupkgs/packages.lock.json nupkgs/Test-SharpProofPublishedPackages.sh; do
        printf 'qualified input %s\n' "$relative_path" > "$case_repository/$relative_path"
        printf '%s\n' "$relative_path" >> "$case_root/expected-paths"
      done
      ;;
    unexpected-tool)
      mkdir -p "$case_repository/tool-nupkgs"
      printf 'unexpected source\n' > "$case_repository/tool-nupkgs/Unexpected.cs"
      ;;
    nested-tool-sidecar)
      mkdir -p "$case_repository/tool-nupkgs/nested"
      printf 'unexpected nested input\n' > "$case_repository/tool-nupkgs/nested/packages.tsv"
      ;;
    untracked) printf 'untracked source\n' > "$case_repository/Unexpected.cs" ;;
    tracked) printf 'changed tracked source\n' >> "$case_repository/Tracked.cs" ;;
    staged)
      printf 'staged tracked source\n' >> "$case_repository/Tracked.cs"
      git -C "$case_repository" add -- Tracked.cs
      ;;
    *) return 1 ;;
  esac
  (
    cd "$case_repository"
    while IFS= read -r relative_path; do sha256sum -- "$relative_path"; done < "$case_root/expected-paths"
  ) > "$case_root/expected.sha256"
  SHARPPROOF_REPO_ROOT="$case_repository" FIXTURE_DISPATCH_LOG="$case_root/dispatch.log" \
    FIXTURE_EXPECTED_PATHS="$case_root/expected-paths" PATH="$fixture/bin:$PATH" \
    bash "$script_directory/../container/entrypoint.sh" developer-packages > "$case_root/output.log" 2>&1 || status=$?
  if [[ $status != "$expected_status" ]]; then
    printf 'Entrypoint fixture %s failed: exit=%s, expected=%s.\n' "$name" "$status" "$expected_status" >&2
    tail -n 35 "$case_root/output.log" >&2
    return 1
  fi
  if [[ $expected_status == 0 ]]; then
    [[ $(wc -l < "$case_root/dispatch.log") == 1 ]]
    [[ $(< "$case_repository/artifacts/entrypoint-fixture/source-commit") == "$expected_commit" ]]
    cmp -- "$case_root/expected.sha256" "$case_repository/artifacts/entrypoint-fixture/inputs.sha256"
  else
    [[ ! -e $case_root/dispatch.log ]]
    grep -Fq 'requires clean exact-commit source' "$case_root/output.log"
  fi
  checks=$((checks + 1))
}
run_case qualified-tool tool 0
run_case existing-core core 0
run_case unexpected-tool unexpected-tool 2
run_case nested-tool-sidecar nested-tool-sidecar 2
run_case unrelated-untracked untracked 2
run_case dirty-tracked tracked 2
run_case dirty-staged staged 2
printf 'Container entrypoint regressions passed: %s exact-input snapshot and clean-source controls. Real Git/entrypoint ran; only the final pwsh dispatcher was stubbed, with no .NET build or Docker launch.\n' "$checks"
