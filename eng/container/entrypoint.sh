#!/usr/bin/env bash
set -euo pipefail

repo_root="${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
command_name="${1:-dev}"
if [[ $# -gt 0 ]]; then
  shift
fi

if [[ "$(id -u)" = "0" ]] && id sharpproof > /dev/null 2>&1; then
  install -d -o sharpproof -g sharpproof \
    /home/sharpproof/.nuget \
    /home/sharpproof/.nuget/NuGet \
    /home/sharpproof/.nuget/packages \
    /home/sharpproof/.dotnet \
    "${repo_root}"
  if [[ "${command_name}" != "dev" && "${command_name}" != "doctor" ]]; then
    install -d -o sharpproof -g sharpproof "${repo_root}/artifacts"
  fi
  if [[ "${command_name}" = "coverage-gate" ]]; then
    # Downloads create this directory as the runner user. Only the gate's
    # output directory needs to be writable after dropping privileges;
    # downloaded shard evidence is consumed without modifying its ownership.
    install -d -o sharpproof -g sharpproof "${repo_root}/artifacts/coverage"
  elif [[ "${command_name}" = "canonical-cohort-gate" ]]; then
    # Normalize only the receipt output directory; downloaded evidence stays unchanged.
    install -d -o sharpproof -g sharpproof "${repo_root}/artifacts/canonical-cohorts"
  fi
  export HOME=/home/sharpproof
  exec runuser --user sharpproof --preserve-environment -- \
    /usr/local/bin/sharpproof-container "${command_name}" "$@"
fi

container_contract="${SHARPPROOF_CONTAINER_CONTRACT:-}"
if [[ -z "${container_contract}" || ! -f "${container_contract}" ]]; then
  echo "SharpProof canonical container contract is missing." >&2
  exit 125
fi

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
  echo "SharpProof verification requires the canonical linux/amd64 container." >&2
  exit 125
fi

source_has_git=false
if git_directory="$(git -c safe.directory="${repo_root}" -C "${repo_root}" \
  rev-parse --absolute-git-dir 2> /dev/null)"; then
  source_has_git=true
  git config --global --add safe.directory "${repo_root}"
  git config --global --add safe.directory "${git_directory}"
fi

unset SHARPPROOF_GOLDEN_SOURCE_ROOT
if [[ "${command_name}" = "dev" ]]; then
  exec /bin/bash "$@"
fi
if [[ "${command_name}" = "doctor" ]]; then
  # Inspect the selected consumer and its existing imports/assets in place.
  # A disposable task clone intentionally omits ignored restore outputs.
  cd "${repo_root}"
  exec pwsh -NoLogo -NoProfile -File ./eng/scripts/Invoke-SharpProofDoctor.ps1 "$@"
fi

case "${command_name}" in
  test | test-changed | semantic-tests | portable-tests | worker-tests | package-tests)
    golden_source_root="$(realpath -e "${SHARPPROOF_LOOP_SOURCE_ROOT:-${repo_root}}")"
    if [[ ! -f "${golden_source_root}/SharpProof.slnx" ||
      ! -f "${golden_source_root}/eng/Versions.props" ]]; then
      echo "SharpProof golden source root is not a repository." >&2
      exit 125
    fi
    export SHARPPROOF_GOLDEN_SOURCE_ROOT="${golden_source_root}"
    ;;
  *)
    unset SHARPPROOF_UPDATE_GOLDEN
    ;;
esac

requires_clean_exact_commit_source() {
  case "$1" in
    nightly | fuzz-nightly | pack | pack-tool)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

requires_git_source() {
  if requires_clean_exact_commit_source "$1"; then
    return 0
  fi
  case "$1" in
    layout | lint | quick | pr | security | pr-gates | repository-gates | test-changed | package-consumers | coverage | coverage-shard | coverage-gate | package-partition-gate)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

assert_clean_exact_commit_source() {
  if ! git -C "${repo_root}" diff --quiet --ignore-submodules=none -- ||
    ! git -C "${repo_root}" diff --cached --quiet \
      --ignore-submodules=none HEAD --; then
    echo "SharpProof ${command_name} requires clean exact-commit source; tracked index or working-tree changes were found." >&2
    exit 2
  fi

  local untracked_path
  while IFS= read -r -d '' untracked_path; do
    case "${untracked_path}" in
      nupkgs/*)
        # Release commands receive the exact package-job artifacts here.
        ;;
      *)
        echo "SharpProof ${command_name} requires clean exact-commit source; an untracked source path was found." >&2
        exit 2
        ;;
    esac
  done < <(git -C "${repo_root}" ls-files \
    --others --exclude-standard -z --)
}

if requires_git_source "${command_name}" &&
  [[ "${source_has_git}" != "true" ]]; then
  echo "SharpProof ${command_name} requires accessible Git metadata for source comparison or certifying evidence. For host Git worktrees, run: pwsh -File eng/scripts/Invoke-SharpProof.ps1 ${command_name} (the wrapper mounts resolved Git metadata read-only)." >&2
  exit 2
fi

if requires_clean_exact_commit_source "${command_name}"; then
  assert_clean_exact_commit_source
fi

task_root="$(mktemp -d /tmp/sharpproof-task.XXXXXXXX)"
mkdir -p "${repo_root}/artifacts"
if [[ "${source_has_git}" = "true" ]]; then
  git clone --quiet --shared --no-checkout "${repo_root}" "${task_root}"
  git config --global --add safe.directory "${task_root}"
  source_origin="$(git -C "${repo_root}" remote get-url origin 2> /dev/null || true)"
  if [[ -n "${source_origin}" ]]; then
    git -C "${task_root}" remote set-url origin "${source_origin}"
  fi
  git -C "${task_root}" checkout --quiet --detach \
    "$(git -C "${repo_root}" rev-parse HEAD)"
  # Docker Desktop bind mounts do not preserve meaningful Git executable
  # bits. Ignore their synthetic working-tree modes in the disposable clone;
  # real mode changes committed between Git trees remain part of comparisons.
  git -C "${task_root}" config core.filemode false
  if ! git -C "${repo_root}" diff --quiet HEAD --; then
    git -C "${repo_root}" diff \
      --binary --full-index --no-ext-diff HEAD -- . |
      git -C "${task_root}" apply \
        --binary --whitespace=nowarn -
  fi
  while IFS= read -r -d '' untracked_path; do
    mkdir -p -- "${task_root}/$(dirname -- "${untracked_path}")"
    cp -a -- \
      "${repo_root}/${untracked_path}" \
      "${task_root}/${untracked_path}"
  done < <(git -C "${repo_root}" ls-files \
    --others --exclude-standard -z --)
  # Package jobs download nupkg/snupkg inputs under nupkgs/. Those file
  # extensions are intentionally ignored by Git, so the general untracked
  # copy above cannot see them. Preserve only ignored package-job inputs;
  # do not broaden the snapshot to other ignored build output.
  while IFS= read -r -d '' package_path; do
    mkdir -p -- "${task_root}/$(dirname -- "${package_path}")"
    cp -a -- \
      "${repo_root}/${package_path}" \
      "${task_root}/${package_path}"
  done < <(git -C "${repo_root}" ls-files \
    --others --ignored --exclude-standard -z -- nupkgs/)
else
  tar \
    --exclude='./artifacts' \
    --exclude='./.git' \
    --exclude='*/bin' \
    --exclude='*/bin/*' \
    --exclude='*/obj' \
    --exclude='*/obj/*' \
    --exclude='./.vs' \
    --exclude='./.baseline-check' \
    --exclude='./.claude' \
    --exclude='./.claude/*' \
    -C "${repo_root}" -cf - . | tar -C "${task_root}" -xf -
fi
mkdir -p "${task_root}/artifacts"
# The optional tool feed is an explicit input to its following qualification
# task. Copy only its release archives and known identity sidecars; keep all
# other ignored artifact output outside the private workspace snapshot.
if [[ "${command_name}" == tool-consumer ]]; then
  package_source=""
  previous_argument=""
  for argument in "$@"; do
    if [[ "${previous_argument,,}" == -packagesource ]]; then
      package_source="${argument}"
      break
    fi
    previous_argument="${argument}"
  done
  case "${package_source}" in
    artifacts/tool-packages | artifacts/tool-packages/ | ./artifacts/tool-packages | ./artifacts/tool-packages/)
      mkdir -p "${task_root}/artifacts/tool-packages"
      for package_input in "${repo_root}"/artifacts/tool-packages/*.nupkg \
        "${repo_root}"/artifacts/tool-packages/packages.tsv \
        "${repo_root}"/artifacts/tool-packages/packages.lock.json \
        "${repo_root}"/artifacts/tool-packages/Test-SharpProofPublishedPackages.sh; do
        if [[ -f "${package_input}" && ! -L "${package_input}" ]]; then
          cp -a -- "${package_input}" "${task_root}/artifacts/tool-packages/"
        fi
      done
      ;;
  esac
fi
# Build outputs are centralized by Arcade. Keep bin/obj/toolset private to
# this task instead of writing them through a shared host artifacts symlink.
case "${command_name}" in
  coverage-gate)
    if [[ -d "${repo_root}/artifacts/coverage" ]]; then
      cp -a "${repo_root}/artifacts/coverage" "${task_root}/artifacts/"
    fi
    ;;
  package-partition-gate)
    if [[ -d "${repo_root}/artifacts/package-partitions" ]]; then
      cp -a "${repo_root}/artifacts/package-partitions" "${task_root}/artifacts/"
    fi
    ;;
  canonical-cohort-gate)
    if [[ -d "${repo_root}/artifacts/canonical-cohorts" ]]; then
      cp -a "${repo_root}/artifacts/canonical-cohorts" "${task_root}/artifacts/"
    fi
    ;;
esac
if [[ "${source_has_git}" = "true" ]] &&
  [[ -d "${task_root}/.git" ]]; then
  # Keep generated task artifacts outside tracked-file discovery.
  printf '/artifacts\n' >> "${task_root}/.git/info/exclude"
  # Include copied additions in changed-line checks and remove deleted paths
  # from tracked-file discovery after moves. Only the disposable task index
  # is modified; existing content edits remain unstaged.
  git -C "${task_root}" add --intent-to-add --all -- .
fi
export SHARPPROOF_REPO_ROOT="${task_root}"
cd "${task_root}"
set +e
if [[ "${command_name}" == arcade ]]; then
  bash ./eng/build.sh "$@"
else
  pwsh -NoLogo -NoProfile -File ./eng/scripts/Invoke-SharpProofContainer.ps1 \
    -Command "${command_name}" "$@"
fi
task_status=$?
set -e
# Export evidence and packages on success or failure. Compiler intermediates,
# tool restore output and temporary build roots stay in the disposable task.
# Aggregate gates consume downloaded evidence owned by the runner. Export only
# their new results, preserving the authenticated inputs and their ownership.
case "${command_name}" in
  package-partition-gate)
    result_paths=()
    ;;
  canonical-cohort-gate)
    result_paths=(canonical-cohorts/canonical-cohort-validation.json)
    ;;
  coverage-gate)
    result_paths=(coverage/coverage-authority.json coverage/SharpProof.coverage.json)
    ;;
  *)
    result_paths=(.)
    ;;
esac
export_paths=()
for result_path in "${result_paths[@]}"; do
  if [[ -e "${task_root}/artifacts/${result_path}" ]]; then
    export_paths+=("${result_path}")
  fi
done
if [[ ${#export_paths[@]} -eq 0 ]]; then
  exit "${task_status}"
fi
tar -C "${task_root}/artifacts" \
  --exclude='./bin' --exclude='./obj' --exclude='./toolset' --exclude='./tmp' \
  -cf - "${export_paths[@]}" | tar -C "${repo_root}/artifacts" -xf -
exit "${task_status}"
