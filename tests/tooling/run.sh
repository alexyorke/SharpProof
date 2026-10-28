#!/usr/bin/env bash
# Run command-planning regressions inside the canonical image with pinned Python.
# Usage: COMPOSE_PROJECT_NAME=<prepared-project> bash tests/tooling/run.sh
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
docker_repository="$repository"
case "$(uname -s)" in
  MINGW* | MSYS* | CYGWIN*)
    docker_repository=$(cygpath -m "$repository")
    export MSYS_NO_PATHCONV=1
    ;;
esac
tooling_image="${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}"
test_image="${COMPOSE_PROJECT_NAME}-tooling-tests:local"
if [[ "$(docker image inspect --format '{{.Os}}/{{.Architecture}}' "$tooling_image")" != linux/amd64 ]]; then
  echo 'Prepare the canonical Linux amd64 tooling image before tooling regressions.' >&2
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

# The mocks are executable scripts in /tmp; the separate workspace tmpfs lets
# the loop regressions exercise the real /workspace path guard without writes
# to the checked-out source or a persistent build volume.
docker run --rm \
  --platform linux/amd64 \
  --network none \
  --read-only \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  --mount "type=bind,source=$docker_repository,target=/workspace/SharpProof,readonly" \
  --tmpfs /tmp:rw,exec,nosuid,nodev,size=128m \
  --tmpfs /workspace/ToolingTests:rw,nosuid,nodev,uid=1000,gid=1000,size=16m \
  --env SHARPPROOF_TOOLING_TEST_WORKSPACE=/workspace/ToolingTests \
  "$test_image" tests/tooling/test_tooling.py "$@"
