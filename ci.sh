#!/usr/bin/env bash
# The whole CI, runnable locally. GitHub Actions runs each lane on its own
# runner by calling this script; nothing else in .github decides what runs.
#
#   ./ci.sh                 build the image, run every lane in order, merge coverage
#   ./ci.sh build projects  run the named lanes
#   ./ci.sh image           build the tooling image (or pull it; see pull_or_build_image)
#   ./ci.sh coverage        merge artifacts/coverage/**/lines.tsv into summary.md
#   ./ci.sh nightly fuzz-0  scheduled lanes (nightly.yml); release-packages (release.yml)
#   ./ci.sh --list          print the lane names as JSON (the CI matrix)
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
# Git Bash on Windows would otherwise rewrite container paths.
export MSYS_NO_PATHCONV=1
# compose.yaml names images after the Compose project, which defaults to the
# lowercased directory name.
project="${COMPOSE_PROJECT_NAME:-$(basename "$PWD" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9_-')}"
export COMPOSE_PROJECT_NAME="$project"
image="${SHARPPROOF_TOOLING_IMAGE:-${project}-tooling:local}"

lanes=(
  build
  worker-a1 worker-a4 worker-b projects-a projects-b gates-corpus-a gates-corpus-b gates-shards
  package-0 package-1 package-2 package-3 package-4 package-5
  self-apply repository evidence
)

# Extra `-f` Compose files for one compose_run call; lint() sets it locally.
compose_files=()

docker_host_path() {
  if command -v cygpath > /dev/null 2>&1; then
    cygpath -m "$1"
  else
    printf '%s\n' "$1"
  fi
}

compose_run() {
  local common git_directory relative pointer="" status=0 mounts=()
  common="$(git rev-parse --path-format=absolute --git-common-dir)" || return
  git_directory="$(git rev-parse --absolute-git-dir)" || return
  common="${common%/}"
  if [[ "$git_directory" != "$common" ]]; then
    relative="${git_directory#"$common/"}"
    if [[ "$relative" == "$git_directory" ]]; then
      echo "Worktree metadata must be inside its Git common directory." >&2
      return 2
    fi
    pointer="$(mktemp)" || return
    printf 'gitdir: /git-common/%s\n' "$relative" > "$pointer"
    chmod a+r "$pointer"
    mounts=(--volume "$(docker_host_path "$common"):/git-common:ro"
    --volume "$(docker_host_path "$pointer"):/workspace/SharpProof/.git:ro")
  fi
  docker compose "${compose_files[@]}" run --rm "${mounts[@]}" "$@" || status=$?
  if [[ -n "$pointer" ]]; then rm -f -- "$pointer"; fi
  return "$status"
}

tooling() {
  compose_run -e GITHUB_RUN_ID -e GITHUB_RUN_ATTEMPT -e GITHUB_SHA tooling "$@"
}

# Instrumented test runs stay below a hosted runner's 16 GB of RAM.
coverage_shard() {
  SHARPPROOF_CONTAINER_MEMORY_LIMIT="${SHARPPROOF_CONTAINER_MEMORY_LIMIT:-12g}" \
    SHARPPROOF_TMPFS_SIZE="${SHARPPROOF_TMPFS_SIZE:-4g}" \
    tooling coverage "$@"
}

# The key covers everything the windows-native stage reads: the stage text,
# its base image, the cross-build recipe, and the z3 section of the catalog.
image_key() {
  {
    grep '^ARG DOTNET_SDK_IMAGE=' eng/container/Dockerfile
    awk '/^FROM / { stage = ($0 == "FROM ${DOTNET_SDK_IMAGE} AS windows-native") } stage' \
      eng/container/Dockerfile
    git ls-files --stage eng/container/windows-native
    jq --sort-keys --compact-output .z3 eng/container/toolchain.json
  } | sha256sum | cut -c1-64 | sed 's/^/sharpproof-windows-native-/'
}

# Compiling Z3 for Windows is ~14 of an uncached build's 15 minutes. With
# SHARPPROOF_WINDOWS_NATIVE_CACHE set to a directory, reuse its payload, or
# save the freshly built one there. The toolchain stage re-verifies the DLLs
# against their pinned digests either way.
build_image() {
  local cache="${SHARPPROOF_WINDOWS_NATIVE_CACHE:-}" native_context=()
  if [[ -n "$cache" && -f "$cache/payload/win-x64/libz3.dll" && -f "$cache/payload/win-arm64/libz3.dll" ]]; then
    native_context=(--build-context "windows-native=$cache")
  fi
  docker buildx build --platform linux/amd64 --file eng/container/Dockerfile --target toolchain \
    --load --tag "$image" --build-arg USER_UID=1000 --build-arg USER_GID=1000 \
    "${native_context[@]}" --progress plain .
  if [[ -n "$cache" && ${#native_context[@]} -eq 0 ]]; then
    rm -rf "$cache" && mkdir -p "$cache"
    local container
    container="$(docker create "$image")"
    docker cp "$container:/opt/sharpproof/windows-native" "$cache/payload"
    docker rm "$container" > /dev/null
  fi
}

# With SHARPPROOF_IMAGE_REGISTRY set (CI sets ghcr.io/<owner>/sharpproof-tooling),
# reuse the image published for this exact eng/container tree, or build it.
# The tree hash selects a cache candidate; provenance authorizes its use.
# SHARPPROOF_IMAGE_PUSH=1 always builds, publishes and reports its digest for attestation.
pull_or_build_image() {
  local registry="${SHARPPROOF_IMAGE_REGISTRY:-}"
  if [[ -z "$registry" ]]; then
    build_image
    return
  fi
  local remote digest version
  remote="$registry:$(git ls-files --stage eng/container | sha256sum | cut -c1-64)"
  if [[ "${SHARPPROOF_IMAGE_PUSH:-}" != 1 ]]; then
    digest="$(docker buildx imagetools inspect "$remote" --format '{{.Manifest.Digest}}' 2> /dev/null || true)"
    version="$(gh --version 2> /dev/null | head -1 | awk '{print $3}' || true)"
    # CLI 2.102 fixes workflow-prefix and case-insensitive source-ref matching.
    if [[ "$digest" =~ ^sha256:[a-f0-9]{64}$ && "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] &&
      [[ "$(printf '%s\n' 2.102.0 "$version" | sort -V | head -1)" == 2.102.0 ]] &&
      gh attestation verify "oci://$registry@$digest" --repo alexyorke/SharpProof \
        --signer-workflow alexyorke/SharpProof/.github/workflows/ci.yml \
        --source-ref refs/heads/master --deny-self-hosted-runners &&
      docker pull --quiet --platform linux/amd64 "$registry@$digest"; then
      docker tag "$registry@$digest" "$image"
      return
    fi
    echo "::notice::No verified tooling cache available; building pinned sources."
  fi
  build_image
  if [[ "${SHARPPROOF_IMAGE_PUSH:-}" == 1 ]]; then
    docker tag "$image" "$remote"
    docker push --quiet "$remote"
    digest="$(docker buildx imagetools inspect "$remote" --format '{{.Manifest.Digest}}')"
    [[ "$digest" =~ ^sha256:[a-f0-9]{64}$ ]]
    printf 'image-name=%s\nimage-digest=%s\n' "$registry" "$digest" >> "$GITHUB_OUTPUT"
  fi
}

# Lint runs on the prepared tooling image; `docker compose build lint` would
# rebuild tooling through its service:tooling context. `compose run lint` would
# too, so the run uses an override that drops the lint service's build.
lint() {
  local compose_files=(-f compose.yaml -f eng/ci/compose.prebuilt-lint.yaml)
  docker buildx build --platform linux/amd64 --file eng/lint/Dockerfile --load \
    --tag "${project}-lint:local" --build-context "canonical_tooling=docker-image://$image" \
    --progress plain . &&
    compose_run -e GITHUB_TOKEN lint
}

# One container builds both evidence projects; their dependency closures
# almost coincide, so separate containers built them twice.
api_evidence() {
  tooling test -Target SharpProof.ApiEvidence.Tests.slnf \
    -TestFilter 'FullyQualifiedName~DotnetApiInventoryTests|FullyQualifiedName~ApiSpecRuntimeOracleTests' &&
    test -s artifacts/api-coverage/catalog-test-evidence.json &&
    test -s artifacts/api-coverage/intrinsic-test-evidence.json
}

scan_image() {
  mkdir -p artifacts &&
    docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$PWD:/repo" -w /repo \
      anchore/grype:v0.120.1@sha256:e4a44ef45d285b829ce6efe2642980329661bd2d18eab5fc539138d4adaebbbe \
      "docker:$image" --config .grype.yaml --only-fixed --fail-on high \
      --output table --output json=artifacts/toolchain-vulnerabilities.json
}

# Run every check, then fail if any did, so one CI run reports them all.
# errexit is off inside `check || ...`, so multi-command checks chain with &&.
all_of() {
  local failed=()
  for check in "$@"; do
    echo "::group::$check"
    "$check" || failed+=("$check")
    echo "::endgroup::"
  done
  if ((${#failed[@]})); then
    echo "Failed: ${failed[*]}" >&2
    return 1
  fi
}
# One LongFuzz shard: a disjoint 250,000-seed range from LONGFUZZ_SEED_BASE
# (random by default) for LONGFUZZ_MINUTES (default 300, at most 320).
fuzz_shard() {
  local shard="$1" out per_shard=250000
  local minutes="${LONGFUZZ_MINUTES:-300}" base="${LONGFUZZ_SEED_BASE:-$((RANDOM * 32768 + RANDOM))}"
  if ! [[ "$minutes" =~ ^[0-9]+$ ]] || ((minutes < 1 || minutes > 320)); then
    echo "::error::LONGFUZZ_MINUTES must be an integer from 1 to 320." >&2
    return 2
  fi
  if ! [[ "$base" =~ ^[0-9]+$ ]] || ((base > 2000000000)); then
    echo "::error::LONGFUZZ_SEED_BASE must be an integer from 0 to 2000000000." >&2
    return 2
  fi
  local start=$((base + shard * per_shard))
  local end=$((start + per_shard))
  mkdir -p artifacts/long-fuzz
  out="$(mktemp -d "$PWD/artifacts/long-fuzz/shard-${shard}.XXXXXXXX")"
  echo "LongFuzz shard $shard: seeds [$start, $end), $minutes minutes."
  compose_run --volume "$(docker_host_path "$out"):/workspace/LongFuzzOutput" \
    -e SHARPPROOF_LONGFUZZ_SEED_START="$start" -e SHARPPROOF_LONGFUZZ_SEED_END="$end" \
    -e SHARPPROOF_LONGFUZZ_MINUTES="$minutes" -e SHARPPROOF_LONGFUZZ_OUTPUT=/workspace/LongFuzzOutput \
    tooling test -Target src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj -TestFilter TestCategory=LongFuzz &
  local fuzz_pid=$! status=0
  # vstest does not stream test output, so follow the harness event log;
  # its ::error:: lines become live annotations in CI.
  tail -n +1 -F --pid="$fuzz_pid" "$out/events.log" 2> /dev/null &
  local tail_pid=$!
  wait "$fuzz_pid" || status=$?
  wait "$tail_pid" || true

  local hits="$out/hits.jsonl" errors=0
  {
    echo "## LongFuzz shard $shard"
    echo
    echo "| Harness | Cases | Seeds | Minutes | Hits |"
    echo "| --- | ---: | --- | ---: | ---: |"
    for stats in "$out"/stats-*.json; do
      [[ -f "$stats" ]] || continue
      jq -r '"| \(.Harness) | \(.Cases) | \(.SeedStart)..\(.LastSeed) | \(.ElapsedMinutes) | \(.Hits) |"' "$stats"
    done
    echo
    if [[ -s "$hits" ]]; then
      echo "| Harness | Kind | Severity | Count |"
      echo "| --- | --- | --- | ---: |"
      jq -rs 'group_by([.Harness, .Kind, .Severity])[]
        | "| \(.[0].Harness) | \(.[0].Kind) | \(.[0].Severity) | \(length) |"' "$hits"
      echo
      echo "First findings (full sources are in ${out#"$PWD/"}):"
      echo
      jq -rs '.[0:25][]
        | "- `\(.Harness)` seed \(.Seed) **\(.Kind)** \(.Claim | gsub("[`|\n]"; " ") | .[0:120]) -> `\(.SourceFile)`"' "$hits"
    else
      echo "No findings."
    fi
  } | tee -a "${GITHUB_STEP_SUMMARY:-/dev/null}"
  if [[ -s "$hits" ]]; then
    errors=$(jq -s '[.[] | select(.Severity == "error")] | length' "$hits")
  fi
  echo "$errors error finding(s)."
  ((status == 0 && errors == 0))
}

# Pack the product and developer packages, then run the package tests
# against that packed feed (task workspaces copy nupkgs/, not artifacts/).
# The consumer image is the consumer stage on top of the exact tooling image
# this run packs with. Export an OCI archive before packaging so templates
# pin its manifest digest without granting the build any registry write access.
consumer_image() {
  local repository="${SHARPPROOF_CONSUMER_REPOSITORY:-ghcr.io/alexyorke/sharpproof-preview}"
  local version digest skopeo
  # The host needs only Docker: PowerShell runs in the tooling image.
  version="$(docker run --rm --platform linux/amd64 --entrypoint pwsh -e SHARPPROOF_CI_PACKAGE_VERSION \
    --volume "$(docker_host_path "$PWD"):/repo:ro" --workdir /repo "$image" -NoLogo -NoProfile -Command \
    'Import-Module ./eng/scripts/SharpProof.ReleaseConfiguration.psm1; Get-SharpProofEffectivePackageVersion -RepositoryRoot .')" || return
  mkdir -p artifacts/consumer-image
  docker buildx build --platform linux/amd64 --file eng/container/Dockerfile --target consumer \
    --build-context "toolchain=docker-image://$image" \
    --build-arg "SHARPPROOF_PACKAGE_VERSION=$version" \
    --tag "$repository:$version" --load --progress plain . || return
  docker run --rm --platform linux/amd64 "$repository:$version" --info > /dev/null || return
  skopeo="$(jq -r .skopeo eng/security/release-tools.json)"
  docker run --rm --platform linux/amd64 -v /var/run/docker.sock:/var/run/docker.sock \
    -v "$(docker_host_path "$PWD/artifacts/consumer-image"):/out" "$skopeo" copy \
    "docker-daemon:$repository:$version" oci-archive:/out/image.tar:consumer || return
  digest="$(docker run --rm --platform linux/amd64 \
    -v "$(docker_host_path "$PWD/artifacts/consumer-image"):/out:ro" "$skopeo" inspect \
    --format '{{.Digest}}' oci-archive:/out/image.tar)" || return
  if [[ ! "$digest" =~ ^sha256:[a-f0-9]{64}$ ]]; then
    echo "The consumer image build reported no digest." >&2
    return 1
  fi
  SHARPPROOF_CONSUMER_IMAGE="$repository@$digest"
  echo "$SHARPPROOF_CONSUMER_IMAGE" > artifacts/consumer-image/reference.txt
  echo "Consumer image staged for approval: $SHARPPROOF_CONSUMER_IMAGE"
}

release_packages() {
  # The canonical entrypoint normalizes repository/artifacts ownership to
  # UID 1000. Hosted runners use a different UID: create host-written export
  # directories before entering the container, and retain their ownership.
  mkdir -p nupkgs artifacts/release-evidence artifacts/consumer-image
  rm -f nupkgs/*.nupkg nupkgs/*.snupkg
  if [[ -z "${SHARPPROOF_CONSUMER_IMAGE:-}" ]]; then consumer_image || return; fi
  tooling pack -Configuration Release
  compose_run -e GITHUB_RUN_ID -e GITHUB_RUN_ATTEMPT -e GITHUB_SHA \
    -e "SHARPPROOF_CONSUMER_IMAGE=$SHARPPROOF_CONSUMER_IMAGE" tooling pack-tool -Configuration Release
  cp artifacts/container-packages/*.nupkg artifacts/container-packages/*.snupkg nupkgs/
  tooling package-tests -Configuration Release -PackageSource nupkgs \
    -TestFilter 'TestCategory!=Performance&TestCategory!=Coverage&TestCategory!=Corpus'
  cp artifacts/tool-packages/*.nupkg nupkgs/
  bash eng/scripts/release-evidence.sh
}

docs() { tooling docs; }
layout() { tooling layout; }
tooling_regressions() { bash tests/tooling/run.sh -v; }
# The consumer workflow's baseline helper; nothing else runs its tests.
consumer_baseline() { bash eng/scripts/Test-SharpProofConsumerBaseline.sh; }
isolated_coverage() { tooling coverage -CoveragePart Isolated; }

run_lane() {
  case "$1" in
    # Dependency audit, then the full analyzer build with -warnaserror, which
    # format-check relies on for everything beyond whitespace.
    build) tooling security -Configuration Release && tooling format-check ;;
    # Each pair below partitions one task group, split to keep lanes near 5 minutes.
    worker-a1) coverage_shard -CoveragePart Semantic -TaskGroup WorkerA1 ;;
    worker-a4) coverage_shard -CoveragePart Semantic -TaskGroup WorkerA4 ;;
    worker-b) coverage_shard -CoveragePart Semantic -TaskGroup WorkerB ;;
    projects-a) coverage_shard -CoveragePart Semantic -TaskGroup ProjectsA ;;
    projects-b) coverage_shard -CoveragePart Semantic -TaskGroup ProjectsB ;;
    gates-corpus-a) coverage_shard -CoveragePart Semantic -TaskGroup GatesCorpusA ;;
    gates-corpus-b) coverage_shard -CoveragePart Semantic -TaskGroup GatesCorpusB ;;
    gates-shards) coverage_shard -CoveragePart Semantic -TaskGroup GatesShards ;;
    package-[0-5]) coverage_shard -CoveragePart Package -PackagePartitionCount 6 -PackagePartition "${1#package-}" ;;
    self-apply) tooling self-apply -Configuration Release ;;
    repository)
      local status=0
      all_of lint docs layout tooling_regressions consumer_baseline scan_image || status=$?
      if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
        cat artifacts/lint/summary.md >> "$GITHUB_STEP_SUMMARY" 2> /dev/null || true
      fi
      return "$status"
      ;;
    evidence)
      local status=0
      all_of api_evidence isolated_coverage || status=$?
      if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
        cat artifacts/api-coverage/*.md >> "$GITHUB_STEP_SUMMARY" 2> /dev/null || true
      fi
      return "$status"
      ;;
    nightly)
      local nightly_status=0
      tooling nightly || nightly_status=$?
      if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
        cat artifacts/perf/summary.md >> "$GITHUB_STEP_SUMMARY" 2> /dev/null || true
        cat artifacts/perf/self-summary.md >> "$GITHUB_STEP_SUMMARY" 2> /dev/null || true
      fi
      return "$nightly_status"
      ;;
    fuzz-[0-9]*) fuzz_shard "${1#fuzz-}" ;;
    release-packages) release_packages ;;
    consumer-image) consumer_image ;;
    image) pull_or_build_image ;;
    image-key) image_key ;;
    # The floor applies to the merged total only; a single shard never sees it.
    coverage) pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProofCoverage.ps1 -Part Merge -MinimumLinePercent 98.5 ;;
    *)
      echo "Unknown lane '$1'. Lanes: ${lanes[*]} coverage nightly fuzz-<n> release-packages consumer-image image image-key" >&2
      return 2
      ;;
  esac
}

if [[ "${1:-}" == --list ]]; then
  printf '["%s"' "${lanes[0]}"
  printf ',"%s"' "${lanes[@]:1}"
  printf ']\n'
elif (($#)); then
  for lane in "$@"; do run_lane "$lane"; done
else
  rm -rf artifacts/coverage
  build_image
  for lane in "${lanes[@]}"; do run_lane "$lane"; done
  run_lane coverage
fi
