#!/usr/bin/env bash
set -euo pipefail

source_root="${SHARPPROOF_LOOP_SOURCE_ROOT:-/workspace/HostSource}"
target_root="${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
artifacts_root="${SHARPPROOF_LOOP_ARTIFACTS_ROOT:-/workspace/LoopArtifacts}"

if [[ $# -eq 0 ]]; then
  echo "Usage: sharpproof-loop <sp-command> [arguments...]" >&2
  exit 2
fi
if [[ ! -d "${source_root}" ]]; then
  echo "SharpProof loop source is missing: ${source_root}" >&2
  exit 125
fi

source_root="$(realpath -e "${source_root}")"
target_parent="$(realpath -e "$(dirname "${target_root}")")"
target_root="${target_parent}/$(basename "${target_root}")"
if [[ "${target_root}" = "/" || "${target_root}" = "${source_root}" ]]; then
  echo "SharpProof loop target must be a separate private workspace." >&2
  exit 125
fi
case "${target_root}" in
  /workspace/*) ;;
  *)
    echo "SharpProof loop target must remain under /workspace." >&2
    exit 125
    ;;
esac

lock_directory="/tmp/sharpproof-loop.lock"
lock_owner="${lock_directory}/owner"
if ! mkdir "${lock_directory}" 2>/dev/null; then
  owner_pid="$(cat "${lock_owner}" 2>/dev/null || true)"
  if [[ "${owner_pid}" =~ ^[0-9]+$ ]] && kill -0 "${owner_pid}" 2>/dev/null; then
    echo "Another SharpProof loop command owns the private build workspace." >&2
    exit 125
  fi
  rm -f -- "${lock_owner}"
  if ! rmdir "${lock_directory}" 2>/dev/null ||
    ! mkdir "${lock_directory}" 2>/dev/null; then
    echo "Another SharpProof loop command owns the private build workspace." >&2
    exit 125
  fi
fi
printf '%s\n' "$$" > "${lock_owner}"
release_lock() {
  rm -f -- "${lock_owner}"
  rmdir "${lock_directory}" 2>/dev/null || true
}
trap release_lock EXIT HUP INT TERM

trust_git_directory() {
  local path="$1"
  if ! git config --global --get-all safe.directory 2>/dev/null |
      grep -Fxq -- "${path}"; then
    git config --global --add safe.directory "${path}"
  fi
}

trust_git_directory "${source_root}"
if ! git -C "${source_root}" rev-parse --is-inside-work-tree \
    >/dev/null 2>&1; then
  echo "SharpProof loop source must be a Git checkout visible in Docker." >&2
  exit 125
fi
source_git_directory="$(git -C "${source_root}" rev-parse --absolute-git-dir)"
trust_git_directory "${source_git_directory}"
source_head="$(git -C "${source_root}" rev-parse HEAD)"

source_patch="$(mktemp /tmp/sharpproof-loop-source-patch.XXXXXXXX)"
source_manifest="$(mktemp /tmp/sharpproof-loop-source-files.XXXXXXXX)"
target_patch="$(mktemp /tmp/sharpproof-loop-target-patch.XXXXXXXX)"
target_manifest="$(mktemp /tmp/sharpproof-loop-target-files.XXXXXXXX)"
cleanup() {
  rm -f -- \
    "${source_patch}" \
    "${source_manifest}" \
    "${target_patch}" \
    "${target_manifest}"
  release_lock
}
trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

get_source_fingerprint() {
  local root="$1"
  local head="$2"
  local patch_path="$3"
  local manifest_path="$4"
  local patch_hash
  local untracked_hash

  git -C "${root}" diff \
    --binary --full-index --no-ext-diff HEAD -- . > "${patch_path}"
  git -C "${root}" ls-files -z \
    --others --exclude-standard -- > "${manifest_path}"
  patch_hash="$(sha256sum "${patch_path}" | cut -d ' ' -f 1)"
  untracked_hash="$(
    while IFS= read -r -d '' relative_path; do
      printf '%s\0' "${relative_path}"
      if [[ -L "${root}/${relative_path}" ]]; then
        printf '120000\0'
      elif [[ -x "${root}/${relative_path}" ]]; then
        printf '100755\0'
      else
        printf '100644\0'
      fi
      git hash-object --no-filters "${root}/${relative_path}"
    done < "${manifest_path}" |
      sha256sum | cut -d ' ' -f 1
  )"
  printf '%s\n%s\n%s\n' "${head}" "${patch_hash}" "${untracked_hash}" |
    sha256sum | cut -d ' ' -f 1
}

source_fingerprint="$(get_source_fingerprint \
  "${source_root}" \
  "${source_head}" \
  "${source_patch}" \
  "${source_manifest}")"

if [[ ! -d "${target_root}/.git" ]]; then
  if [[ -d "${target_root}" ]] &&
    find "${target_root}" -mindepth 1 -maxdepth 1 -print -quit |
      grep -q .; then
    echo "SharpProof loop workspace is nonempty but is not a Git checkout." >&2
    exit 125
  fi
  git clone --quiet --shared --no-checkout \
    "${source_root}" "${target_root}"
  git -C "${target_root}" config core.filemode false
fi

trust_git_directory "${target_root}"

if [[ -e "${target_root}/artifacts" &&
  ! -L "${target_root}/artifacts" ]]; then
  if find "${target_root}/artifacts" -mindepth 1 -print -quit |
      grep -q .; then
    echo "SharpProof loop artifacts path is unexpectedly nonempty." >&2
    exit 125
  fi
  rmdir "${target_root}/artifacts"
fi
if [[ ! -L "${target_root}/artifacts" ]]; then
  ln -s "${artifacts_root}" "${target_root}/artifacts"
fi
if ! grep -Fxq '/artifacts' "${target_root}/.git/info/exclude"; then
  printf '/artifacts\n' >> "${target_root}/.git/info/exclude"
fi

target_head="$(git -C "${target_root}" rev-parse HEAD)"
target_fingerprint="$(get_source_fingerprint \
  "${target_root}" \
  "${target_head}" \
  "${target_patch}" \
  "${target_manifest}")"
if [[ "${target_fingerprint}" != "${source_fingerprint}" ]]; then
  git -C "${target_root}" reset --hard --quiet
  git -C "${target_root}" checkout --quiet --detach "${source_head}"
  git -C "${target_root}" reset --hard --quiet "${source_head}"

  while IFS= read -r -d '' relative_path; do
    case "${relative_path}" in
      ""|/*|../*|*/../*)
        echo "Invalid path in the SharpProof loop target inventory." >&2
        exit 125
        ;;
    esac
    if ! grep -Fzxq -- "${relative_path}" "${source_manifest}"; then
      rm -f -- "${target_root}/${relative_path}"
    fi
  done < "${target_manifest}"

  if [[ -s "${source_patch}" ]]; then
    git -C "${target_root}" apply \
      --binary --whitespace=nowarn "${source_patch}"
  fi
  while IFS= read -r -d '' relative_path; do
    case "${relative_path}" in
      ""|/*|../*|*/../*)
        echo "Invalid path in the SharpProof loop source inventory." >&2
        exit 125
        ;;
    esac
    source_path="${source_root}/${relative_path}"
    target_path="${target_root}/${relative_path}"
    mkdir -p -- "$(dirname "${target_path}")"
    if [[ -d "${target_path}" && ! -L "${target_path}" ]]; then
      rm -rf -- "${target_path}"
    fi
    cp -a -- "${source_path}" "${target_path}"
  done < "${source_manifest}"
fi
rm -f -- "${target_root}/.git/sharpproof-loop-source-files"

export SHARPPROOF_REPO_ROOT="${target_root}"
cd "${target_root}"
sp "$@"
