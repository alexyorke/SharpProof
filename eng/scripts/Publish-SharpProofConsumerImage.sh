#!/usr/bin/env bash
set -euo pipefail
script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
commit=${1:?Expected source commit}
version_tag=${2:?Expected release tag}
expected_digest=${3:?Expected qualified image digest}
[[ "$commit" =~ ^[a-f0-9]{40}$ ]]
[[ "$version_tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.-]+)?$ ]]
[[ "$expected_digest" =~ ^sha256:[a-f0-9]{64}$ ]]
[[ ${GITHUB_REPOSITORY:-} == alexyorke/SharpProof && ${GITHUB_SHA:-} == "$commit" && ${GITHUB_REF:-} == "refs/tags/$version_tag" ]]
[[ -n ${GHCR_TOKEN:-} && -n ${GITHUB_ACTOR:-} ]]
repository=ghcr.io/alexyorke/sharpproof
source_image="$repository:sha-$commit"
version_image="$repository:$version_tag"
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT

inspect_reference() {
  local reference=$1 diagnostic actual
  if docker buildx imagetools inspect "$reference" --format '{{json .Manifest}}' > "$scratch/manifest.json" 2> "$scratch/inspect.log"; then
    actual=$(pwsh -NoLogo -NoProfile -File "$script_directory/Get-SharpProofConsumerImageDigest.ps1" -ManifestPath "$scratch/manifest.json")
    if [[ "$actual" != "$expected_digest" ]]; then
      printf 'Refusing to replace an existing consumer-image digest: %s\n' "$reference" >&2
      return 1
    fi
    printf 'present\n'
    return 0
  fi
  diagnostic=$(< "$scratch/inspect.log")
  # Only an explicit not-found response for this exact image permits a push.
  # Transport, authentication, token-service, malformed-output and other
  # registry failures must never be treated as an absent immutable alias.
  case "$diagnostic" in
    "ERROR: $reference: not found" | "$reference: not found" | \
      "ERROR: $reference: manifest unknown" | "ERROR: $reference: manifest unknown: manifest unknown" | \
      "ERROR: unexpected status from HEAD request to https://ghcr.io/v2/alexyorke/sharpproof/manifests/${reference##*:}: 404 Not Found")
      printf 'absent\n'
      ;;
    *)
      printf 'Remote consumer-image inspection did not establish an exact manifest-not-found response: %s\n' "$reference" >&2
      return 1
      ;;
  esac
}

printf '%s' "$GHCR_TOKEN" | docker login ghcr.io --username "$GITHUB_ACTOR" --password-stdin
# Check both names before the first registry mutation, including reruns.
source_state=$(inspect_reference "$source_image")
version_state=$(inspect_reference "$version_image")
if [[ "$source_state" == absent ]]; then docker push "$source_image"; fi
[[ $(inspect_reference "$source_image") == present ]]
if [[ "$version_state" == absent ]]; then
  docker tag "$source_image" "$version_image"
  docker push "$version_image"
fi
[[ $(inspect_reference "$version_image") == present ]]
printf 'Verified consumer image %s@%s and both immutable release aliases.\n' "$repository" "$expected_digest"
