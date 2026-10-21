#!/usr/bin/env bash
set -euo pipefail
cd /workspace/SharpProof
export SHARPPROOF_HM_SOURCE_COMMIT
SHARPPROOF_HM_SOURCE_COMMIT=$(git rev-parse HEAD)
task_root=$(mktemp -d /tmp/hm-inference.XXXXXX)
tar --exclude=.git --exclude=artifacts -cf - . | tar -C "$task_root" -xf -
cd "$task_root"
dotnet restore SharpProof.HmInference.slnx
dotnet build SharpProof.HmInference.slnx --no-restore > build.log 2>&1 || {
  mkdir -p /workspace/SharpProof/artifacts/hm-inference
  cp build.log /workspace/SharpProof/artifacts/hm-inference/build.log
  grep -oE 'error [A-Z]+[0-9]+' build.log | sort | uniq -c || true
  head -45 build.log
  exit 1
}
mkdir -p /workspace/SharpProof/artifacts/hm-inference
cp build.log /workspace/SharpProof/artifacts/hm-inference/build.log
tail -5 build.log
output=$(find artifacts/bin/SharpProof.HmInference -name SharpProof.HmInference.dll -path '*/net10.0/*' | head -1)
test -n "$output"
mkdir -p /workspace/SharpProof/artifacts/hm-inference
bash src/SharpProof.HmInference/verify-projects.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/project-checks.txt
bash src/SharpProof.HmInference/verify-frameworks.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/framework-checks.txt
bash src/SharpProof.HmInference/verify-generators.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/generator-checks.txt
bash src/SharpProof.HmInference/verify-tool-builds.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/tool-build-checks.txt
bash src/SharpProof.HmInference/verify-unsafe.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/unsafe-checks.txt
bash src/SharpProof.HmInference/verify-constraints.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/constraint-checks.txt
bash src/SharpProof.HmInference/verify-aliases.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/alias-import-checks.txt
bash src/SharpProof.HmInference/verify-arrays.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/array-construction-checks.txt
bash src/SharpProof.HmInference/verify-partials.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/partial-method-checks.txt
bash src/SharpProof.HmInference/verify-callerconstraints.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/caller-constraint-checks.txt
bash src/SharpProof.HmInference/verify-nullablecallers.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/nullable-caller-checks.txt
bash src/SharpProof.HmInference/verify-nullablegroups.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/nullable-group-checks.txt
bash src/SharpProof.HmInference/verify-fixedarguments.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/fixed-argument-checks.txt
bash src/SharpProof.HmInference/verify-storageconversions.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/fixed-storage-checks.txt
bash src/SharpProof.HmInference/verify-joins.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/expression-join-checks.txt
bash src/SharpProof.HmInference/verify-localreturns.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/local-return-checks.txt
bash src/SharpProof.HmInference/verify-performance.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/performance-checks.txt
bash src/SharpProof.HmInference/verify-builder-layout.sh "$output" | tee /workspace/SharpProof/artifacts/hm-inference/builder-layout-checks.txt
dotnet "$output" --self-test | tee /workspace/SharpProof/artifacts/hm-inference/tests.txt
dotnet "$output" --samples | tee /workspace/SharpProof/artifacts/hm-inference/samples.txt
dotnet "$output" --hm-samples | tee /workspace/SharpProof/artifacts/hm-inference/hm-samples.txt
# Lock files are source inputs. Export only the new project's restore result.
cp src/SharpProof.HmInference/packages.lock.json /workspace/SharpProof/src/SharpProof.HmInference/packages.lock.json
cp src/SharpProof.HmInference.Analyzer/packages.lock.json /workspace/SharpProof/src/SharpProof.HmInference.Analyzer/packages.lock.json
cp src/SharpProof.HmInference.CodeFixes/packages.lock.json /workspace/SharpProof/src/SharpProof.HmInference.CodeFixes/packages.lock.json
cp src/SharpProof.HmInference.Core/packages.lock.json /workspace/SharpProof/src/SharpProof.HmInference.Core/packages.lock.json
bash src/SharpProof.HmInference/verify-package.sh
