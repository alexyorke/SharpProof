#!/usr/bin/env bash
set -euo pipefail

repo_root="${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
command_name="${1:-dev}"
if [[ $# -gt 0 ]]; then
  shift
fi

if [[ "$(id -u)" = "0" ]] && id sharpproof >/dev/null 2>&1; then
  install -d -o sharpproof -g sharpproof \
    /home/sharpproof/.nuget \
    /home/sharpproof/.nuget/NuGet \
    /home/sharpproof/.nuget/packages \
    /home/sharpproof/.dotnet \
    "${repo_root}"
  chown sharpproof:sharpproof \
    /home/sharpproof/.nuget \
    /home/sharpproof/.nuget/NuGet \
    /home/sharpproof/.nuget/packages \
    /home/sharpproof/.dotnet \
    "${repo_root}"
  if [[ "${command_name}" != "dev" ]]; then
    install -d -o sharpproof -g sharpproof "${repo_root}/artifacts"
    chown sharpproof:sharpproof "${repo_root}/artifacts"
  fi
  export HOME=/home/sharpproof
  exec runuser --user sharpproof --preserve-environment -- \
    /usr/local/bin/sharpproof-container "${command_name}" "$@"
fi

if [[ ! -f /etc/sharpproof/container-contract.json ]]; then
  echo "SharpProof canonical container contract is missing." >&2
  exit 125
fi

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
  echo "SharpProof verification requires the canonical linux/amd64 container." >&2
  exit 125
fi

resolve_linked_worktree_git_directory() {
  local dot_git_file="${repo_root}/.git"
  [[ -f "${dot_git_file}" ]] || return 1

  local git_pointer
  git_pointer="$(sed -n 's/^[[:space:]]*gitdir:[[:space:]]*//p' \
    "${dot_git_file}" | head -n 1 | tr -d '\r')"
  [[ -n "${git_pointer}" ]] || return 1

  if [[ "${git_pointer}" = /* && -d "${git_pointer}" ]]; then
    (cd "${git_pointer}" && pwd -P)
    return 0
  fi
  if [[ "${git_pointer}" != /* && -d "${repo_root}/${git_pointer}" ]]; then
    (cd "${repo_root}/${git_pointer}" && pwd -P)
    return 0
  fi

  local worktree_name="${git_pointer##*/}"
  [[ "${worktree_name}" =~ ^[A-Za-z0-9._-]+$ ]] || return 1
  local parent_root="${SHARPPROOF_GIT_PARENT_ROOT:-}"
  [[ -n "${parent_root}" && -d "${parent_root}" ]] || return 1

  local candidate
  candidate="$(find "${parent_root}" -xdev -type d \
    -path "*/.git/worktrees/${worktree_name}" -print -quit 2>/dev/null || true)"
  [[ -n "${candidate}" && -d "${candidate}" ]] || return 1
  (cd "${candidate}" && pwd -P)
}

source_has_git=false
linked_worktree=false
linked_git_directory=""
linked_common_directory=""
if [[ -f "${repo_root}/.git" ]]; then
  linked_worktree=true
  if linked_git_directory="$(resolve_linked_worktree_git_directory)"; then
    linked_common_directory="${linked_git_directory}"
    if [[ -f "${linked_git_directory}/commondir" ]]; then
      linked_common_pointer="$(tr -d '\r' < "${linked_git_directory}/commondir")"
      if [[ -n "${linked_common_pointer}" ]]; then
        if [[ "${linked_common_pointer}" = /* ]]; then
          linked_common_directory="${linked_common_pointer}"
        elif linked_common_directory="$(cd "${linked_git_directory}/${linked_common_pointer}" && pwd -P)"; then
          :
        else
          linked_common_directory=""
        fi
      fi
    fi
  fi
fi

git_source() {
  if [[ -n "${linked_git_directory}" && -n "${linked_common_directory}" ]]; then
    git --git-dir="${linked_git_directory}" \
      --work-tree="${repo_root}" "$@"
  else
    git -c safe.directory="${repo_root}" -C "${repo_root}" "$@"
  fi
}

if git_directory="$(git_source rev-parse --absolute-git-dir 2>/dev/null)"; then
  source_has_git=true
  git -C / config --global --add safe.directory "${repo_root}"
  git -C / config --global --add safe.directory "${git_directory}"
  if [[ -n "${linked_common_directory}" ]]; then
    git -C / config --global --add safe.directory "${linked_common_directory}"
  fi
fi

if [[ "${command_name}" = "dev" &&
  "${SHARPPROOF_DEV_CONTAINER:-}" = "1" ]]; then
  exec /bin/bash "$@"
fi

# corpus-update intentionally rewrites checked-in corpus evidence. Run it
# against the mounted checkout so the generated snapshot and importer files
# remain available to the caller after this task container exits.
if [[ "${command_name}" = "corpus-update" ]]; then
  export SHARPPROOF_REPO_ROOT="${repo_root}"
  cd "${repo_root}"
  exec pwsh -NoLogo -NoProfile -File ./scripts/Invoke-SharpProofContainer.ps1 \
    -Command "${command_name}" "$@"
fi

requires_clean_exact_commit_source() {
  case "$1" in
    acceptance|mutation|fuzz-nightly|pack|pilots|release-tag|release-baseline|release-plan|release-qualification|release-publish)
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
    pr-gates|test-changed|package-consumers|performance|coverage)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

assert_clean_exact_commit_source() {
  if ! git_source diff --quiet --ignore-submodules=none -- ||
    ! git_source diff --cached --quiet \
      --ignore-submodules=none HEAD --; then
    echo "SharpProof ${command_name} requires clean exact-commit source; tracked index or working-tree changes were found." >&2
    exit 2
  fi

  local untracked_file
  untracked_file="$(mktemp /tmp/sharpproof-untracked.XXXXXXXX)"
  if ! git_source ls-files \
    --others --exclude-standard -z -- > "${untracked_file}"; then
    rm -f -- "${untracked_file}"
    echo "SharpProof ${command_name} could not inspect Git untracked paths." >&2
    exit 2
  fi
  local untracked_path
  while IFS= read -r -d '' untracked_path; do
    case "${untracked_path}" in
      nupkgs/*)
        # Release commands receive the exact package-job artifacts here.
        ;;
      *)
        rm -f -- "${untracked_file}"
        echo "SharpProof ${command_name} requires clean exact-commit source; an untracked source path was found." >&2
        exit 2
        ;;
    esac
  done < "${untracked_file}"
  rm -f -- "${untracked_file}"
}

if requires_git_source "${command_name}" &&
  [[ "${source_has_git}" != "true" ]]; then
  if [[ "${linked_worktree}" = "true" ]]; then
    echo "SharpProof ${command_name} could not resolve linked-worktree Git metadata; mount the host repository's common .git directory at ${SHARPPROOF_GIT_PARENT_ROOT:-/workspace/SharpProof-host-parent}." >&2
  else
    echo "SharpProof ${command_name} requires a Git checkout with an exact commit for source comparison or certifying evidence." >&2
  fi
  exit 2
fi

if requires_clean_exact_commit_source "${command_name}"; then
  assert_clean_exact_commit_source
fi

case "${command_name}" in
  *)
    task_root="$(mktemp -d /tmp/sharpproof-task.XXXXXXXX)"
    mkdir -p "${repo_root}/artifacts"
    if [[ "${source_has_git}" = "true" ]]; then
      git_clone_source="${repo_root}"
      if [[ "${linked_worktree}" = "true" &&
        -n "${linked_common_directory}" ]]; then
        git_clone_source="${linked_common_directory}"
      fi
      git clone --quiet --shared --no-checkout \
        "${git_clone_source}" "${task_root}"
      git -C / config --global --add safe.directory "${task_root}"
      source_origin="$(git_source remote get-url origin 2>/dev/null || true)"
      if [[ -n "${source_origin}" ]]; then
        git -C "${task_root}" remote set-url origin "${source_origin}"
      fi
      # A mounted CI checkout commonly has a detached HEAD and keeps the
      # release base only as a remote-tracking ref. A local shared clone does
      # not copy those refs, so preserve the source ref namespace explicitly
      # without fetching from the network.
      refs_file="$(mktemp /tmp/sharpproof-refs.XXXXXXXX)"
      if ! git_source for-each-ref \
        --format='%(refname) %(objectname)' refs/remotes/ > "${refs_file}"; then
        rm -f -- "${refs_file}"
        echo "SharpProof ${command_name} could not inspect Git remote refs." >&2
        exit 2
      fi
      while IFS=' ' read -r ref object; do
        if [[ -n "${ref}" && -n "${object}" ]]; then
          git -C "${task_root}" update-ref "${ref}" "${object}"
        fi
      done < "${refs_file}"
      rm -f -- "${refs_file}"
      git -C "${task_root}" checkout --quiet --detach \
        "$(git_source rev-parse HEAD)"
      # Docker Desktop bind mounts do not preserve meaningful Git executable
      # bits. Ignore their synthetic working-tree modes in the disposable clone;
      # real mode changes committed between Git trees remain part of comparisons.
      git -C "${task_root}" config core.filemode false
      deleted_file="$(mktemp /tmp/sharpproof-deleted.XXXXXXXX)"
      if ! git_source diff \
        --no-renames --name-only --diff-filter=D -z HEAD -- > "${deleted_file}"; then
        rm -f -- "${deleted_file}"
        echo "SharpProof ${command_name} could not inspect Git deleted paths." >&2
        exit 2
      fi
      while IFS= read -r -d '' deleted_path; do
        rm -f -- "${task_root}/${deleted_path}"
      done < "${deleted_file}"
      rm -f -- "${deleted_file}"
    fi
    if requires_clean_exact_commit_source "${command_name}"; then
      # Exact-commit commands run from the detached Git tree above. Only the
      # explicitly supported package-input directory may be overlaid; using a
      # whole-worktree tar here would reintroduce ignored source files.
      if [[ -d "${repo_root}/nupkgs" ]]; then
        tar -C "${repo_root}" -cf - nupkgs |
          tar -C "${task_root}" -xf -
      fi
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
        -C "${repo_root}" -cf - . | tar -C "${task_root}" -xf -
    fi
    ln -s "${repo_root}/artifacts" "${task_root}/artifacts"
    if [[ "${source_has_git}" = "true" ]] &&
      [[ -d "${task_root}/.git" ]]; then
      # The disposable clone uses a symlink so evidence lands in the host
      # artifact mount. A trailing-slash ignore rule matches directories but
      # not this symlink, so exclude the exact task-local link explicitly.
      printf '/artifacts\n' >> "${task_root}/.git/info/exclude"
    fi
    export SHARPPROOF_REPO_ROOT="${task_root}"
    cd "${task_root}"
    if [[ "${command_name}" = "dev" ]]; then
      exec /bin/bash "$@"
    fi
    exec pwsh -NoLogo -NoProfile -File ./scripts/Invoke-SharpProofContainer.ps1 \
      -Command "${command_name}" "$@"
    ;;
esac
