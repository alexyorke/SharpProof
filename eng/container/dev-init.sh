#!/usr/bin/env bash
set -euo pipefail

target="${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
origin="${SHARPPROOF_ORIGIN_URL:-https://github.com/alexyorke/SharpProof.git}"
ref="${SHARPPROOF_DEV_REF:-}"

if [[ -z "${origin//[[:space:]]/}" ]]; then
  echo "SHARPPROOF_ORIGIN_URL must identify a Git repository." >&2
  exit 125
fi

if [[ ! -d "${target}/.git" ]]; then
  if find "${target}" -mindepth 1 -maxdepth 1 -print -quit | grep -q .; then
    echo "The persistent SharpProof workspace is nonempty but is not a Git checkout: ${target}" >&2
    exit 125
  fi

  clone_arguments=(clone)
  if [[ -n "${ref}" ]]; then
    clone_arguments+=(--branch "${ref}")
  fi
  clone_arguments+=("${origin}" "${target}")
  git "${clone_arguments[@]}"
fi

cd "${target}"
test -f /etc/sharpproof/container-contract.json
sp contract
sp restore

cat <<'EOF'
SharpProof persistent development volume is ready.
Use: sp build | sp portable-tests | sp worker-tests | sp package-tests
EOF
