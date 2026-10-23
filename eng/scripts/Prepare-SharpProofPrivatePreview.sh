#!/usr/bin/env bash
# Trusted orchestration from the exact workflow checkout. Source build commands
# execute in Docker without Actions cache credentials or shared cache restore/save.
set -euo pipefail
[[ "$GITHUB_REPOSITORY" == alexyorke/SharpProof-preview && "${SHARPPROOF_PRIVATE_PREVIEW:-}" == true ]]
[[ "$GITHUB_EVENT_NAME" == workflow_dispatch && "$GITHUB_REF" == refs/heads/main ]]
[[ "$SHARPPROOF_SOURCE_COMMIT" =~ ^[a-f0-9]{40}$ && "$SHARPPROOF_WORKFLOW_COMMIT" == "$GITHUB_WORKFLOW_SHA" ]]
[[ "$(git rev-parse HEAD)" == "$SHARPPROOF_SOURCE_COMMIT" ]]
orchestration_root="$GITHUB_WORKSPACE/artifacts/private-release-orchestration"
[[ "$(git -C "$orchestration_root" rev-parse HEAD)" == "$SHARPPROOF_WORKFLOW_COMMIT" ]]
script_path="$(realpath -e -- "${BASH_SOURCE[0]}")"
[[ "$script_path" == "$orchestration_root/eng/scripts/Prepare-SharpProofPrivatePreview.sh" ]]
[[ -n "${RUNNER_TEMP:-}" && "$RUNNER_TEMP" == /* && -n "${GITHUB_OUTPUT:-}" && "$GITHUB_OUTPUT" == /* ]]
runner_temp="$(realpath -e -- "$RUNNER_TEMP")"
[[ "$runner_temp" != / ]]
native_path="$runner_temp/sharpproof-private-windows-native"
[[ ! -e "$native_path" ]]
mkdir -p -- "$native_path"
container=''
cleanup() {
  if [[ -n "$container" ]]; then docker rm --force "$container" > /dev/null; fi
}
trap cleanup EXIT
unset ACTIONS_RUNTIME_TOKEN ACTIONS_CACHE_URL ACTIONS_RUNTIME_URL ACTIONS_RESULTS_URL
docker buildx build --platform linux/amd64 --file eng/container/Dockerfile \
  --target toolchain --no-cache --load --tag "${COMPOSE_PROJECT_NAME}-tooling:local" \
  --build-arg USER_UID=1000 --build-arg USER_GID=1000 --progress plain .
container="$(docker create "${COMPOSE_PROJECT_NAME}-tooling:local")"
[[ "$container" =~ ^[a-f0-9]{64}$ ]]
docker cp "$container:/opt/sharpproof/windows-native" "$native_path/payload"
docker rm "$container" > /dev/null
container=''
test -f "$native_path/payload/win-x64/libz3.dll"
test -f "$native_path/payload/win-arm64/libz3.dll"
echo "path=$native_path" >> "$GITHUB_OUTPUT"
