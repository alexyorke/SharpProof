#!/usr/bin/env bash
# Host helper: layer the pinned test runtime over the already qualified image.
# Usage: COMPOSE_PROJECT_NAME=<prepared-project> bash eng/scripts/Test-SharpProofConsumerBaseline.sh
set -euo pipefail

if [[ "${SHARPPROOF_CONTAINER:-}" == 1 ]]; then
  echo 'Run this helper on the Docker host after preparing the canonical tooling image.' >&2
  exit 2
fi
if [[ ! "${COMPOSE_PROJECT_NAME:-}" =~ ^[a-z0-9][a-z0-9_-]*$ ]]; then
  echo 'COMPOSE_PROJECT_NAME must name the already prepared canonical tooling image.' >&2
  exit 2
fi
repository=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)
if [[ ! -f "$repository/SharpProof.slnx" ||
  ! -f "$repository/eng/scripts/Test-SharpProofConsumerBaseline.py" ]]; then
  echo 'The consumer baseline test source is not a SharpProof workspace.' >&2
  exit 2
fi
docker_repository="$repository"
case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    docker_repository=$(cygpath -m "$repository")
    export MSYS_NO_PATHCONV=1
    ;;
esac
tooling_image="${COMPOSE_PROJECT_NAME}-tooling:local"
test_image="${COMPOSE_PROJECT_NAME}-consumer-baseline-tests:local"
if [[ "$(docker image inspect --format '{{.Os}}/{{.Architecture}}' "$tooling_image")" != linux/amd64 ]]; then
  echo 'Prepare the canonical Linux amd64 tooling image before baseline regressions.' >&2
  exit 2
fi

docker buildx build \
  --platform linux/amd64 \
  --file "$docker_repository/eng/container/ConsumerBaseline.Dockerfile" \
  --build-context "canonical_tooling=docker-image://$tooling_image" \
  --network none \
  --load \
  --tag "$test_image" \
  --progress plain \
  "$docker_repository"

docker run --rm \
  --platform linux/amd64 \
  --network none \
  --read-only \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  --mount "type=bind,source=$docker_repository,target=/workspace/SharpProof,readonly" \
  --tmpfs /tmp:rw,noexec,nosuid,nodev,size=64m \
  "$test_image"
