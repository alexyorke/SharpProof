#!/usr/bin/env bash
set -euo pipefail

repo_root="${SHARPPROOF_REPO_ROOT:-/workspace/SharpProof}"
command_name="${1:-build}"
if [[ $# -gt 0 ]]; then
  shift
fi

cd "${repo_root}"
exec pwsh -NoLogo -NoProfile -File \
  ./scripts/Invoke-SharpProofContainer.ps1 \
  -Command "${command_name}" "$@"
