#!/usr/bin/env bash
set -euo pipefail

target="${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
seed="${SHARPPROOF_SEED_ROOT:-/workspace/seed}"
bundle="${seed}/.devcontainer/repository.bundle"
origin="${SHARPPROOF_ORIGIN_URL:-https://github.com/alexyorke/SharpProof.git}"

if [[ ! -f "${bundle}" ]] ||
   ! git bundle list-heads "${bundle}" HEAD | grep -qE '^[0-9a-f]{40} HEAD$'; then
  echo "The SharpProof seed bundle is unavailable or invalid: ${bundle}" >&2
  exit 125
fi

if [[ ! -d "${target}/.git" ]]; then
  if find "${target}" -mindepth 1 -maxdepth 1 -print -quit | grep -q .; then
    echo "The persistent SharpProof workspace is nonempty but is not a Git checkout: ${target}" >&2
    exit 125
  fi
  git clone "${bundle}" "${target}"
  git -C "${target}" remote set-url origin "${origin}"
fi

cd "${target}"
test -f /etc/sharpproof/container-contract.json
pwsh -NoLogo -NoProfile -File \
  ./scripts/Test-SharpProofContainerContract.ps1
sp restore

cat <<'EOF'
SharpProof persistent development volume is ready.
Use: sp build | sp portable-tests | sp worker-tests | sp package-tests
EOF
