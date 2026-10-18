#!/usr/bin/env bash
set -euo pipefail
if [[ $# != 6 ]]; then
  echo 'Usage: Test-SharpProofReleaseAttestations.sh packages evidence repository commit provenance-bundle sbom-bundle' >&2
  exit 2
fi
source_directory=$1
evidence_directory=$2
repository=$3
source_commit=$4
provenance_bundle=$5
sbom_bundle=$6
[[ "$repository" == alexyorke/SharpProof && "$source_commit" =~ ^[0-9a-f]{40}$ ]]
shopt -s nullglob
packages=("$source_directory"/*.nupkg "$source_directory"/*.snupkg)
[[ ${#packages[@]} == 5 ]]
cp "$provenance_bundle" "$evidence_directory/provenance.sigstore.json"
cp "$sbom_bundle" "$evidence_directory/sbom.sigstore.json"
policy=(--repo "$repository" --signer-workflow "$repository/.github/workflows/package-consumers.yml" --source-digest "$source_commit" --deny-self-hosted-runners)
for package in "${packages[@]}"; do
  gh attestation verify "$package" --bundle "$provenance_bundle" "${policy[@]}"
  gh attestation verify "$package" --bundle "$sbom_bundle" "${policy[@]}" --predicate-type https://spdx.dev/Document/v2.3
done
for evidence in "$evidence_directory/sbom.spdx.json" "$evidence_directory/release-manifest.json"; do
  gh attestation verify "$evidence" --bundle "$provenance_bundle" "${policy[@]}"
done
