#!/usr/bin/env bash
set -euo pipefail

if [[ "${SHARPPROOF_CONTAINER:-}" != "1" ||
  "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ||
  ! -f "${SHARPPROOF_CONTAINER_CONTRACT:-/nonexistent}" ]]; then
  echo 'SharpProof requires its canonical Linux amd64 consumer image.' >&2
  exit 125
fi

if [[ "$(id -u)" = "0" ]]; then
  for directory in \
    /workspace/consumer/.sharpproof-obj \
    /workspace/consumer/.sharpproof-bin \
    /workspace/consumer/artifacts \
    /home/sharpproof/.nuget/packages; do
    install -d -o sharpproof -g sharpproof "${directory}"
  done
  export HOME=/home/sharpproof
  exec runuser --user sharpproof --preserve-environment -- \
    /usr/local/bin/sharpproof-consumer "$@"
fi

exec dotnet "$@"
