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

git config --global --add safe.directory "${repo_root}"
if git_directory="$(git -C "${repo_root}" rev-parse --absolute-git-dir 2>/dev/null)"; then
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

if requires_clean_exact_commit_source "${command_name}"; then
  assert_clean_exact_commit_source
fi

case "${command_name}" in
  *)
    task_root="$(mktemp -d /tmp/sharpproof-task.XXXXXXXX)"
    mkdir -p "${repo_root}/artifacts"
    git clone --quiet --shared --no-checkout "${repo_root}" "${task_root}"
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
    while IFS= read -r -d '' deleted_path; do
      rm -f -- "${task_root}/${deleted_path}"
    done < <(git -C "${repo_root}" diff \
      --no-renames --name-only --diff-filter=D -z HEAD --)
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
    ln -s "${repo_root}/artifacts" "${task_root}/artifacts"
    git config --global --add safe.directory "${task_root}"
    export SHARPPROOF_REPO_ROOT="${task_root}"
    cd "${task_root}"
    exec pwsh -NoLogo -NoProfile -File ./scripts/Invoke-SharpProofContainer.ps1 \
      -Command "${command_name}" "$@"
    ;;
esac
