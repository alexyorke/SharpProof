#!/usr/bin/env bash
set -euo pipefail

cd "${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
test -f /etc/sharpproof/container-contract.json
pwsh -NoLogo -NoProfile -File \
  ./scripts/Test-SharpProofContainerContract.ps1
sp restore

cat <<'EOF'
SharpProof development container is ready.
Use: sp build | sp portable-tests | sp worker-tests | sp package-tests
EOF
