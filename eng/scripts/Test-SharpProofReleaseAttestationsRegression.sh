#!/usr/bin/env bash
set -euo pipefail
expected_spdx_predicate=${1:-https://spdx.dev/Document/v2.3}
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
fixture=$(mktemp -d /tmp/sharpproof-attestation-fixture.XXXXXXXX)
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/bin" "$fixture/packages" "$fixture/evidence"
for name in A.nupkg B.nupkg C.nupkg B.snupkg C.snupkg; do printf fixture > "$fixture/packages/$name"; done
printf fixture > "$fixture/provenance.json"
printf fixture > "$fixture/sbom.json"
printf fixture > "$fixture/evidence/sbom.spdx.json"
printf fixture > "$fixture/evidence/release-manifest.json"
cat > "$fixture/bin/gh" << 'MOCK'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$CALL_LOG"
if [[ -n ${FAIL_SUBJECT:-} && $3 == "$FAIL_SUBJECT" ]]; then exit 17; fi
MOCK
chmod +x "$fixture/bin/gh"
export PATH="$fixture/bin:$PATH"
export CALL_LOG="$fixture/calls"
commit=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
arguments=("$fixture/packages" "$fixture/evidence" alexyorke/SharpProof "$commit" "$fixture/provenance.json" "$fixture/sbom.json")
bash "$script_directory/Test-SharpProofReleaseAttestations.sh" "${arguments[@]}"
[[ $(wc -l < "$CALL_LOG") == 12 ]]
[[ $(grep -F -c -- "--predicate-type $expected_spdx_predicate" "$CALL_LOG") == 5 ]]
[[ $(grep -c -- "--source-digest $commit --deny-self-hosted-runners" "$CALL_LOG") == 12 ]]
[[ $(grep -c -- '--signer-workflow alexyorke/SharpProof/.github/workflows/package-consumers.yml' "$CALL_LOG") == 12 ]]
if FAIL_SUBJECT="$fixture/packages/A.nupkg" bash "$script_directory/Test-SharpProofReleaseAttestations.sh" "${arguments[@]}"; then
  echo 'Attestation verifier failure was ignored' >&2
  exit 1
fi
echo 'Attestation policy regressions passed: every archive/evidence subject, signer/repository/source policy, SPDX predicate and fail-closed CLI errors. No real signatures were generated or verified by this fixture.'
