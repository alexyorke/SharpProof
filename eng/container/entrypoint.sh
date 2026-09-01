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

source_has_git=false
if git_directory="$(git -c safe.directory="${repo_root}" -C "${repo_root}" \
  rev-parse --absolute-git-dir 2>/dev/null)"; then
  source_has_git=true
  git config --global --add safe.directory "${repo_root}"
  git config --global --add safe.directory "${git_directory}"
fi

if [[ "${command_name}" = "dev" ]]; then
  exec /bin/bash "$@"
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
  echo "SharpProof ${command_name} requires a Git checkout with an exact commit for source comparison or certifying evidence." >&2
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
      source_origin="$(git -C "${repo_root}" remote get-url origin 2>/dev/null || true)"
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
    exec pwsh -NoLogo -NoProfile -File ./scripts/Invoke-SharpProofContainer.ps1 \
      -Command "${command_name}" "$@"
