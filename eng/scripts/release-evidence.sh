#!/usr/bin/env bash
# Build evidence from the exact OCI archive and package bytes that will ship.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
export MSYS_NO_PATHCONV=1
root="$PWD"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
scratch_mount="$scratch"
if command -v cygpath > /dev/null 2>&1; then root="$(cygpath -m "$root")"; fi
if command -v cygpath > /dev/null 2>&1; then scratch_mount="$(cygpath -m "$scratch")"; fi
syft="$(jq -r .syft eng/security/release-tools.json)"
grype="$(jq -r .grype eng/security/release-tools.json)"
mkdir -p artifacts/release-evidence
status=0
for subject in consumer packages; do
  source=oci-archive:/repo/artifacts/consumer-image/image.tar
  if [[ "$subject" == packages ]]; then source=dir:/repo/nupkgs; fi
  docker run --rm --platform linux/amd64 --user "$(id -u):$(id -g)" \
    -v "$root:/repo" -v "$scratch_mount:/tmp" -w /repo -e HOME=/tmp -e SYFT_CHECK_FOR_APP_UPDATE=false \
    -e SYFT_GOLANG_CAPTURE_SYMBOLS=extended-stdlib \
    -e SYFT_PACKAGE_NESTED_ARCHIVE_MAX_DEPTH=3 \
    -e SYFT_PACKAGE_NESTED_ARCHIVE_MAX_MEMORY_BYTES=536870912 \
    -e SYFT_PACKAGE_NESTED_ARCHIVE_MAX_DISK_BYTES=4294967296 \
    -e SYFT_DOTNET_EXCLUDE_PROJECT_REFERENCES=false "$syft" "$source" \
    -o "syft-json=/repo/artifacts/release-evidence/$subject.syft.json" \
    -o "spdx-json=/repo/artifacts/release-evidence/$subject.spdx.json"
  jq -e '.artifacts | length > 0' "artifacts/release-evidence/$subject.syft.json"
  # Retain all findings, including those without fixes. Apply the existing
  # fixed-high/critical policy separately to that complete report.
  docker run --rm --platform linux/amd64 -v "$root:/repo" -w /repo "$grype" \
    "sbom:artifacts/release-evidence/$subject.syft.json" --config .grype.yaml \
    --output json > "artifacts/release-evidence/$subject.vulnerabilities.json"
  if ! jq -e '[.matches[] | select((.vulnerability.severity == "High" or
    .vulnerability.severity == "Critical") and .vulnerability.fix.state == "fixed")] | length == 0' \
    "artifacts/release-evidence/$subject.vulnerabilities.json"; then
    echo "Fixable High/Critical findings in $subject:" >&2
    jq -r '.matches[] | select((.vulnerability.severity == "High" or
      .vulnerability.severity == "Critical") and .vulnerability.fix.state == "fixed") |
      [.vulnerability.id, .artifact.name, .artifact.version,
       (.vulnerability.fix.versions | join(","))] | @tsv' \
      "artifacts/release-evidence/$subject.vulnerabilities.json" >&2
    status=1
  fi
done
exit "$status"
