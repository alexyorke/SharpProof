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
  elif [[ "${command_name}" = "format-shard-gate" ]]; then
    # The gate exports one new validation file beside immutable downloaded receipts.
    install -d -o sharpproof -g sharpproof "${repo_root}/artifacts/formatting"
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
    nightly | fuzz-nightly | pack | pack-ci-products | pack-tool | package-reproducibility | developer-packages)
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
    layout | lint | quick | pr | security | pr-gates | repository-gates | test-changed | package-consumers | coverage | coverage-shard | coverage-gate | package-partition-gate | format-shard-gate)
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
      nupkgs/* | tool-nupkgs/packages.tsv | tool-nupkgs/packages.lock.json | tool-nupkgs/Test-SharpProofPublishedPackages.sh)
        # Release commands receive the exact package-job archives and these
        # three optional-tool identity sidecars. Other source remains forbidden.
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
  # Package jobs download immutable archives under nupkgs/ and tool-nupkgs/. Those file
  # extensions are intentionally ignored by Git, so the general untracked
  # copy above cannot see them. Preserve only ignored package-job inputs;
  # do not broaden the snapshot to other ignored build output.
  while IFS= read -r -d '' package_path; do
    mkdir -p -- "${task_root}/$(dirname -- "${package_path}")"
    cp -a -- \
      "${repo_root}/${package_path}" \
      "${task_root}/${package_path}"
  done < <(git -C "${repo_root}" ls-files \
    --others --ignored --exclude-standard -z -- nupkgs/ tool-nupkgs/)
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
# Consumer qualification imports only its explicit immutable harness input.
# Ordinary compiler outputs remain private to each task workspace.
if [[ "${command_name}" == package-consumers ]]; then
  consumer_harness=""
  previous_argument=""
  for argument in "$@"; do
    if [[ "${previous_argument,,}" == -consumerharness ]]; then
      consumer_harness="${argument}"
      break
    fi
    previous_argument="${argument}"
  done
  if [[ -n "${consumer_harness}" ]]; then
    if [[ "${consumer_harness}" != artifacts/consumer-harness ]]; then
      echo "Consumer harness must use the explicit artifacts/consumer-harness input." >&2
      exit 2
    fi
    harness_source="${repo_root}/artifacts/consumer-harness"
    if [[ ! -d "${harness_source}" || -L "${harness_source}" ]] ||
      [[ -n "$(find "${harness_source}" -type l -print -quit)" ]]; then
      echo "Consumer harness input must be a directory without symbolic links." >&2
      exit 2
    fi
    cp -a -- "${harness_source}" "${task_root}/artifacts/consumer-harness"
  fi
fi
# Prepared developer qualification accepts only its two declared artifact inputs.
# Every runtime and archive byte is authenticated again in the private workspace.
if [[ "${command_name}" == developer-experience ]] &&
  [[ -n "${SHARPPROOF_DEVELOPER_HARNESS:-}${SHARPPROOF_QUALIFIED_CODEFIX_PACKAGES:-}" ]]; then
  if [[ "${SHARPPROOF_DEVELOPER_HARNESS:-}" != artifacts/developer-harness ||
    "${SHARPPROOF_QUALIFIED_CODEFIX_PACKAGES:-}" != artifacts/prepared-developer-packages ]]; then
    echo "Prepared developer inputs must use the explicit authenticated artifact paths." >&2
    exit 2
  fi
  for developer_input in developer-harness prepared-developer-packages; do
    input_source="${repo_root}/artifacts/${developer_input}"
    if [[ ! -d "${input_source}" || -L "${input_source}" ]] ||
      [[ -n "$(find "${input_source}" -type l -print -quit)" ]]; then
      echo "Prepared developer inputs must be directories without symbolic links." >&2
      exit 2
    fi
    cp -a -- "${input_source}" "${task_root}/artifacts/${developer_input}"
  done
fi
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
  format-shard-gate)
    input_source="${repo_root}/artifacts/formatting"
    if [[ ! -d "${input_source}" || -L "${input_source}" ]] ||
      [[ -n "$(find "${input_source}" -type l -print -quit)" ]]; then
      echo "Formatting receipts must be directories without symbolic links." >&2
      exit 2
    fi
    cp -a -- "${input_source}" "${task_root}/artifacts/formatting"
    ;;
  package-reproducibility)
    reproduction_mode=""
    previous_argument=""
    for argument in "$@"; do
      if [[ "${previous_argument,,}" == -reproductionmode ]]; then
        reproduction_mode="${argument,,}"
        break
      fi
      previous_argument="${argument}"
    done
    if [[ "${reproduction_mode}" == compare ]]; then
      input_source="${repo_root}/artifacts/reproducibility-inputs"
      if [[ ! -d "${input_source}" || -L "${input_source}" ]] ||
        [[ -n "$(find "${input_source}" -type l -print -quit)" ]]; then
        echo "Reproduction inputs must be directories without symbolic links." >&2
        exit 2
      fi
      cp -a -- "${input_source}" "${task_root}/artifacts/reproducibility-inputs"
    fi
    ;;
esac
if [[ "${source_has_git}" = "true" ]] &&
  [[ -d "${task_root}/.git" ]]; then
  # Keep generated task artifacts outside tracked-file discovery.
  printf '/artifacts\n' >> "${task_root}/.git/info/exclude"
  if [[ "${command_name}" == developer-experience && -n "${SHARPPROOF_DEVELOPER_HARNESS:-}" ]]; then
    # These exact data inputs are authenticated separately. Keep other
    # untracked files visible to the clean-source and runtime-receipt checks.
    printf '%s\n' \
      '/nupkgs/packages.tsv' '/nupkgs/packages.lock.json' \
      '/nupkgs/Test-SharpProofPublishedPackages.sh' \
      '/tool-nupkgs/packages.tsv' '/tool-nupkgs/packages.lock.json' \
      '/tool-nupkgs/Test-SharpProofPublishedPackages.sh' \
      '/consumer-image.json' >> "${task_root}/.git/info/exclude"
  fi
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
input_excludes=()
case "${command_name}" in
  package-consumers)
    if [[ -n "${consumer_harness:-}" ]]; then
      # The harness is an authenticated, runner-owned input. Export only the
      # new qualification evidence, never overwrite the downloaded payload.
      result_paths=(package-consumers/harness-results)
    else
      result_paths=(.)
    fi
    ;;
  package-partition-gate)
    result_paths=()
    ;;
  canonical-cohort-gate)
    result_paths=(canonical-cohorts/canonical-cohort-validation.json)
    ;;
  coverage-gate)
    result_paths=(coverage/coverage-authority.json coverage/SharpProof.coverage.json)
    ;;
  format-shard-gate)
    result_paths=(formatting/formatting-validation.json)
    ;;
  package-reproducibility)
    result_paths=(reproducibility)
    ;;
  developer-experience)
    result_paths=(.)
    input_excludes=(--exclude='./developer-harness' --exclude='./prepared-developer-packages')
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
  "${input_excludes[@]}" \
  -cf - "${export_paths[@]}" | tar -C "${repo_root}/artifacts" -xf -
exit "${task_status}"
