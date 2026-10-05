#!/usr/bin/env bash
set -euo pipefail

source_root="${SHARPPROOF_LOOP_SOURCE_ROOT:-/workspace/HostSource}"
target_root="${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
artifacts_root="${SHARPPROOF_LOOP_ARTIFACTS_ROOT:-/workspace/LoopArtifacts}"
origin_url="${SHARPPROOF_ORIGIN_URL:-}"

if [[ $# -eq 0 ]]; then
  echo "Usage: sharpproof-loop <sp-command> [arguments...]" >&2
  exit 2
fi
if [[ ! -d "${source_root}" ]]; then
  echo "SharpProof loop source is missing: ${source_root}" >&2
  exit 125
fi
if [[ -z "${origin_url}" ||
  "${origin_url}" == *$'\n'* ||
  "${origin_url}" == *$'\r'* ]]; then
  echo "SharpProof loop origin URL is missing or invalid." >&2
  exit 125
fi

source_root="$(realpath -e "${source_root}")"
artifacts_root="$(realpath -e "${artifacts_root}")"
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

validate_relative_path() {
  local path="$1"
  local inventory="$2"
  case "${path}" in
    ""|/*|../*|*/../*)
      echo "Invalid path in the SharpProof loop ${inventory} inventory." >&2
      exit 125
      ;;
  esac
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

source_patch_temp="$(mktemp /tmp/sharpproof-loop-source-patch.XXXXXXXX)"
source_manifest_temp="$(mktemp /tmp/sharpproof-loop-source-files.XXXXXXXX)"
source_patch="${source_patch_temp}"
source_manifest="${source_manifest_temp}"
source_files_root="${source_root}"
target_manifest="$(mktemp /tmp/sharpproof-loop-target-files.XXXXXXXX)"
cleanup() {
  rm -f -- \
    "${source_patch_temp}" \
    "${source_manifest_temp}" \
    "${target_manifest}"
  release_lock
}
trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

snapshot_root="${SHARPPROOF_LOOP_SNAPSHOT_ROOT:-}"
if [[ -n "${snapshot_root}" ]]; then
  snapshot_root="$(realpath -e "${snapshot_root}")"
  case "${snapshot_root}" in
    "${artifacts_root}"/.sharpproof-loop-input-*) ;;
    *)
      echo "SharpProof loop snapshot escaped the artifacts input root." >&2
      exit 125
      ;;
  esac
  snapshot_head="$(tr -d '\r\n' < "${snapshot_root}/head")"
  if [[ "${snapshot_head}" != "${source_head}" ]]; then
    echo "SharpProof loop snapshot does not match the host HEAD." >&2
    exit 2
  fi
  source_patch="${snapshot_root}/source.patch"
  source_manifest="${snapshot_root}/source-files"
  source_files_root="${snapshot_root}/files"
  if [[ ! -f "${source_patch}" ||
    ! -f "${source_manifest}" ||
    ! -d "${source_files_root}" ]]; then
    echo "SharpProof loop snapshot is incomplete." >&2
    exit 125
  fi
else
  git -C "${source_root}" diff \
    --binary --full-index --no-ext-diff HEAD -- . > "${source_patch}"
  git -C "${source_root}" ls-files -z \
    --others --exclude-standard -- > "${source_manifest}"
  git -C "${source_root}" ls-files -z \
    --others --ignored --exclude-standard -- nupkgs/ >> "${source_manifest}"
fi

if [[ ! -d "${target_root}/.git" ]]; then
  if [[ -d "${target_root}" ]] &&
    find "${target_root}" -mindepth 1 -maxdepth 1 -print -quit |
      grep -q .; then
    echo "SharpProof loop workspace is nonempty but is not a Git checkout." >&2
    exit 125
  fi
  git clone --quiet --shared --no-checkout \
    "${source_root}" "${target_root}"
fi

trust_git_directory "${target_root}"
if git -C "${target_root}" remote get-url origin >/dev/null 2>&1; then
  git -C "${target_root}" remote set-url origin "${origin_url}"
else
  git -C "${target_root}" remote add origin "${origin_url}"
fi
# Unlike the Docker Desktop bind-mounted source, this private Linux volume
# preserves executable bits. Keep its tracked modes canonical so applying a
# host-captured patch does not warn about 100755 script inputs appearing 0644.
git -C "${target_root}" config core.filemode true

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

git -C "${target_root}" ls-files -z \
  --others --exclude-standard -- > "${target_manifest}"
git -C "${target_root}" ls-files -z \
  --others --ignored --exclude-standard -- nupkgs/ >> "${target_manifest}"
git -C "${target_root}" reset --hard --quiet
while IFS= read -r -d '' relative_path; do
  validate_relative_path "${relative_path}" target
  if ! grep -Fzxq -- "${relative_path}" "${source_manifest}"; then
    rm -f -- "${target_root}/${relative_path}"
  fi
done < "${target_manifest}"

git -C "${target_root}" checkout --quiet --detach "${source_head}"

if [[ -s "${source_patch}" ]]; then
  git -C "${target_root}" apply \
    --binary --whitespace=nowarn "${source_patch}"
fi
while IFS= read -r -d '' relative_path; do
  validate_relative_path "${relative_path}" source
  source_path="${source_files_root}/${relative_path}"
  target_path="${target_root}/${relative_path}"
  mkdir -p -- "$(dirname "${target_path}")"
  if [[ -d "${target_path}" && ! -L "${target_path}" ]]; then
    rm -rf -- "${target_path}"
  fi
  cp -a -- "${source_path}" "${target_path}"
done < "${source_manifest}"

export SHARPPROOF_REPO_ROOT="${target_root}"
cd "${target_root}"
sp "$@"
