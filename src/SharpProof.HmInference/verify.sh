#!/usr/bin/env bash
set -euo pipefail
cd /workspace/SharpProof
task_root=$(mktemp -d /tmp/hm-inference.XXXXXX)
tar --exclude=.git --exclude=artifacts -cf - . | tar -C "$task_root" -xf -
cd "$task_root"
dotnet restore SharpProof.HmInference.slnx
dotnet build SharpProof.HmInference.slnx --no-restore -p:RunAnalyzersDuringBuild=false > build.log 2>&1 || { tail -60 build.log; exit 1; }
output=$(find artifacts/bin/SharpProof.HmInference -name SharpProof.HmInference.dll -path '*/net10.0/*' | head -1)
test -n "$output"
mkdir -p /workspace/SharpProof/artifacts/hm-inference
dotnet "$output" --self-test | tee /workspace/SharpProof/artifacts/hm-inference/tests.txt
dotnet "$output" --samples | tee /workspace/SharpProof/artifacts/hm-inference/samples.txt
dotnet "$output" --hm-samples | tee /workspace/SharpProof/artifacts/hm-inference/hm-samples.txt
# Lock files are source inputs. Export only the new project's restore result.
cp src/SharpProof.HmInference/packages.lock.json /workspace/SharpProof/src/SharpProof.HmInference/packages.lock.json
