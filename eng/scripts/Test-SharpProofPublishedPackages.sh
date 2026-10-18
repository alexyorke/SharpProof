#!/usr/bin/env bash
set -euo pipefail

# Qualified originals stay unchanged. NuGet.org adds a repository primary
# signature to unsigned submissions. Authenticate it and compare NuGet content
# hashes; never strip signatures or assert that the outer ZIP bytes are equal.
mode=${1:?Expected preflight or published}
package_dir=${2:?Expected package directory}
[[ "$mode" == preflight || "$mode" == published ]]
[[ -s "$package_dir/packages.tsv" ]]
awk -F '\t' 'NF != 5 { exit 1 }' "$package_dir/packages.tsv"
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT
export DOTNET_CLI_UI_LANGUAGE=en-US DOTNET_NUGET_SIGNATURE_VERIFICATION=true
trusted_config="$scratch/nuget.config"
fingerprint_arguments=()
declare -A seen_ids=() seen_files=()

run_nuget() (
  log=$1
  shift
  status=0
  timeout --kill-after=5 90 dotnet nuget "$@" 2>&1 < /dev/null | head -c 65537 > "$log" || status=$?
  if [[ $(stat -c %s "$log") -gt 65536 ]]; then
    printf 'NuGet %s diagnostic output exceeded 65536 bytes.\n' "${1:-unknown}" >&2
    tail -n 20 "$log" >&2
    return 1
  fi
  if [[ "$status" != 0 ]]; then
    printf 'NuGet %s failed with status %s.\n' "${1:-unknown}" "$status" >&2
    tail -n 20 "$log" >&2
  fi
  return "$status"
)

prepare_repository_trust() {
  printf '%s' '<configuration><packageSources><clear/></packageSources><config><add key="signatureValidationMode" value="require"/></config><trustedSigners><clear/></trustedSigners></configuration>' > "$trusted_config"
  run_nuget "$scratch/trust.log" trust source NuGetOfficial \
    --source-url https://api.nuget.org/v3/index.json --configfile "$trusted_config"
  [[ $(stat -c %s "$trusted_config") -le 65536 ]]
  grep -Fq 'serviceIndex="https://api.nuget.org/v3/index.json"' "$trusted_config"
  mapfile -t certificates < <(grep '<certificate ' "$trusted_config")
  [[ ${#certificates[@]} -gt 0 && ${#certificates[@]} -le 16 ]]
  for certificate in "${certificates[@]}"; do
    fingerprint=$(printf '%s\n' "$certificate" | sed -nE 's/^ *<certificate fingerprint="([a-fA-F0-9]{64})" hashAlgorithm="SHA256" allowUntrustedRoot="false" *\/> *$/\1/p')
    [[ "$fingerprint" =~ ^[a-fA-F0-9]{64}$ ]]
    fingerprint_arguments+=(--certificate-fingerprint "$fingerprint")
  done
}

count=0
while IFS=$'\t' read -r id version filename expected content_hash; do
  [[ "$id" =~ ^[a-z0-9][a-z0-9_.-]*$ ]]
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-z0-9.-]+)?$ ]]
  [[ "$filename" =~ ^[a-zA-Z0-9_.-]+\.nupkg$ && "$expected" =~ ^[a-f0-9]{128}$ ]]
  [[ "$content_hash" =~ ^[A-Za-z0-9+/]{86}==$ ]]
  [[ -z "${seen_ids[$id]:-}" && -z "${seen_files[$filename]:-}" ]]
  seen_ids[$id]=1
  seen_files[$filename]=1
  actual=$(sha512sum "$package_dir/$filename")
  [[ "${actual%% *}" == "$expected" ]]
  original_content_hash=$(openssl dgst -sha512 -binary "$package_dir/$filename" | openssl base64 -A)
  [[ "$original_content_hash" == "$content_hash" ]]
  for attempt in {1..12}; do
    status=$(curl --silent --show-error --location \
      --proto '=https' --proto-redir '=https' --max-time 120 \
      --retry 3 --retry-all-errors --output "$scratch/remote.nupkg" \
      --write-out '%{http_code}' \
      "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg")
    if [[ "$status" == 200 ]]; then
      if [[ ${#fingerprint_arguments[@]} == 0 ]]; then prepare_repository_trust; fi
      # Config alone does not enforce an allowlist for verify. Explicit CLI
      # fingerprints authenticate the primary signer, which must be NuGet.org.
      run_nuget "$scratch/verify.log" verify "$scratch/remote.nupkg" --all \
        --configfile "$trusted_config" "${fingerprint_arguments[@]}" --verbosity normal
      [[ $(stat -c %s "$scratch/verify.log") -le 65536 ]]
      [[ $(grep -c '^Signature type:' "$scratch/verify.log") == 1 ]]
      grep -Fxq 'Signature type: Repository' "$scratch/verify.log"
      [[ $(grep -c '^Service index:' "$scratch/verify.log") == 1 ]]
      grep -Fxq 'Service index: https://api.nuget.org/v3/index.json' "$scratch/verify.log"
      mapfile -t verified_hashes < <(sed -nE 's/^Content hash: ([A-Za-z0-9+/]{86}==)\r?$/\1/p' "$scratch/verify.log")
      [[ ${#verified_hashes[@]} == 1 && "${verified_hashes[0]}" == "$content_hash" ]]
      remote_archive=$(sha512sum "$scratch/remote.nupkg")
      printf 'Authenticated NuGet.org package content: %s %s; downloaded ZIP SHA512=%s; original ZIP SHA512=%s\n' \
        "$id" "$version" "${remote_archive%% *}" "$expected"
      break
    fi
    if [[ "$status" != 404 ]]; then
      echo "Cannot verify published package: HTTP $status for $id $version" >&2
      exit 1
    fi
    if [[ "$mode" == preflight ]]; then break; fi
    if [[ "$attempt" == 12 ]]; then
      echo "Published package is still unavailable: $id $version" >&2
      exit 1
    fi
    sleep 10
  done
  count=$((count + 1))
done < "$package_dir/packages.tsv"
[[ "$count" -gt 0 ]]
echo "Verified $count package identities ($mode); repository signatures may change downloaded ZIP bytes, not qualified content."
