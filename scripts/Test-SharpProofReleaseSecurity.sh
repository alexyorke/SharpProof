#!/usr/bin/env bash
set -euo pipefail

# Process/transport stubs exercise policy control flow, not cryptographic claims.
# Actual SDK repository-primary, wrong-pin and tamper probes are separate.
package_dir=${1:?}
remote_fixture=${2:?}
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT
mkdir "$scratch/bin"
cat > "$scratch/bin/git" << 'MOCK'
#!/usr/bin/env bash
case "$1" in
  rev-parse) echo 0123456789012345678901234567890123456789 ;;
  fetch) exit 0 ;;
  merge-base) exit "${MOCK_ANCESTRY:-0}" ;;
  *) exit 99 ;;
esac
MOCK
cat > "$scratch/bin/gh" << 'MOCK'
#!/usr/bin/env bash
if [[ "$*" == *toolchain-security.yml* ]]; then
  echo "${MOCK_IMAGE:-success}"
elif [[ "$*" == *coverage.yml* ]]; then
  echo "${MOCK_COVERAGE:-success}"
else
  echo "${MOCK_CI:-success}"
fi
MOCK
cat > "$scratch/bin/curl" << 'MOCK'
#!/usr/bin/env bash
while (($#)); do
  if [[ "$1" == --output ]]; then output=$2; shift 2; else shift; fi
done
cp "$MOCK_REMOTE_PACKAGE" "$output"
printf '%s' "${MOCK_HTTP_STATUS:-200}"
MOCK
cat > "$scratch/bin/dotnet" << 'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case "$*" in
  'nuget trust source '*)
    while (($#)); do
      if [[ "$1" == --configfile ]]; then config=$2; shift 2; else shift; fi
    done
    pin=$(printf 'a%.0s' {1..64})
    if [[ ${MOCK_BAD_PIN:-0} == 1 ]]; then pin=$(printf '0%.0s' {1..64}); fi
    {
      echo '<configuration><trustedSigners><repository name="NuGetOfficial" serviceIndex="https://api.nuget.org/v3/index.json">'
      if [[ ${MOCK_NO_CERTS:-0} != 1 ]]; then
        printf '    <certificate fingerprint="%s" hashAlgorithm="%s" allowUntrustedRoot="false" />\n' "$pin" "${MOCK_CERT_ALGORITHM:-SHA256}"
      fi
      echo '</repository></trustedSigners></configuration>'
    } > "$config"
    ;;
  'nuget verify '*)
    expected_pin=$(printf 'a%.0s' {1..64})
    [[ "$*" == *"--certificate-fingerprint $expected_pin"* ]] || exit 1
    if [[ ${MOCK_NO_HASH:-0} != 1 ]]; then printf 'Content hash: %s\n' "${MOCK_CONTENT_HASH:-$MOCK_EXPECTED_CONTENT_HASH}"; fi
    if [[ ${MOCK_DUPLICATE_HASH:-0} == 1 ]]; then printf 'Content hash: %s\n' "$MOCK_EXPECTED_CONTENT_HASH"; fi
    printf 'Signature type: %s\n' "${MOCK_SIGNATURE_TYPE:-Repository}"
    if [[ ${MOCK_EXTRA_SIGNATURE:-0} == 1 ]]; then echo 'Signature type: Author'; fi
    printf 'Service index: %s\n' "${MOCK_SERVICE_INDEX:-https://api.nuget.org/v3/index.json}"
    exit "${MOCK_VERIFY_EXIT:-0}"
    ;;
  *) exit 99 ;;
esac
MOCK
cat > "$scratch/bin/sleep" << 'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$scratch/bin/"*
export PATH="$scratch/bin:$PATH"
export GITHUB_REPOSITORY=alexyorke/SharpProof GITHUB_SHA=0123456789012345678901234567890123456789 DEFAULT_BRANCH=master
export MOCK_REMOTE_PACKAGE="$scratch/remote.nupkg"
export MOCK_EXPECTED_CONTENT_HASH
MOCK_EXPECTED_CONTENT_HASH=$(cut -f5 "$package_dir/packages.tsv")
cp "$remote_fixture" "$MOCK_REMOTE_PACKAGE"
[[ $(sha512sum "$MOCK_REMOTE_PACKAGE" | cut -d ' ' -f1) != $(cut -f4 "$package_dir/packages.tsv") ]]
checks=0
expect_success() {
  "$@" > "$scratch/output" 2>&1 || {
    cat "$scratch/output" >&2
    exit 1
  }
  checks=$((checks + 1))
}
expect_failure() {
  if "$@" > "$scratch/output" 2>&1; then
    echo "Unexpected security success: $*" >&2
    exit 1
  fi
  checks=$((checks + 1))
}
expect_success bash "$repo_root/scripts/Test-SharpProofReleaseCommit.sh"
expect_failure env MOCK_ANCESTRY=1 bash "$repo_root/scripts/Test-SharpProofReleaseCommit.sh"
expect_failure env MOCK_CI=failure bash "$repo_root/scripts/Test-SharpProofReleaseCommit.sh"
expect_failure env MOCK_COVERAGE=missing-or-pending bash "$repo_root/scripts/Test-SharpProofReleaseCommit.sh"
expect_failure env MOCK_COVERAGE=cancelled bash "$repo_root/scripts/Test-SharpProofReleaseCommit.sh"
expect_failure env MOCK_IMAGE=failure bash "$repo_root/scripts/Test-SharpProofReleaseCommit.sh"
expect_failure env GITHUB_REPOSITORY=someone/fork bash "$repo_root/scripts/Test-SharpProofReleaseCommit.sh"
expect_success bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
expect_success bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" published "$package_dir"
expect_success env MOCK_HTTP_STATUS=404 bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
expect_failure env MOCK_HTTP_STATUS=404 bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" published "$package_dir"
expect_failure env MOCK_HTTP_STATUS=403 bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
for override in MOCK_VERIFY_EXIT=1 MOCK_BAD_PIN=1 MOCK_NO_CERTS=1 MOCK_CERT_ALGORITHM=SHA512 \
  MOCK_NO_HASH=1 MOCK_DUPLICATE_HASH=1 MOCK_SIGNATURE_TYPE=Author MOCK_EXTRA_SIGNATURE=1 \
  MOCK_SERVICE_INDEX=https://other.example/v3/index.json; do
  expect_failure env "$override" bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
done
wrong_hash=$(printf 'different authenticated content' | openssl dgst -sha512 -binary | openssl base64 -A)
expect_failure env MOCK_CONTENT_HASH="$wrong_hash" bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
mkdir "$scratch/bad-feed"
cp "$package_dir/Fixture.1.0.0.nupkg" "$scratch/bad-feed/"
cut -f1-4 "$package_dir/packages.tsv" > "$scratch/bad-feed/packages.tsv"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$scratch/bad-feed"
awk '{ print $0 "\textra" }' "$package_dir/packages.tsv" > "$scratch/bad-feed/packages.tsv"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$scratch/bad-feed"
cat "$package_dir/packages.tsv" "$package_dir/packages.tsv" > "$scratch/bad-feed/packages.tsv"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$scratch/bad-feed"
awk -F '\t' -v OFS='\t' -v hash="$wrong_hash" '{ $5=hash; print }' "$package_dir/packages.tsv" > "$scratch/bad-feed/packages.tsv"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$scratch/bad-feed"
cp "$package_dir/Fixture.1.0.0.nupkg" "$scratch/original.nupkg"
printf 'modified local package' >> "$package_dir/Fixture.1.0.0.nupkg"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
cp "$scratch/original.nupkg" "$package_dir/Fixture.1.0.0.nupkg"
echo "Passed $checks release ancestry, qualification, repository-signature policy and package-content cases."
