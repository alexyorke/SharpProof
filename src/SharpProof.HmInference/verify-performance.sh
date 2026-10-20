#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
source_commit=${SHARPPROOF_HM_SOURCE_COMMIT:-verification}
script=src/SharpProof.HmInference/benchmark-solution.ps1
common=(-Cli "$output" -SourceCommit "$source_commit" -Evidence "$evidence" -Projects 2 -Replicas 1 -Samples 1 -EnvironmentId verification-only)
pwsh -NoLogo -NoProfile -File "$script" "${common[@]}" -Label solution-verification-first
pwsh -NoLogo -NoProfile -File "$script" "${common[@]}" -Label solution-verification-repeat -Compare "$evidence/solution-verification-first.json"
if pwsh -NoLogo -NoProfile -File "$script" "${common[@]}" -Label solution-verification-first -Compare "$evidence/solution-verification-first.json" > "$evidence/solution-verification-preserve.log" 2>&1; then
  printf '%s\n' 'A comparison overwrote its own baseline.'
  exit 1
fi
grep -Fq 'Use a different Label' "$evidence/solution-verification-preserve.log"
bad=$(mktemp /tmp/hm-incomparable-baseline.XXXXXX.json)
sed -E 's/("fixture_sha256": ")[^"]+/\1mismatch/' "$evidence/solution-verification-first.json" > "$bad"
invalid_label=solution-verification-invalid-$RANDOM
if pwsh -NoLogo -NoProfile -File "$script" "${common[@]}" -Label "$invalid_label" -Compare "$bad" > "$evidence/solution-verification-invalid.log" 2>&1; then
  printf '%s\n' 'An incomparable performance baseline was accepted.'
  exit 1
fi
grep -Fq 'Incomparable baseline: fixture_sha256' "$evidence/solution-verification-invalid.log"
test ! -e "$evidence/$invalid_label-setup.log"
bad_script=$(mktemp /tmp/hm-quality-benchmark.XXXXXX.ps1)
# shellcheck disable=SC2016 # Literal PowerShell variable in the quality-failure fixture.
sed 's/$expected.Add(/$expected.Add("missing " + /' "$script" > "$bad_script"
quality_label=solution-verification-quality-$RANDOM
if pwsh -NoLogo -NoProfile -File "$bad_script" "${common[@]}" -Label "$quality_label" > "$evidence/solution-verification-quality.log" 2>&1; then
  printf '%s\n' 'An incorrect diagnostic set was accepted as a performance sample.'
  exit 1
fi
grep -Fq 'Quality mismatch:' "$evidence/solution-verification-quality.log"
test ! -e "$evidence/$quality_label.json"
printf '%s\n' 'PASS CliSolutionScaleQualityAndComparablePerformanceEvidence'
