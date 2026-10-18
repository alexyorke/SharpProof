#!/usr/bin/env bash
set -euo pipefail

# The caller supplies one real package and its generated manifest.
package_dir=${1:?}
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT
mkdir "$scratch/bin"
cat >"$scratch/bin/git" <<'MOCK'
#!/usr/bin/env bash
case "$1" in
  rev-parse) echo 0123456789012345678901234567890123456789 ;;
  fetch) exit 0 ;;
  merge-base) exit "${MOCK_ANCESTRY:-0}" ;;
  *) exit 99 ;;
esac
MOCK
cat >"$scratch/bin/gh" <<'MOCK'
#!/usr/bin/env bash
if [[ "$*" == *toolchain-security.yml* ]]; then
  echo "${MOCK_IMAGE:-success}"
elif [[ "$*" == *coverage.yml* ]]; then
  echo "${MOCK_COVERAGE:-success}"
else
  echo "${MOCK_CI:-success}"
fi
MOCK
cat >"$scratch/bin/curl" <<'MOCK'
#!/usr/bin/env bash
while (($#)); do
  if [[ "$1" == --output ]]; then
    output=$2
    shift 2
  else
    shift
  fi
done
cp "$MOCK_REMOTE_PACKAGE" "$output"
printf '%s' "${MOCK_HTTP_STATUS:-200}"
MOCK
cat >"$scratch/bin/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$scratch/bin/"*
export PATH="$scratch/bin:$PATH"
export GITHUB_REPOSITORY=alexyorke/SharpProof
export GITHUB_SHA=0123456789012345678901234567890123456789
export DEFAULT_BRANCH=master
export MOCK_REMOTE_PACKAGE="$scratch/remote.nupkg"
cp "$package_dir/Fixture.1.0.0.nupkg" "$MOCK_REMOTE_PACKAGE"
checks=0
expect_success() {
  "$@" >"$scratch/output" 2>&1 || {
    cat "$scratch/output" >&2
    exit 1
  }
  checks=$((checks + 1))
}
expect_failure() {
  if "$@" >"$scratch/output" 2>&1; then
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
printf 'different package' >"$MOCK_REMOTE_PACKAGE"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" published "$package_dir"
printf 'modified local package' >>"$package_dir/Fixture.1.0.0.nupkg"
expect_failure bash "$repo_root/scripts/Test-SharpProofPublishedPackages.sh" preflight "$package_dir"
echo "Passed $checks release ancestry, qualification and package-integrity cases."
