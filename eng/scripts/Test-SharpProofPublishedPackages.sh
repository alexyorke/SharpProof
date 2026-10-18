#!/usr/bin/env bash
set -euo pipefail

mode=${1:?Expected preflight or published}
package_dir=${2:?Expected package directory}
[[ "$mode" == preflight || "$mode" == published ]]
[[ -s "$package_dir/packages.tsv" ]]
scratch=$(mktemp -d)
trap 'rm -rf -- "$scratch"' EXIT
count=0
while IFS=$'\t' read -r id version filename expected extra; do
  [[ -z "$extra" && "$id" =~ ^[a-z0-9][a-z0-9_.-]*$ ]]
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-z0-9.-]+)?$ ]]
  [[ "$filename" =~ ^[a-zA-Z0-9_.-]+\.nupkg$ && "$expected" =~ ^[a-f0-9]{128}$ ]]
  actual=$(sha512sum "$package_dir/$filename")
  [[ "${actual%% *}" == "$expected" ]]
  for attempt in {1..12}; do
    status=$(curl --silent --show-error --location \
      --proto '=https' --proto-redir '=https' --max-time 120 \
      --retry 3 --retry-all-errors --output "$scratch/remote.nupkg" \
      --write-out '%{http_code}' \
      "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg")
    if [[ "$status" == 200 ]]; then
      actual=$(sha512sum "$scratch/remote.nupkg")
      if [[ "${actual%% *}" != "$expected" ]]; then
        echo "Published package bytes differ: $id $version" >&2
        exit 1
      fi
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
echo "Verified $count package identities ($mode)."
