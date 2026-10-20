#!/usr/bin/env bash
set -euo pipefail
cd /workspace/SharpProof
export SHARPPROOF_HM_SOURCE_COMMIT
SHARPPROOF_HM_SOURCE_COMMIT=$(git rev-parse HEAD)
task_root=$(mktemp -d /tmp/hm-solution-performance.XXXXXX)
tar --exclude=.git --exclude=artifacts -cf - . | tar -C "$task_root" -xf -
cd "$task_root"
dotnet restore SharpProof.HmInference.slnx > restore.log 2>&1 || {
  tail -35 restore.log
  exit 1
}
dotnet build SharpProof.HmInference.slnx --no-restore > build.log 2>&1 || {
  tail -45 build.log
  exit 1
}
output=$(find artifacts/bin/SharpProof.HmInference -name SharpProof.HmInference.dll -path '*/net10.0/*' | head -1)
pwsh -NoLogo -NoProfile -File src/SharpProof.HmInference/benchmark-solution.ps1 -Cli "$output" -SourceCommit "$SHARPPROOF_HM_SOURCE_COMMIT" -Evidence /workspace/SharpProof/artifacts/hm-inference "$@"
