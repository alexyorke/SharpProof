#!/usr/bin/env bash
set -euo pipefail
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
fixture=$(mktemp -d /tmp/sharpproof-image-publication.XXXXXXXX)
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/bin"
cat > "$fixture/bin/docker" << 'MOCK'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  login)
    cat > /dev/null
    printf 'login\n' >> "$MOCK_CALLS"
    ;;
  buildx)
    [[ $2 == imagetools && $3 == inspect && $5 == --format && $6 == '{{json .Manifest}}' ]]
    reference=$4
    name=${reference##*:}
    printf 'inspect %s\n' "$reference" >> "$MOCK_CALLS"
    if [[ -f $MOCK_STATE/$name ]]; then
      printf '{"digest":"%s"}\n' "$(< "$MOCK_STATE/$name")"
      exit 0
    fi
    if [[ $name == sha-* ]]; then mode=$MOCK_SOURCE_STATUS; else mode=$MOCK_VERSION_STATUS; fi
    case "$mode" in
      equal) printf '{"digest":"%s"}\n' "$EXPECTED_DIGEST" ;;
      different) printf '{"digest":"sha256:%064d"}\n' 1 ;;
      missing) printf 'ERROR: %s: not found\n' "$reference" >&2; exit 17 ;;
      manifest-unknown) printf 'ERROR: %s: manifest unknown: manifest unknown\n' "$reference" >&2; exit 17 ;;
      missing-404) printf 'ERROR: unexpected status from HEAD request to https://ghcr.io/v2/alexyorke/sharpproof/manifests/%s: 404 Not Found\n' "$name" >&2; exit 17 ;;
      transport) printf 'ERROR: ghcr.io: TLS handshake timeout\n' >&2; exit 17 ;;
      auth) printf 'ERROR: unauthorized: authentication required\n' >&2; exit 17 ;;
      token-404) printf 'ERROR: unexpected status from GET request to https://ghcr.io/token: 404 Not Found\n' >&2; exit 17 ;;
      malformed) printf '{"digest":"untrusted"}\n' ;;
      oversized)
        printf '{"digest":"%s","padding":"' "$EXPECTED_DIGEST"
        head -c 1048576 /dev/zero | tr '\0' 'x'
        printf '"}\n'
        ;;
      *) exit 91 ;;
    esac
    ;;
  push)
    printf 'push %s\n' "$2" >> "$MOCK_CALLS"
    digest=$EXPECTED_DIGEST
    if [[ ${MOCK_WRONG_PUSH_REF:-} == "$2" ]]; then digest=sha256:$(printf '%064d' 2); fi
    printf '%s' "$digest" > "$MOCK_STATE/${2##*:}"
    ;;
  tag) printf 'tag %s %s\n' "$2" "$3" >> "$MOCK_CALLS" ;;
  *) exit 92 ;;
esac
MOCK
chmod +x "$fixture/bin/docker"
export PATH="$fixture/bin:$PATH"
export GITHUB_REPOSITORY=alexyorke/SharpProof GITHUB_SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
export GITHUB_REF=refs/tags/v1.2.3-preview.7 GITHUB_ACTOR=fixture GHCR_TOKEN=fixture
export EXPECTED_DIGEST=sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
source_image=ghcr.io/alexyorke/sharpproof:sha-$GITHUB_SHA
version_image=ghcr.io/alexyorke/sharpproof:v1.2.3-preview.7
checks=0
run_case() {
  local name=$1 source=$2 version=$3 expected_status=$4 expected_pushes=$5 status=0
  export MOCK_STATE="$fixture/$name/state" MOCK_CALLS="$fixture/$name/calls"
  export MOCK_SOURCE_STATUS=$source MOCK_VERSION_STATUS=$version
  mkdir -p "$MOCK_STATE"
  : > "$MOCK_CALLS"
  bash -x "$script_directory/Publish-SharpProofConsumerImage.sh" "$GITHUB_SHA" v1.2.3-preview.7 "$EXPECTED_DIGEST" > "$fixture/$name/output" 2>&1 || status=$?
  local actual_pushes
  actual_pushes=$(grep -c '^push ' "$MOCK_CALLS" || true)
  if [[ ($expected_status == success && $status != 0) || ($expected_status == failure && $status == 0) || $actual_pushes != "$expected_pushes" ]]; then
    printf 'Publication fixture %s failed: expected=%s, status=%s, pushes=%s/%s.\n' "$name" "$expected_status" "$status" "$actual_pushes" "$expected_pushes" >&2
    tail -n 70 "$fixture/$name/output" >&2
    tail -n 30 "$MOCK_CALLS" >&2
    return 1
  fi
  checks=$((checks + 1))
}
run_case new missing missing success 2
run_case existing equal equal success 0
run_case mixed equal manifest-unknown success 1
run_case explicit-404 missing-404 missing-404 success 2
run_case source-conflict different missing failure 0
run_case version-conflict missing different failure 0
for failure in transport auth token-404 malformed oversized; do
  run_case "$failure" "$failure" missing failure 0
  run_case "version-$failure" missing "$failure" failure 0
done
export MOCK_WRONG_PUSH_REF=$source_image
run_case wrong-source-push missing missing failure 1
unset MOCK_WRONG_PUSH_REF
export MOCK_WRONG_PUSH_REF=$version_image
run_case wrong-version-push equal missing failure 1
unset MOCK_WRONG_PUSH_REF
printf 'Consumer-image publication policy regressions passed: %s reuse/conflict/not-found/auth/transport/malformed/post-push controls. Docker was mocked; no image or attestation was published.\n' "$checks"
